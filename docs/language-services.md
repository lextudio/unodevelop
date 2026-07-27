# Language Services — Roslyn + LSP Plan

Goal: replace SharpDevelop's original NRefactory-based language services with two
purpose-built backends — **Roslyn** for C#/VB (first-party, in-process) and a generic
**LSP client** for every other language (out-of-process, via each language's own
server) — while keeping the existing UnoEdit editor surface and SharpDevelop service
contracts (`IParserService`, `ICompletionItemList`, quick-info, navigation) as the
UI-facing seam.

## 0. Where we actually stand today

**Status (updated after implementation): all 7 originally-planned slices are done for both
backends — see §4 for the per-slice checklist. Two extensions beyond the original plan were
also added: per-TFM Roslyn `ProjectId` splitting with an editor UI to switch the active TFM
(§2.1 status note), and a VS-style class/member navigation bar with icons (§5, new).
Remaining work is completeness hardening on already-working paths, not missing slices —
see §6 for the current list.**

Below is the original greenfield assessment, kept for history; §4 now carries the live
status.

- NRefactory is referenced by only three UnoDevelop files, and none of them do real
  analysis:
  - `src/Main/Base/Src/Editor/AvalonEditTextEditorAdapter.uno.cs` — `CreateCompletionBinding`
    and `ShowCompletionWindow` are typed against NRefactory/`ICompletionItemList` but
    both return `null!` (stubs). **Superseded**: real completion/quick-info/diagnostics/
    go-to-definition/format now flow through `MainPage.xaml.cs`, not through these
    stubs — they remain dead code from the old NRefactory seam.
  - `src/Main/SharpDevelop/Services/UnoProjectService.cs` — a `using` only, no calls.
  - `src/Main/SharpDevelop/Debugger/Visualizers/IVisualizerDescriptor.cs` — interface
    shape only.
- `grep` for `Microsoft.CodeAnalysis`, `LanguageServer`, `OmniSharp`, `textDocument/`,
  `JsonRpc` across `src/` and `docs/` returns nothing **(historical — Microsoft.CodeAnalysis
  is now referenced by `ICSharpCode.SharpDevelop.Uno.csproj` and used throughout
  `src/Main/Base/Src/LanguageServices/Roslyn/`; `LanguageServer`/`JsonRpc` still return
  nothing — LSP work hasn't started)**.
- `design.md` and the `session*.md` logs never mention Roslyn, LSP, or a NRefactory
  replacement — the project-system slices (`project-system.md`) are the only precedent
  for how this codebase runs a large, upstream-anchored, slice-by-slice migration.
- The project model is already MSBuild-evaluated and complete enough to drive a real
  compiler: `MSBuildBasedProject` (`externals/SharpDevelop/src/Main/Base/Project/Src/Project/MSBuildBasedProject.cs`)
  exposes evaluated items (`GetItemsOfType(ItemType.Compile/ProjectReference/Reference)`),
  target framework(s), and output paths through the same MSBuild evaluation used by the
  CPS dependency bridge (`project-system.md`, slices 30-36). That evaluation output is
  exactly the input shape Roslyn's `Microsoft.CodeAnalysis.Workspace` / a `.csproj`-driven
  LSP server needs (source files, references, defines, LangVersion, TFM).
- Target framework is `net10.0-desktop` (Uno Skia desktop host, not WASM/mobile) —
  a real, full .NET process. Roslyn's in-process assemblies (`Microsoft.CodeAnalysis.CSharp`,
  `.Workspaces`) and a child LSP server process both run here without platform
  restrictions. This would be a materially harder plan on WASM; it isn't one here.

**Net effect**: there is no working NRefactory system to keep feature-parity with — the
practical bar is "ship real completion/diagnostics for the first time," not "don't
regress." That changes the risk profile: we can design straight for the target shape
without a parallel-run/fallback period against existing NRefactory behavior.

## 1. Two backends, one seam

```text
UnoEdit (TextEditor)
   │  buffer text, caret, selection
   ▼
ILanguageService (per-document, resolved by file extension / project language)
   │
   ├── CSharpVBLanguageService   → Microsoft.CodeAnalysis (Roslyn), in-process
   │
   └── LspLanguageService        → generic LSP client, one server process per language
```

Both backends implement the same small internal contract (not the raw LSP shape, and
not raw Roslyn types) so the editor and SharpDevelop's `IParserService`/completion/
quick-info/navigation contracts don't need to know which backend is behind a given
document:

```csharp
interface ILanguageService
{
    Task<CompletionResult> GetCompletionsAsync(DocumentId doc, int offset, CancellationToken ct);
    Task<QuickInfo?> GetQuickInfoAsync(DocumentId doc, int offset, CancellationToken ct);
    Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DocumentId doc, CancellationToken ct);
    Task<IReadOnlyList<NavigationTarget>> GoToDefinitionAsync(DocumentId doc, int offset, CancellationToken ct);
    Task<IReadOnlyList<TextEdit>> FormatAsync(DocumentId doc, TextSpan? span, CancellationToken ct);
    void OnTextChanged(DocumentId doc, TextChange change);
}
```

