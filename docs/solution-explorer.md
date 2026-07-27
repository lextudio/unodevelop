# Solution Explorer Migration Plan

## Goal

Bring UnoDevelop Solution Explorer back toward SharpDevelop's Project Browser model instead of growing a parallel Uno-only tree builder.

The target is not a pixel-for-pixel WinForms port. The target is to reuse SharpDevelop's project tree semantics, project item model, context menu paths, state model, and command behavior, while replacing only the host-specific view layer with Uno/WinUI controls.

## Current State

The current `SolutionExplorerTreeBuilder` is useful but temporary. It mixes several concerns:

1. solution item traversal
2. project file XML parsing
3. physical directory scanning
4. node display decisions
5. partial icon selection
6. partial context menu state

This is why the tree diverges from SharpDevelop and Visual Studio:

1. project files such as `.csproj` appear as ordinary child nodes
2. references and packages are not represented as first-class nodes
3. linked files are only marked with text
4. `DependentUpon` children are only partially nested
5. missing and ghost files are not modeled
6. project item types are flattened into file/folder nodes
7. Show All Files cannot match SharpDevelop because project membership state is missing

## Reuse Strategy

Prefer reusing SharpDevelop code in this order:

1. upstream project contracts and item types
2. upstream `ProjectItem`, `FileProjectItem`, reference item classes, and metadata behavior
3. upstream Project Browser node semantics (`ProjectNode`, `DirectoryNode`, `FileNode`, `FileNodeStatus`, special folders)
4. upstream addin paths and command enablement state
5. Uno-only visual controls and templates

Do not port WinForms `TreeNode` UI directly. Instead, extract the semantics into a host-neutral adapter:

1. SharpDevelop node semantics become `SolutionExplorerNodeModel`
2. Uno renders those models with `TreeViewNode`
3. commands still use SharpDevelop addin paths and owner state

## Target Architecture

### Model Layer

Introduce an Explorer model that mirrors SharpDevelop's node concepts:

1. `Solution`
2. `SolutionFolder`
3. `Project`
4. `ReferencesFolder`
5. `PackagesFolder`
6. `Reference`
7. `PackageReference`
8. `Folder`
9. `File`
10. `DependentFile`
11. `LinkedFile`
12. `MissingFile`
13. `GhostFile`

Each node should carry:

1. display name
2. physical path if any
3. bound `ISolutionItem` or `ProjectItem`
4. SharpDevelop context menu path
5. SharpDevelop/owner state flags
6. icon key
7. project membership state

### Project Data Layer

Move project tree construction away from raw XML and directory scans where possible.

Preferred source order:

1. real `IProject.Items`
2. evaluated `MSBuildBasedProject` items
3. project XML fallback only for data not yet surfaced through upstream objects
4. physical file system only for Show All Files / ghost nodes

### View Layer

The Uno view should be a rendering adapter:

1. convert model nodes to `TreeViewNode`
2. bind icons and labels
3. display linked/missing/ghost states
4. keep context menu routing through addin paths

The view must not decide project semantics.

## Milestones

### Milestone 1 - Stop The Most Visible Divergence

1. remove `.csproj` from normal project child display
2. add `References` and `Packages` grouping nodes
3. render `Reference`, `ProjectReference`, and `PackageReference` entries
4. keep current file tree behavior otherwise

This is intentionally small and should be done before deeper refactoring.

Status: implemented in `SolutionExplorerTreeBuilder` and `SolutionExplorerNodeContext`.

### Milestone 2 - Project Item Tree From Upstream Items

1. build files from `IProject.Items`
2. preserve item type and metadata
3. support `Link`
4. support `DependentUpon`
5. support missing files

Status: partially implemented against the current `UnoProjectService.ProjectDisplayItem` pipeline.
Loaded projects now prefer upstream `IProject.Items` / `FileProjectItem` data for files, links, `DependentUpon`, references, and package references. XML parsing remains as a fallback for project paths that do not have a loaded `IProject`.
Linked files now have their own node kind, `DependentUpon` files render as children of their parent file node, and explicit missing `Include` / `Update` items are preserved as missing file nodes.
Nodes created from loaded projects now keep the original upstream `ProjectItem` on the node context. The Properties pad uses that binding to expose item type, include, link, and `DependentUpon` metadata.

Remove From Project now prefers the bound upstream `ProjectItem` (`project.Items.Remove(item)` + `project.Save()`) when the node carries one and it belongs to a loaded project, falling back to the XML/path manipulation only for non-loaded projects and directories.

References, project references, and package references now have distinct node kinds (`Reference`, `ProjectReference`, `PackageReference`) with their own context menu paths (`ReferenceNode`, `PackageReferenceNode`) and command state (`RemovableReference`, `OpenReference`). Remove works against the bound `ProjectItem`; project references also support Open (opens the referenced `.csproj`). The Properties pad now surfaces `HintPath`, `Version`, and `Private` metadata.

Remaining work: move the fallback XML parser behind the model builder; distinguish assembly vs project references produced by the fallback (non-loaded) XML path, which still tag project references correctly but lack a bound item for Remove.

### Milestone 3 - Show All Files Parity

1. distinguish in-project files from physical-only files
2. render physical-only files as ghost nodes
3. hide ghost nodes unless Show All Files is enabled
4. support include/exclude commands against the project file

Status: partially implemented. Project content nodes now keep a set of in-project physical paths and, when Show All Files is enabled, merge physical-only files back into the tree as `GhostFile` nodes. Physical-only folders created only to contain ghost files render as `GhostFolder` nodes. Ghost files are openable but are not treated as project members for rename, delete, or remove-from-project commands.

Include In Project and Exclude From Project are now available from file context menus. Include first removes matching `Remove` entries and only adds an explicit `Include` when there is no matching project item. Exclude reuses the existing remove-entry path so physical files stay on disk and become ghost files when Show All Files is enabled.

Remaining work: decide whether Show All Files should enumerate every physical file or keep the current practical filter that skips build output and common hidden infrastructure folders, then broaden membership commands to directories and special project item types.

### Milestone 4 - Replace Local Builder With Adapter

1. keep `SolutionExplorerTreeBuilder` only as a compatibility facade
2. introduce a SharpDevelop-inspired node model builder
3. move context menu and command state onto node model objects
4. remove ad hoc XML decisions from the view path

Status: started. `SolutionExplorerNodeModel` now exists as the host-neutral node representation, and project child nodes are converted from model objects into Uno `TreeViewNode` instances at the boundary.

## Execution Notes

Short term fixes are acceptable only when they remove visible divergence and point toward the model above. Avoid adding more UI-only heuristics to `SolutionExplorerTreeBuilder` unless the same behavior can later move into the model adapter without changing external behavior.

The first implementation pass should complete Milestone 1.
