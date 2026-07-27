# File/Project Template System — Port Plan

Goal: "New Item"/"New Project" support for UnoDevelop, backed by the same engine `dotnet new` and
modern Visual Studio use — **`Microsoft.TemplateEngine`**, not SharpDevelop's or MonoDevelop's own
(different, older, proprietary) template formats.

## 0. Why `Microsoft.TemplateEngine`, not SharpDevelop's own template classes

SharpDevelop already has a portable, linkable template model
(`externals/SharpDevelop/src/Main/Base/Project/Templates/` — `FileTemplate.cs`, `TextTemplate.cs`,
`ProjectTemplate.cs`, ~750 lines, zero WPF dependency) and MonoDevelop has its own
(`MonoDevelop.Ide.Templates`, used by `T4FileTemplate`/`FileTemplateHost` — see
docs/t4-templating.md §3). Both are `.xml`-descriptor-plus-placeholder-substitution systems
specific to their own IDE, predating the modern `dotnet new`/`template.json` standard.

Decision (per direction): use `Microsoft.TemplateEngine` instead of reviving either legacy system.
Confirmed via NuGet that the relevant packages are real, stable, and versioned alongside the .NET
SDK (latest stable `10.0.301`, matching this project's `net10.0-desktop` target):

- `Microsoft.TemplateEngine.Abstractions` — host/template interfaces (`ITemplateEngineHost`, etc.)
- `Microsoft.TemplateEngine.Edge` — the bootstrapper/entry point real hosts (the `dotnet new` CLI,
  Visual Studio, Rider) use to discover and instantiate templates
- `Microsoft.TemplateEngine.Orchestrator.RunnableProjects` — the generator that understands
  `template.json`-described templates (what essentially every real-world `dotnet new` template
  uses)
- `Microsoft.TemplateEngine.Utils` — host-implementation helpers

Trade-off vs. linking SharpDevelop's own template classes: more upfront integration work (need a
real `ITemplateEngineHost` implementation, not just linking existing files), but:

- Template format (`template.json`) is the actual industry standard, not a format only this IDE's
  ancestor ever used.
- Can point at the **same global template cache `dotnet new install` populates**
  (`~/.templateengine`) — a template a user installs via the `dotnet` CLI shows up in UnoDevelop
  too, for free, no extra packaging step.
- One engine for both file templates and project templates (SharpDevelop/MonoDevelop both split
  these into separate class hierarchies).
- No proprietary XML schema to invent, document, or maintain going forward.

The UI work (a "New Item"/"New Project" dialog) is identical either way — neither legacy system
nor Template Engine changes that half of the job.

## 1. Design

- **`UnoTemplateEngineHost`** (`ITemplateEngineHost` implementation, via
  `Microsoft.TemplateEngine.Edge.DefaultTemplateEngineHost` — not the obsolete
  `Microsoft.TemplateEngine.Utils` one of the same name) — identifies UnoDevelop to the engine
  (host identifier `"unodevelop"`, version), analogous in spirit to how `UnoNuGetProject` adapts
  `NuGet.ProjectManagement.NuGetProject` to this codebase's own project model
  (docs/nuget-manager.md slice 1) rather than reusing an upstream IDE's own adapter.
- **Template discovery**: `Microsoft.TemplateEngine.IDE`'s `Bootstrapper` — **not**
  hand-constructing `Microsoft.TemplateEngine.Edge.EngineEnvironmentSettings` directly, which
  finds nothing on its own (see §2 slice 1 status: it doesn't register the default
  generator/provider components that make templates discoverable — `Bootstrapper` does that for
  you, which is the whole reason this higher-level package exists for IDE hosts). Bootstrapper
  already surfaces any `dotnet new install`-managed template packages under our host identity;
  later:
  - point it at the SDK's own bundled templates too (see §2 slice 1 status — `console`/`classlib`
    don't show up by default), and
  - a bundled set of first-party templates shipped with UnoDevelop itself (e.g. a basic
    "Text Template (.tt)" item template, folding in docs/t4-templating.md §3's file-template
    need).
- **Template instantiation**: given a selected template + target directory + parameter values
  (project name, namespace, etc.), the engine generates files on disk; UnoDevelop's job is to then
  add the generated files to the project (`ProjectService`/`IProjectService.ProjectItemAdded`,
  same event-driven refresh path already used by `CustomToolsService`, see docs/t4-templating.md
  §2) and open the primary output file in the editor.
- Decoupled snapshot pattern (same precedent as `LanguageServiceProjectSnapshot`,
  `UnoNuGetProject`): template metadata read for listing/display should be a plain DTO
  (`TemplateSummary`-like record: identity, name, description, tags, parameters) independent of
  the live engine objects, so listing/filtering logic is unit-testable without a real template
  package installed.

## 2. Proposed slice order