`CompletionResult`/`QuickInfo`/`Diagnostic`/`NavigationTarget`/`TextEdit` are our own
DTOs (position = line/column, matching UnoEdit's `TextDocument`), not `Microsoft.CodeAnalysis`
or LSP wire types — so the seam has exactly one adapter on each side (Roslyn→DTO,
LSP JSON→DTO) instead of leaking either backend's model into the editor.

A `LanguageServiceRegistry` picks a backend per open document by file extension and
registered project language:

| Extension | Backend | Notes |
| --- | --- | --- |
| `.cs` | `CSharpVBLanguageService` | via Roslyn `Workspace` |
| `.vb` | `CSharpVBLanguageService` | same Workspace, `Microsoft.CodeAnalysis.VisualBasic` |
| everything else with a registered server (`.ts`/`.js`, `.py`, `.rs`, `.go`, `.json`, `.md`, ...) | `LspLanguageService` | one server process per language, keyed by server command from config |
| unregistered | none | falls back to the existing lexical-only highlighting, no regression from today |

## 2. C#/VB backend — Roslyn

### 2.1 Workspace shape

Use a custom `Workspace` subclass fed directly from `MSBuildBasedProject`'s already-evaluated
state, **not** `Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace`. Reasons:

- `MSBuildWorkspace` re-invokes MSBuild evaluation itself (its own `ProjectLoader`), which
  would run a second, separate evaluation pipeline alongside SharpDevelop's own
  `MSBuildBasedProject` evaluation — two sources of truth for the same project, prone to
  drifting out of sync (e.g. after a `ProjectItemAdded` event that only one side observes).
- SharpDevelop's project system already tracks live evaluation and change events
  (`ProjectItemAdded`/`ProjectItemRemoved`/`UnoProjectChangeWatcher.ChangedExternally`, the
  same events the CPS dependency bridge subscribes to per `project-system.md` slice 46).
  Reusing those events to incrementally update a Roslyn `Solution` is the same pattern
  already proven for the dependency tree, and keeps exactly one evaluation pipeline.

Concretely (**as designed** — see status notes below for what actually shipped):

- `UnoDevelopRoslynWorkspace : Workspace` — one per open `IProjectService.CurrentSolution`.
- On solution load: for each `MSBuildBasedProject`, build a Roslyn `ProjectInfo` from
  `GetItemsOfType(ItemType.Compile)` (documents), `GetItemsOfType(ItemType.ProjectReference)`
  (project-to-project references, resolved to the sibling Roslyn `ProjectId`),
  `GetItemsOfType(ItemType.Reference)` + resolved `PackageReference` assembly paths (from
  the dependency bridge's already-resolved metadata — no separate NuGet resolution needed),
  and `CompilationOptions`/`ParseOptions` from evaluated `LangVersion`/`Nullable`/`DefineConstants`/
  `TargetFramework`.
- On `ProjectItemAdded`/`ProjectItemRemoved` for a `Compile` item: `Workspace.OnDocumentAdded`/
  `OnDocumentRemoved`.
- On UnoEdit text change: `Workspace.OnDocumentTextChanged` (debounced per keystroke batch,
  not per keystroke, to avoid re-running full-document diagnostics on every character).
- Multi-targeted projects (`net8.0;net9.0`, already handled by the dependency bridge, slice
  32) get one Roslyn `ProjectId` per TFM slice, matching the existing per-TFM dependency
  tree shape, so completion/diagnostics can differ per TFM the same way NuGet resolution
  already does.

**Status — what actually shipped** (`CSharpVBLanguageService`,
`src/Main/Base/Src/LanguageServices/Roslyn/CSharpVBLanguageService.cs`):

- ✅ `AdhocWorkspace` (not a custom `Workspace` subclass) fed from `LanguageServiceProjectSnapshot`
  (`LanguageServiceProjectSnapshot.FromProject`) — one Roslyn project per project file, with
  compile documents, project references, metadata references, `LangVersion`/`Nullable`/
  preprocessor symbols all wired.
- ✅ **Corrected** (an earlier pass here under-reported this): `MainPage.xaml.cs` *does*
  subscribe to `IProjectService.ProjectItemAdded`/`ProjectItemRemoved`
  (`MainPage.xaml.cs:378-379`, handler `OnProjectItemCollectionChanged`), and reacts to each
  event by calling `LoadLanguageServiceProjectAsync` for the affected project. The granularity
  differs from the original spec, though: instead of a single `Workspace.OnDocumentAdded`/
  `OnDocumentRemoved` call for the one item that changed, the handler re-snapshots the whole
  project (`LanguageServiceProjectSnapshot.FromProject`) and diffs it against the current
  document set (`RemoveProjectDocumentsMissingFromSnapshot`) — functionally correct, but O(project
  size) per edit rather than O(1).
- ✅ Text-change sync: `OnTextChanged`/`UpsertDocumentAsync`, driven from `MainPage`'s editor
  sync path (`SyncLanguageServiceDocumentAsync`).
- ✅ **DONE** (superseding the "left as its own design pass" note from an earlier revision) —
  per-TFM Roslyn `ProjectId` splitting. `LanguageServiceProjectSnapshot.FromProjectAllTargetFrameworks`
  reads the project's evaluated `TargetFrameworks`/`TargetFramework` property and, for a
  multi-targeted project, re-evaluates a dedicated `Microsoft.Build.Evaluation.Project` per TFM
  with the `TargetFramework` global property pinned (`TryEvaluateForTargetFramework`) — a real
  per-TFM evaluation, not the dependency bridge's static-XML-condition parsing (which only
  approximates per-TFM item visibility for the tree view, not real compiler options). Falls back
  to the project-wide snapshot if a given TFM's evaluation throws, so one bad TFM doesn't take
  down the others. `CSharpVBLanguageService` keys projects and per-document `RoslynDocumentId`
  variants by `(projectFileName, targetFramework)`, tracks each project's known TFMs and its
  currently "active" one (`GetTargetFrameworks`/`GetActiveTargetFramework`/
  `SetActiveTargetFramework`), and resolves every feature call (completion/diagnostics/hover/
  go-to-def/format) to the active TFM's document variant. Project-to-project reference
  resolution (`ResolveReferencedProjectId`) prefers an **exact TFM match** between the
  referencing slice and the referenced project (the common real-world case: both projects
  multi-target the same TFM set) and only falls back to the referenced project's "active" TFM
  when no exact match is loaded — not full NuGet nearest-compatible-framework resolution
  (netstandard fallback, version-range matching), which stays out of scope as a real build-system
  concern beyond an editor's approximate compilation context.
- ✅ **DONE** — editor UI to switch the active TFM: a right-aligned target-framework `ComboBox`
  in the navigation bar (§5), shown only when `GetTargetFrameworks` reports more than one TFM;
  selecting a value calls `SetActiveTargetFramework` and reschedules a diagnostics refresh.
  Modeled after Visual Studio's "active target framework" selector for multi-targeted projects.

### 2.2 Feature mapping

| SharpDevelop contract | Roslyn API |
| --- | --- |
| `ICompletionItemList` / `ShowCompletionWindow` | `CompletionService.GetCompletionsAsync` |
| Quick info / tooltips | `QuickInfoService.GetQuickInfoAsync` |
| Error list / squiggles | `Compilation.GetDiagnostics()` (no analyzers) or `Compilation.WithAnalyzers(...).GetAllDiagnosticsAsync()` (project has analyzer references) — **done**, see below |
| Go to definition | `SymbolFinder.FindSourceDefinitionAsync` / `ISymbol.Locations` |
| Find references | `SymbolFinder.FindReferencesAsync` |
| Format document | `Formatter.FormatAsync` |
| Rename symbol | `Renamer.RenameSymbolAsync` — **done**, see below |

**Status — rename** (`CSharpVBLanguageService.RenameSymbolAsync`, `ILanguageService.RenameSymbolAsync`):
resolves the symbol at the caret via `SymbolFinder.FindSymbolAtPositionAsync`, renames it with
`Renamer.RenameSymbolAsync`, then diffs old vs. new `Solution` per document to produce
`TextEdit`s keyed by absolute file path (not just the current document — every file the symbol
appears in). This closes the "needs a multi-file apply path through UnoEdit" gap the original
plan flagged: `MainPage.xaml.cs`'s `RenameSymbolAsync` (bound to F2, prompting via a
`ContentDialog` — there's no other interactive input-box facility in this codebase;
`IMessageService.ShowInputBox` is a console-only stub) applies edits to open editors via their
live `TextDocument` and directly to disk (`File.ReadAllText`/`WriteAllText`) for files that
aren't open. `LspLanguageService.RenameSymbolAsync` calls `textDocument/rename` and parses the
`WorkspaceEdit.changes` shape (not the versioned `documentChanges` shape — no configured LSP
server needs it). Multi-targeted projects: every TFM slice of a file is a separate Roslyn
`Document`, so only the first slice's edits per file path are kept (they should describe the
same text change in the common case).

**Status — third-party analyzers/source generators** (`CreateAnalyzerReferences`,
`DirectAnalyzerAssemblyLoader`): `LanguageServiceProjectSnapshot.AnalyzerAssemblyFileNames`
resolves `Analyzer` items (both in the project-wide and per-TFM evaluation paths) — the same
raw MSBuild item real CPS surfaces as `Analyzer` dependency nodes (`project-system.md` slice
21), read directly via `IProject.GetItemsOfType(new ItemType("Analyzer"))` rather than the
dependency bridge's separate static-XML parsing. Each path becomes an `AnalyzerFileReference`
attached to the Roslyn `Project` — one Workspace abstraction covers both `DiagnosticAnalyzer`s
and `ISourceGenerator`/`IIncrementalGenerator`s, and `Project.GetCompilationAsync()`
automatically runs any generators and includes their generated trees, no separate
generator-driver plumbing needed. `GetDiagnosticsAsync` uses the cheap `compilation.GetDiagnostics()`
path when a project has no analyzers, and `CompilationWithAnalyzers.GetAllDiagnosticsAsync()`
(compiler + analyzer diagnostics combined) when it does. Assemblies load directly via
`Assembly.LoadFrom` (`DirectAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader`) — resolving this
doc's own open question (§7) in favor of "simplicity, matches VS/Rider" over per-analyzer
`AssemblyLoadContext` isolation; a misbehaving analyzer takes down the whole language service,
not just itself, same trade-off VS/Rider make by default. Covered end-to-end by
`CSharpVBLanguageService_LoadsThirdPartyAnalyzer_AndSurfacesItsDiagnostics`, which compiles a
real `DiagnosticAnalyzer` to a `.dll` with Roslyn at test time and verifies its diagnostic
surfaces through `GetDiagnosticsAsync`.

### 2.3 What's explicitly out of scope for the first pass

- Multi-project incremental design-time builds beyond what `MSBuildBasedProject` already
  evaluates (i.e., no live MSBuild re-evaluation on every keystroke — only on project-file/
  item-collection changes, same cadence as the Solution Explorer tree today).