1. ✅ **DONE** — **Engine wiring, discovery only, no UI**. `UnoTemplateEngineHost`
   (`src/Main/Base/Src/Templates/UnoTemplateEngineHost.cs`) + `TemplateDiscoveryService`
   (`TemplateDiscoveryService.cs`), wrapping `Microsoft.TemplateEngine.IDE.Bootstrapper` — the
   real IDE-facing entry point, confirmed against real installed templates on this machine (42
   found: MAUI, Avalonia, Uno Platform, Azure Functions, ASP.NET Identity variants, etc.), not
   just "it compiles." `TemplateSummary` is the decoupled DTO
   (`ITemplateInfo` → identity/short name/name/description/tags), same precedent as
   `NuGetSearchResult`. Covered by `TemplateDiscoveryServiceTests.cs` (asserts discovery finds
   *something* well-formed and sorted — deliberately not asserting a specific template exists,
   since installed template packages vary per machine, unlike NuGet's fixed nuget.org search).
   **Status note — one real gap found**: the base .NET SDK's own bundled templates
   (`console`/`classlib`/etc.) did **not** show up via `Bootstrapper`'s default settings on this
   machine, even though `dotnet new list` (the CLI) shows them — they apparently come from the
   SDK install location's own template folder, which isn't in `Bootstrapper`'s default scan path
   the way `dotnet new install`-managed packages are. Needs its own small investigation (probably
   pointing at `dotnet --list-sdks`' resolved SDK path) before slice 3 UI ships, so "New Item"
   isn't missing the most basic templates on a clean machine.
2. ✅ **DONE** — **Instantiate a template into a folder**. Given a `TemplateSummary` + target
   directory + parameter values, `TemplateDiscoveryService.InstantiateAsync()` generates files on
   disk via the engine, returning a `TemplateInstantiationResult` (success/error, output directory,
   primary output paths). `GetCreationEffectsAsync()` dry-runs without writing files (the same
   result shape, populated from `ITemplateCreationResult.CreationEffects`). Verified by
   `TemplateInstantiationTests.cs`: a fixture template (`sourceName` + symbol `replaces`) is
   installed as a temp package, instantiated, and the output file is checked for correct
   name-substitution. The dry-run variant asserts paths are returned but no directory is created.
   Both tests use an isolated host identity (per-run GUID) to avoid cross-test pollution.
3. ✅ **DONE** — **"New Item" dialog**. `NewItemDialog.xaml` + `.xaml.cs` (small WinUI ContentDialog
   under `UnoDevelop.Templates`): ListView of discovered templates filtered by
   `Tags["type"] == "item"`, with a name TextBox and "Add"/"Cancel" buttons. Wired to the
   Solution Explorer "Add > New Item..." context menu entry via
   `NewItemSolutionExplorerCommand` → `UnoSolutionExplorerController.AddNewItem()`. On
   confirmation: instantiates the template into the target directory (resolved from the selected
   tree node), refreshes the solution tree, and opens the primary output in the workbench.
   Error handling: the dialog shows loading-failure or zero-template messages inline; the
   controller catches instantiation failures and reports via `IMessageService.ShowError`.
4. ✅ **DONE** — **"New Project" dialog**. `NewProjectDialog.xaml` + `.xaml.cs` (WinUI ContentDialog,
   filters by project-type templates, name + location fields). Wired to "Add > New Project..."
   in the Solution Explorer context menu via `NewProjectSolutionExplorerCommand` →
   `UnoSolutionExplorerController.AddNewProject()`. On confirmation: creates the project directory,
   instantiates the template, finds the generated `.csproj`, and adds it to the current solution
   via `ISolutionFolder.AddExistingProject()`. If no solution is open, creates a new `.slnx`
   solution around the project and opens it.

Deferred / open questions:

- ✅ **Bundled first-party templates (initial)**: UnoDevelop now ships a built-in
  **"Text Template (.tt)" item template** under
  `src/Main/SharpDevelop/Templates/Bundled/TextTemplate` and auto-installs it into
  `Microsoft.TemplateEngine` when opening "Add > New Item..." if the package isn't already
  installed. This closes the T4 new-item bootstrap gap on clean machines where no item template
  packs are present yet.
- ✅ **Template parameter UI (initial simple version)**: both "New Item" and "New Project"
  dialogs now expose an optional multiline `key=value` editor; parsed pairs are passed through to
  `TemplateDiscoveryService.InstantiateAsync(...)` as template parameters. This intentionally
  avoids dynamic symbol typing for now (choice/bool/computed), but unblocks templates requiring
  extra symbols beyond `name`/`sourceName`.
- ✅ **Project-template solution wiring (implemented)**:
  - If a template generates a `.sln`/`.slnx` and no solution is open, UnoDevelop opens that
    generated solution directly.
  - If a template generates one or more project files and a solution is already open, UnoDevelop
    adds all generated projects (not just the first one), targeting the selected solution folder
    when available.
  - If no solution is open and the template only generates project files, UnoDevelop creates a
    wrapper `.slnx` and adds all generated projects.