## 3. Other languages — LSP client

### 3.1 Why LSP instead of hand-rolled parsers per language

UnoDevelop's own effort should go into the *client* (one implementation, reused for every
language), not into N language-specific parser integrations. Every mainstream language
already ships a maintained LSP server (`typescript-language-server`, `pyright`/`pylsp`,
`rust-analyzer`, `gopls`, `clangd`, ...); the client only needs to speak the protocol.

### 3.2 Client shape

- `LspLanguageService : ILanguageService` — one instance per **project language**, each
  owning one child server process (LSP servers are typically single-workspace-rooted, so
  one process per project rather than per file).
- Transport: JSON-RPC 2.0 over the server's stdio (the universal LSP transport). Use
  `StreamJsonRpc` (MIT, Microsoft) rather than hand-rolling JSON-RPC framing — it already
  handles `Content-Length` header framing and request/response/notification correlation,
  which is most of the protocol-plumbing risk. **Shipped**: `StreamJsonRpc` with its built-in
  `SystemTextJsonFormatter` (not Newtonsoft.Json, to avoid adding that dependency) over
  `HeaderDelimitedMessageHandler` — see §4 slice 5.
- `LspServerRegistry` — maps file extension → `(command, args)` launch spec, configurable
  (a settings file, not hardcoded), so adding a new language is a config entry, not a code
  change: e.g. `.ts`/`.tsx` maps to `typescript-language-server --stdio`, `.py` to `pylsp`.
  If the configured command isn't found on PATH, that language silently falls back to
  lexical-only highlighting — same "no regression from today" fallback as an unregistered
  extension.
- Document sync: `textDocument/didOpen` on first open, `textDocument/didChange` (incremental,
  `TextDocumentContentChangeEvent` ranges from UnoEdit's own change events — UnoEdit already
  tracks per-edit ranges for its own undo stack) on edit, `textDocument/didClose` on tab close.
- Requests mapped 1:1 onto `ILanguageService`: `textDocument/completion`, `textDocument/hover`
  (→ `QuickInfo`), `textDocument/publishDiagnostics` (server-pushed, not request/response —
  cached per-document and returned from `GetDiagnosticsAsync`), `textDocument/definition`,
  `textDocument/formatting`.
- Process lifecycle: start on first document of that language opened in the current solution,
  send `initialize`/`initialized` with the solution's root `rootUri`, `shutdown`/`exit` when
  the last document of that language closes or the solution closes. One misbehaving/crashed
  server should only take down that language's features, never the editor or other languages
  — wrap all requests with a timeout + restart-on-crash policy, same isolation goal as the
  Roslyn side's "one Workspace per solution" not sharing state with the LSP processes.

### 3.3 What's explicitly out of scope for the first pass

- Multi-root workspace support (`workspace/workspaceFolders`) — one root per solution is
  enough for UnoDevelop's current single-solution model.
- Semantic tokens (`textDocument/semanticTokens`) replacing UnoEdit's lexical highlighter —
  worth a later slice once basic completion/diagnostics/hover are proven, since it changes
  the highlighting pipeline UnoEdit already has working today (`unoedit-highlighting-stateful-redraw`).
- Code actions / quick fixes (`textDocument/codeAction`) — no multi-file apply path was needed
  for these the way rename needed one (§2.2 has the rename status — `LspLanguageService`
  implements `RenameSymbolAsync` via `textDocument/rename`, applied through the same
  `MainPage.xaml.cs` multi-file apply path Roslyn's rename uses); code actions are deferred
  because they're a different UI surface (an actions menu at the caret) that doesn't exist yet.

## 4. Proposed slice order

Following the project-system precedent (small, buildable, test-covered slices rather than
one large branch):

1. ✅ **DONE** — **`ILanguageService` contract + `LanguageServiceRegistry`** — DTOs
   (`LanguageServiceContracts.cs`), registry (`LanguageServiceRegistry.cs`, tested by
   `LanguageServiceRegistryTests.cs`), a `NoOpLanguageService` fallback, and wiring into the
   editor's completion/quick-info/diagnostics/go-to-definition/format call sites in
   `MainPage.xaml.cs` (registered per extension in `ServiceBootstrapper.cs`). The old
   `AvalonEditTextEditorAdapter.uno.cs` NRefactory stubs are now dead code, superseded by this
   path rather than actually removed.
2. ✅ **DONE** — **Roslyn workspace, project-snapshot based** — `CSharpVBLanguageService`
   loads project(s) into an `AdhocWorkspace` from `LanguageServiceProjectSnapshot`; completion +
   diagnostics wired end-to-end. (Shipped via project snapshots + `AdhocWorkspace`, not the
   originally-specced `UnoDevelopRoslynWorkspace` subclass fed by live `MSBuildBasedProject`
   event subscriptions — see §2.1 status note.)
3. ✅ **DONE**, now matching the original spec's granularity too — **Roslyn incremental
   updates**: text-change sync (`OnTextChanged`) and quick-info + go to definition are wired.
   `MainPage.xaml.cs` subscribes to `ProjectItemAdded`/`ProjectItemRemoved` via two dedicated
   handlers (`OnProjectItemAdded`/`OnProjectItemRemoved`); for a `Compile` item, each calls a
   targeted `CSharpVBLanguageService.AddCompileDocumentAsync`/`RemoveDocument` that adds/removes
   just that one document's Roslyn `DocumentId`(s) — an O(1) operation, not the whole-project
   re-snapshot-and-diff this section previously described. Non-`Compile` item changes (References,
   ProjectReferences, ...) still fall back to a full project reload (`LoadLanguageServiceProjectAsync`)
   since those can change project-level compilation inputs, not just the document set. A new file
   is added to every TFM slice the project already has projects for (documented assumption: the
   file isn't under a per-TFM `Condition` on the `Compile` item itself — true for the SDK-style
   implicit-globbing common case; a genuinely per-TFM-conditional individual file needs a full
   reload to be picked up correctly). Covered by
   `CSharpVBLanguageService_AddCompileDocumentAsync_*`/`_RemoveDocument_*` tests in
   `LanguageServiceRegistryTests.cs`.
4. ✅ **DONE** — **Multi-project + multi-TFM Roslyn** — project-to-project references are
   wired (`ApplyProjectReferences`), and multi-targeted projects now get one Roslyn `ProjectId`
   per TFM slice, each with its own real per-TFM MSBuild evaluation, plus an editor UI to pick
   the active one (§2.1 status note, §5).
5. ✅ **DONE** (pilot scope: completion + diagnostics) — **LSP client core** —
   `LspServerRegistry` (`src/Main/Base/Src/LanguageServices/Lsp/LspServerRegistry.cs`,
   `CreateDefault()` maps `.ts`/`.tsx`/`.js`/`.jsx` to `typescript-language-server --stdio`)
   and `LspLanguageService` (`.../Lsp/LspLanguageService.cs`), wired into
   `ServiceBootstrapper.cs` (one shared process per language-server command) and into
   `MainPage.xaml.cs`'s `SyncLanguageServiceDocumentAsync`. Transport is **`StreamJsonRpc` +
   `SystemTextJsonFormatter`** (not Newtonsoft.Json — deliberately kept out of the dependency
   set) over the child process's raw stdio streams (`Process.StandardInput/OutputBaseStream`),
   framed with `HeaderDelimitedMessageHandler`. `initialize`/`initialized` handshake,
   `didOpen`/`didChange` (full-document sync, not incremental ranges — a deliberate
   simplification over §3.2's spec), `textDocument/completion`, and a
   `textDocument/publishDiagnostics` notification handler (cached per-URI, returned from
   `GetDiagnosticsAsync`) are implemented. If the configured command isn't found on PATH,
   `LspLanguageService` catches the process-start failure, logs once via `LoggingService.Warn`,
   and returns empty results forever after — the "no regression" fallback from §3.2 works as
   specified. Covered by `LspLanguageServiceTests.cs` (registry mapping + missing-command
   fallback + stubbed-feature behavior).
6. ✅ **DONE** — **LSP hover + go to definition**, plus registry expansion. `GetQuickInfoAsync`
   calls `textDocument/hover` and normalizes whichever hover-contents shape the server returns
   (`MarkupContent`, `MarkedString`, or an array of either); `GoToDefinitionAsync` calls
   `textDocument/definition` and normalizes both possible result shapes (`Location` and
   `LocationLink`) to `NavigationTarget`. `LspServerRegistry.CreateDefault()` now also maps
   `.py` to `pylsp` — added as a pure config entry with **no** change to `LspLanguageService`,
   confirming the registry is genuinely language-agnostic rather than TypeScript-specific.
7. ✅ **DONE for both backends** — **Formatting**. Roslyn: `FormatAsync` via
   `Formatter.FormatAsync`. LSP: `LspLanguageService.FormatAsync` calls
   `textDocument/formatting` for whole-document requests (`span is null`) or
   `textDocument/rangeFormatting` when a span is given, converting the server's `TextEdit[]`
   result back to our DTO. Both are wired from `MainPage.xaml.cs`'s existing
   `FormatDocumentAsync` call site (`Ctrl+K`) with no dispatch-site changes needed beyond the
   `is LspLanguageService` sync branch already added in slice 5.

All items originally listed in this slice order (§4) are now implemented. See §6 for the
current completeness list (not missing slices — hardening on already-working paths).

## 5. Editor navigation bar (beyond the original plan)

Not part of the original slice order — added in the same implementation pass as multi-TFM
support, because a "pick the active TFM" UI needs *some* per-document chrome above the editor
anyway, and Visual Studio's own multi-targeting selector lives in exactly that spot (the
class/member navigation bar). Implemented once for both:

- **Type dropdown** (left) — every type declared in the open document, sourced from
  `ILanguageService.GetDocumentOutlineAsync` (new contract method, §1). Selecting one moves the
  caret to its declaration.
- **Member dropdown** (second) — members of the selected type (methods, properties, fields,
  events, constructors); nested types are flattened into the type dropdown instead of nested
  under their declaring type, matching how a class/member nav bar reads. Selecting one navigates
  to it.
- **Target framework selector** (right-aligned) — only visible when the owning project is
  multi-targeted (§2.1); switches `CSharpVBLanguageService`'s active TFM and reschedules
  diagnostics.
- **Icons**: each dropdown row shows a 16×16 glyph per symbol kind (Class/Struct/Interface/Enum/
  Method/Property/Field/Event) plus, when Roslyn reports it, an accessibility-specific variant
  (Private/Protected/Internal — 24 additional icons). Per this project's established convention
  (`SolutionExplorerNodeContext.cs`'s icons, `Property_16x.svg`/`CSInterface_16x.svg` already in
  `src/Main/SharpDevelop/Icons/`), these are Visual Studio Image Library SVGs, added under
  `Icons/` following the same `{Name}_16x.svg` naming as the existing set — not a new asset
  source. `LspLanguageService.GetDocumentOutlineAsync` never reports `Accessibility` (LSP's
  `documentSymbol` has no such field), so LSP-backed outlines always show the plain per-kind icon.
- **Caret-follow**: the type/member selection tracks the caret (`Caret.PositionChanged`) without
  moving it back — the inverse of clicking a dropdown entry, guarded by
  `EditorViewContent.IsSyncingOutlineSelectionFromCaret` so the two directions don't fight each
  other. Containment uses a new `DocumentOutlineNode.ExtentSpan` (the full declaration span, e.g.
  the whole method body) distinct from `Span` (the name-only token used for "jump to
  declaration") — both backends populate it: Roslyn from `ISymbol.DeclaringSyntaxReferences`,
  LSP from `DocumentSymbol.range` (vs. `selectionRange` for `Span`).

**`GetDocumentOutlineAsync` implementations**:

- Roslyn (`CSharpVBLanguageService.GetDocumentOutlineAsync`): walks `Compilation.GlobalNamespace`
  symbol-by-symbol (not syntax-tree-based) filtering to symbols whose `Location.SourceTree`
  matches the open document — this makes the walk identical for C# and VB with one
  implementation, since it operates on `ISymbol`/`INamedTypeSymbol`, not per-language syntax
  node types.
- LSP (`LspLanguageService.GetDocumentOutlineAsync`): calls `textDocument/documentSymbol` and
  normalizes the response, flattening namespace/module/package/file container symbols and
  re-flattening nested types to top-level entries the same way the Roslyn backend does, so both
  backends produce outlines with the same shape.

## 6. Remaining completeness gaps

Not missing slices — hardening on already-working paths:

- Roslyn: exact-TFM project-reference matching (§2.1) still falls back to full NuGet
  nearest-compatible-framework resolution being out of scope — acceptable, not planned to close.
- LSP: only two pilot languages configured (TypeScript/JavaScript, Python) — adding more is a
  config-only change to `LspServerRegistry.CreateDefault()`; no incremental-range document
  sync (`didChange` always sends full text, §3.2); no multi-root workspace support (§3.3).
- Code actions / quick fixes: **implemented for both backends, see §8.** Remaining gaps within
  that feature specifically: Roslyn `CodeRefactoringProvider`s (non-diagnostic refactorings like
  Extract Method) and third-party analyzer assemblies' own fix providers aren't discovered —
  only built-in `CodeFixProvider`s composed from `MefHostServices.DefaultAssemblies`; LSP only
  supports actions with a literal `edit` already in the `textDocument/codeAction` response —
  `codeAction/resolve` and command-only actions aren't handled.
- Analyzer isolation: analyzers load directly into the language service's process
  (`DirectAnalyzerAssemblyLoader`), so a misbehaving third-party analyzer can take down
  completion/diagnostics for the whole solution, not just itself. Matches VS/Rider's default,
  resolving the "Open questions" item below in favor of simplicity — revisit only if this
  actually causes problems in practice.

## 8. Code actions / quick fixes plan

Triggered by: both backends' package/protocol prerequisites turned out to already be satisfied
(`ICSharpCode.SharpDevelop.Uno.csproj` already references `Microsoft.CodeAnalysis.CSharp.Features`/
`VisualBasic.Features`, which is what `CodeFixProvider`/`CodeRefactoringProvider` live in — not
just the `.Workspaces`/`.CSharp` packages `CSharpVBLanguageService` was originally built against),
so the §2.3/§3.3 "deferred" note is stale. What's actually missing is new contract surface on
both backends plus a new UI affordance — no existing call site does anything with a computed fix
today (diagnostics are read-only squiggles, §6).

### 8.1 Contract shape

Code actions can't reuse `TextEdit`/`RenameSymbolAsync`'s shape directly: a computed action is a
short-lived, backend-side object (a Roslyn `CodeAction` holds document-diff operations; an LSP
`CodeAction` may need a follow-up `codeAction/resolve` call before it has concrete edits) — it
can't be serialized across the `ILanguageService` boundary and handed back a version later the way
a plain data value could. So the shape is "list, then apply by id," mirroring how VS's own light
bulb and LSP's own `codeAction`/`codeAction/resolve` split already work, rather than a single
`GetCodeActionEditsAsync`:

```csharp
Task<IReadOnlyList<CodeActionInfo>> GetCodeActionsAsync(DocumentId documentId, TextSpan span, CancellationToken cancellationToken);
Task<IReadOnlyDictionary<string, IReadOnlyList<TextEdit>>> ApplyCodeActionAsync(DocumentId documentId, string actionId, CancellationToken cancellationToken);
```

`CodeActionInfo(string Id, string Title, bool IsPreferred)` — `Id` is opaque to the caller (UI
just needs to echo it back to `ApplyCodeActionAsync`); `IsPreferred` maps to Roslyn's own
`CodeAction` priority concept and LSP's `CodeAction.isPreferred`, for a future "apply the single
preferred fix" keyboard shortcut (VS's Ctrl+.+Enter) — not required for slice 1's menu UI.
`ApplyCodeActionAsync` returns the same `IReadOnlyDictionary<string, IReadOnlyList<TextEdit>>`
shape `RenameSymbolAsync` already returns, so it can go through the exact same multi-file apply
path (§8.4) with no new plumbing on that side.

Each backend caches its last-computed action list per document between the `GetCodeActionsAsync`
call and the `ApplyCodeActionAsync` call that follows it (keyed by the opaque id) — a fresh
`GetCodeActionsAsync` call for a document supersedes its previous cache entry, and applying a
stale/unknown id returns an empty edit map rather than throwing (same "quietly do nothing rather
than crash the editor" posture as the rest of `ILanguageService`).

### 8.2 LSP backend

Lower-risk slice — `textDocument/codeAction` is a single request/response, no MEF-style provider
discovery needed:

- Declare `textDocument.codeAction` in the `initialize` capabilities payload (currently `{}`,
  §3 status note) so servers that gate behavior on capability negotiation actually offer actions.
- `GetCodeActionsAsync`: `textDocument/codeAction` with `range` = the given span and an empty
  `context.diagnostics: []` — the servers configured today (typescript-language-server, pylsp)
  recompute their own applicable diagnostics for the given range rather than requiring the client
  to echo them back; avoids a second lossy JSON round-trip converting our cached
  `LanguageDiagnostic`s back to LSP's wire shape for a filter most servers don't actually need.
- Response items are `(CodeAction | Command)[]`. Slice 1 only supports items that carry a literal
  `edit` (a `WorkspaceEdit`) already in the response; `command`-only actions (no edit, requires the
  client to invoke a server-defined command) and actions needing `codeAction/resolve` because
  `edit` is initially absent are out of scope for slice 1 — most servers used here today
  (typescript-language-server, pylsp) return literal edits for their common quick fixes, so this
  covers the useful common case first.
- `ApplyCodeActionAsync` parses the cached action's `edit.changes` with the same
  `changes: { [uri]: TextEdit[] }` parsing `RenameSymbolAsync` already has (§2.2) — worth
  extracting into one shared private helper so there's exactly one WorkspaceEdit parser, not two
  copies that could drift.

### 8.3 Roslyn backend

Higher-risk slice, **not implemented yet** (`CSharpVBLanguageService.GetCodeActionsAsync` returns
empty for now, matching the "no regression, quietly do nothing" fallback rather than pretending
this works) — Roslyn has no single public "get me the applicable code actions for this span" API
outside VS's own internal orchestration services (`Microsoft.CodeAnalysis.CodeFixes.CodeFixService`
is `internal` in Roslyn). The real path, same technique OmniSharp/other non-VS Roslyn hosts use:

1. Discover built-in `CodeFixProvider`/`CodeRefactoringProvider` MEF exports from the same
   assembly set already composed for the workspace's `MefHostServices.DefaultHost`
   (`CSharpVBLanguageService.cs:45`) — needs a `System.Composition` `ContainerConfiguration`
   over `MefHostServices.DefaultAssemblies`, not currently a package this project references.
2. For each `CodeFixProvider` whose `FixableDiagnosticIds` intersects the diagnostics already
   computed at that span (`GetDiagnosticsAsync` already runs `compilation.GetDiagnostics()`/
   `CompilationWithAnalyzers`, §2.2), call `RegisterCodeFixesAsync` with a `CodeFixContext` and
   collect the registered `CodeAction`s; for `CodeRefactoringProvider`s, call
   `ComputeRefactoringsAsync` with a `CodeRefactoringContext` regardless of diagnostics (most
   refactorings — Extract Method, Introduce Variable — aren't diagnostic-driven).
3. Cache each computed `CodeAction` object itself (not just its title) keyed by an opaque id;
   `ApplyCodeActionAsync` calls `CodeAction.GetOperationsAsync`, applies the resulting
   `ApplyChangesOperation`s to the workspace's `Solution`, and diffs old vs. new solution per
   document the same way `RenameSymbolAsync` already does to produce `TextEdit`s.
4. Third-party analyzers already loaded via `AnalyzerFileReference` (§2.2) may ship their own
   `CodeFixProvider`s in the same assembly — worth checking whether `AnalyzerFileReference`
   already exposes fix providers for free (it's built for `DiagnosticAnalyzer`/generator loading,
   not fix-provider loading) or whether they need the same MEF discovery pass as the built-ins.

Scope for the first Roslyn slice: `CodeFixProvider`s only (skip `CodeRefactoringProvider`s), and
prove it against one well-known built-in fixer (e.g. `CS0246`/add-missing-using) with a test that
compiles a real snippet missing a using and asserts the fix's edit adds it — the same "compile a
real analyzer at test time" pattern `CSharpVBLanguageService_LoadsThirdPartyAnalyzer_...` already
established (§2.2) — before broadening to the rest of the built-in fixer set.

### 8.4 UI: actions menu

No existing affordance to build on (§6) — new surface, modeled on the existing F2-rename flow
(`MainPage.xaml.cs:2251`, `RenameSymbolAsync`) rather than a diagnostics-anchored light bulb glyph,
to ship something end-to-end fastest:

- Keyboard shortcut, same binding style as the existing F2/Ctrl+K handlers (`MainPage.xaml.cs:2040`).
  **Simplification**: VS/Rider's usual Ctrl+. is unavailable — this Uno version's `VirtualKey`
  enum has no OEM/punctuation key members at all (only letters/numbers/function/navigation keys),
  so Ctrl+Enter is used instead.
- At the caret (or over the current selection span, if any): call `GetCodeActionsAsync`; if empty,
  a status-bar message ("No code actions available", mirroring `RenameSymbolAsync`'s "Nothing to
  rename at the caret" message) — no menu shown for an empty result.
- Otherwise show a `MenuFlyout`, one `MenuFlyoutItem` per `CodeActionInfo.Title`; selecting one
  calls `ApplyCodeActionAsync` and applies the returned edits through the exact same multi-file
  apply path `RenameSymbolAsync` uses (extracted into a shared `ApplyEditsAcrossFilesAsync`
  helper as part of this slice, rather than duplicating the ~40-line open-editor/disk-write loop
  a second time). **Simplification**: the flyout anchors to the editor control
  (`FlyoutBase.ShowAt(FrameworkElement)`), not a precise caret pixel position — no existing code
  in this project converts a text offset to screen coordinates (Quick Info, the closest existing
  feature, is shown via a status-bar message rather than a positioned popup, §2.2/§4 slice 6);
  building that conversion is its own follow-up, not blocking a working menu.
- Not in scope for slice 1: an automatic light-bulb glyph that appears unprompted next to a
  diagnostic squiggle (VS/Rider's actual light-bulb UX) — that needs the diagnostics pipeline to
  carry a "this diagnostic might be fixable" hint and a gutter/inline glyph rendering surface
  neither of which exist today; the keyboard-triggered menu is the fastest path to something
  real and testable, matching this doc's own "smallest working slice first" pattern (§4).

### 8.5 Slice order

1. ✅ **DONE** — **Contract** — `CodeActionInfo` DTO, `ILanguageService.GetCodeActionsAsync`/
   `ApplyCodeActionAsync`, `NoOpLanguageService` empty implementations.
2. ✅ **DONE** — **LSP backend** (§8.2) — literal-edit code actions only, no `codeAction/resolve`
   or command-only actions. Covered by `LspLanguageServiceTests`' missing-command-fallback
   pattern (no fake server harness exists for a full round-trip test, same coverage depth as
   rename).
3. ✅ **DONE** — **Roslyn backend** (§8.3) — `CSharpVBLanguageService.CodeActions.cs`:
   `CodeFixProvider`s discovered via `System.Composition` (`ContainerConfiguration.WithAssemblies(MefHostServices.DefaultAssemblies)`,
   the same assembly set the Workspace itself was composed from), filtered by
   `FixableDiagnosticIds` against diagnostics already computed for the document
   (`ComputeRoslynDiagnosticsAsync`, extracted from `GetDiagnosticsAsync` for reuse), applied via
   `CodeAction.GetOperationsAsync` + a solution diff (`DiffSolutionsToTextEditsAsync`, extracted
   from `RenameSymbolAsync` for reuse — both need "what changed across the whole solution"
   from a before/after `Solution` pair). Added `System.Composition.Hosting`/`TypedParts`
   (pinned to 9.0.0 to match what `Microsoft.CodeAnalysis.CSharp.Features` and
   `Microsoft.VisualStudio.Composition` already pull in transitively — 8.0.0 caused an NU1605
   downgrade error). **Third-party analyzer fix providers are not discovered** — only the
   built-in fixer set composed from `MefHostServices.DefaultAssemblies`; per-project analyzer
   assemblies would need a second composition pass, left for later. Proven end-to-end (not just
   compiled) by `CSharpVBLanguageService_GetCodeActionsAsync_OffersAddMissingUsing`: a real CS0246
   (missing `using System.Collections.Generic;`) surfaces the built-in add-import fix, which is
   then actually applied and the resulting text checked — this is also what caught, during
   development, that CS0246 has *two* legitimate built-in fixes ("using ..." and "fully qualify
   ...") that both mention the missing namespace, so a naive title match picks the wrong one.
   `CodeRefactoringProvider`s (Extract Method, Introduce Variable, ...) are still out of scope,
   per §8.3's original slice-1 scoping.
4. ✅ **DONE** — **UI** (§8.4) — Ctrl+Enter menu (Ctrl+. unavailable, see §8.4), reusing a
   newly-extracted `ApplyEditsAcrossFilesAsync` shared with F2 rename.

**Status: code actions work end-to-end for both backends now** — LSP-backed languages
(TypeScript/JavaScript, Python) via literal-edit `textDocument/codeAction` results, C#/VB via
Roslyn's built-in `CodeFixProvider`s. Remaining gaps: `CodeRefactoringProvider`s (non-diagnostic
refactorings), third-party analyzer fix providers, and LSP's `codeAction/resolve`/command-only
actions — see §6.

Slices 2 and 4 are independent of slice 3 landing — the menu works end-to-end for LSP-backed
languages (TypeScript/JavaScript, Python) as soon as slices 1/2/4 ship, with C#/VB simply
reporting no actions until slice 3 follows.

## 7. Open questions

- **LSP server distribution**: do we expect users to have language servers already installed
  on `PATH` (lowest UnoDevelop maintenance burden, matches how e.g. Neovim/Helm plugins work),
  or do we want to bundle/auto-install common ones later? Starting with "PATH lookup, silent
  fallback if missing" (§3.2) keeps the first slices small; revisit once there's real usage
  data on which servers users actually want.
- **Debounce/cancellation policy** for both backends under fast typing — needs to reuse
  whatever cancellation-token discipline UnoEdit's own redraw pipeline already established
  (`unoedit-highlighting-stateful-redraw`) rather than inventing a second one.
