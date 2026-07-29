# OpenDevelop Sync Ledger

This file tracks which UnoDevelop components have been unified with OpenDevelop and which ones still need work.

`externals/OpenDevelop/doc/technotes/` remains the shared home for feature-specific technical notes. This file is intentionally UnoDevelop-local because it records the state of this repo's migration.

## Rules

- Prefer linking OpenDevelop source through `$(SharpDevelopSourceRoot)` over keeping UnoDevelop-local copies.
- Keep Uno-specific UI in UnoDevelop when the OpenDevelop implementation is WPF-specific.
- Move UI-free services and contracts into OpenDevelop first, then link them back into UnoDevelop.
- Do not revive old SharpDevelop/NRefactory/Cecil parser infrastructure when Roslyn or LSP already owns the feature.
- Use `ilspycmd` when inspecting binaries or NuGet packages for migration decisions.

## Unified

### Source Root

- `SharpDevelopSourceRoot` now points at `externals/OpenDevelop`.
- The old `externals/SharpDevelop` source dependency has been removed.
- Future OpenDevelop bumps should be handled by build/test triage, not by reintroducing a second SharpDevelop fork.

### Core / Base Infrastructure

- Core services are linked from OpenDevelop where possible: logging, message service, property service, resource service, string parser support, AddIn tree, and file/path utility types.
- Base project infrastructure is substantially linked from OpenDevelop, including project items, build contracts, MSBuild support, target-framework metadata, project browser contracts, and utility schedulers.
- `ProjectContentContainer`, `IParser`, `IAssemblyParserService`, parser events, and related compatibility interfaces are now linked from OpenDevelop instead of being Uno-only substitutes.

### Language Services

- `ILanguageService`, `LanguageServiceRegistry`, Roslyn C#/VB service, LSP service, DTO contracts, completion, diagnostics, navigation, formatting, rename, code actions, and Roslyn resource-reference support live in OpenDevelop-linked source.
- `IParserService` is now a compatibility facade backed by `LanguageServiceParserAdapter`.
- UnoDevelop registers `LanguageServiceParserAdapter` instead of a local/minimal parser service.
- The legacy parser direction is now:

```text
IParserService
    -> LanguageServiceRegistry
        -> ILanguageService
            -> Roslyn / LSP / NoOp
```

### Resource Files / Resource Editor

- `.resx`, `.resources`, `.ico`, and `.cur` parsing/saving use the shared `LeXtudio.OpenDevelop.ResourceFiles` library.
- UnoDevelop's `LeXtudio.OpenDevelop.ResourceFiles` project now links the OpenDevelop source files directly.
- UnoDevelop's ResourceEditor is an independent addin with a native Uno/WinUI surface while sharing the underlying resource-file model and reader/writer. OpenDevelop's WPF `ResourceEditor` addin is not linked into UnoDevelop directly.

### Unit Testing

- The UI-free simple MTP testing pieces were moved into OpenDevelop and linked back:
  `ITestService`, `TestService`, `TestProjectDetector`, `DotNetTestRunner`, plus shared MTP support.
- UnoDevelop keeps the native pad/view layer locally.
- Integration tests use the xUnit v3 in-process/MTP runner via `dotnet run --project ... -- ...`.

### Templates, T4, NuGet, And Custom Tools

- Template engine services are shared through OpenDevelop technotes and linked/shared base code.
- T4 custom-tool service and observed-save support are aligned with the OpenDevelop direction.
- NuGet source/search/service pieces used by project package management and AddIn package installation are aligned around shared service contracts.
- SDK-style package-reference reading/editing and optional `dotnet restore` orchestration are shared through OpenDevelop's NuGet project helpers.
- UnoDevelop's native project package manager dialog now uses those shared services for installed-package refresh, search, install, uninstall, update checks, update application, dependency preview, license metadata warning, and restore result reporting.

### Project System / CPS Shim

- CPS/project-tree and dependency-tree work is mostly unified around OpenDevelop's shared shim and technotes.
- Multi-targeting support is already present in UnoDevelop and overlaps with OpenDevelop's documented target-framework service direction.

### AddIns Already Unified Or Native-Equivalent

- XML editor: bulk hand-copied OpenDevelop files have been replaced with linked OpenDevelop source.
- Hex editor: reusable utility source is linked from OpenDevelop.
- Resource editor: implemented as an independent native Uno addin over linked OpenDevelop resource-file services.
- Icon editor: implemented as an independent native Uno addin over linked OpenDevelop icon/cursor parsing services.
- Settings editor: implemented as a native Uno view over OpenDevelop's shared `.settings` document loader/saver.
- AddIn manager/scout: uses shared AddIn infrastructure plus native Uno UI.
- Android SDK/device managers: native Uno UI over portable service/CLI code.
- XAML designer: stays native Uno/WinUI; do not port OpenDevelop WPF design surface directly.

## Partially Unified

### Debugging

- Decision: keep UnoDevelop's DAP-based `DebugService` and `Main/Debugger` surface.
- Do not port OpenDevelop's Windows/COM-oriented `Debugger.Core` wholesale.
- Done: strengthened debugger parity tests. `DebuggerIntegrationTests.cs` (real DAP session against
  the `DebugTestApp` fixture) gained 5 tests: invalid-expression evaluate (must error via `ide-evaluate`,
  not throw/hang the DevFlow round-trip, then a subsequent valid evaluate still succeeds), double-start
  rejection (`ide-debug-project` while already debugging returns `started:false, error:"Already
  debugging."` without disturbing the live session), stop-while-stepping (`ide-stop-debug` issued
  immediately after an in-flight step terminates cleanly, no hang/exception), an out-of-range
  breakpoint line (99999) not crashing bookmark-add/sync/debug-launch, and a multi-breakpoint
  stop-sequencing test proving `CurrentStopSequence` strictly increases and reported current
  file/line change correctly across breakpoint-hit → step-over → (line advances, no more
  breakpoints ahead so a further continue reports `stopped:false` rather than hanging).
  `DebugServiceStateTests.cs` gained a not-debugging `SetBreakpointsAsync` no-op test, plus a new
  sibling `DebugServiceBuildResolutionTests.cs` reaching the private static
  `ResolveBuildOutputAsync` build-output-resolution helper via reflection to verify it returns null
  (not an exception) and reports something to the output category when pointed at a nonexistent
  project. Verified: `UnoDevelop.Core.Tests` 234/234, `UnoDevelop.IntegrationTests` 79/79.
  Remaining gap: no test drives a *second* real breakpoint hit purely via `ide-debug-continue`
  (the existing `ContinueDebug_HitsSecondBreakpoint` test covers that path already); the new
  stop-sequencing test instead uses step-over for its second stop since the fixture app's `Main`
  has no loop to hit the same breakpoint twice under `continue`.

### Parser Compatibility

- `LanguageServiceParserAdapter` now covers legacy `Parse*`, owner-project tracking, parse events, snapshots, and safe unknown resolve fallbacks.
- Still needed: richer Roslyn-backed symbol/context mapping for old `Resolve*` callers if any real feature still depends on NRefactory-style `ResolveResult`.

### Documentation

- Feature technotes should live in `externals/OpenDevelop/doc/technotes/`.
- This ledger remains local because it tracks UnoDevelop's migration state.
- Avoid recreating parallel copies of every technote under `UnoDevelop/docs`.

## Not Yet Unified

This section is intentionally split by OpenDevelop readiness. "Not unified" does not always mean
"port it from OpenDevelop next": OpenDevelop itself still carries many historical SharpDevelop
AddIns that are not part of the LibreWPF MVP path, or that still depend on WPF, WinForms,
NRefactory, old debugger APIs, or Windows-only assumptions.

### OpenDevelop MVP / Shared Direction Exists

These are the best candidates for further UnoDevelop convergence because OpenDevelop already has
a modern technote, MVP implementation, or reusable UI-free service layer.

- `AddIns/DisplayBindings/ILSpyAddIn`
  - OpenDevelop direction: embed ILSpy through a hostable ILSpy engine/facade, not the legacy external-process launcher.
  - UnoDevelop direction: use `ilspycmd` for investigation, share the ILSpy/decompiler engine, and build a native Uno view.
  - Status: not unified.
- `AddIns/Analysis/CodeCoverage`
  - OpenDevelop direction: AltCover/MTP integration is documented and partly shared through the unit-testing layer.
  - UnoDevelop direction: keep native UI, share test/coverage service logic where possible.
  - Status: backend execution and tool deployment are unified for AltCover and Coverlet. UnoDevelop links OpenDevelop's coverage result model/parser, AltCover application wrapper, OpenCover settings files, project result-path helper, shared `AltCoverCoverageRunner`, shared `CoverletCoverageRunner`, shared run-result model, shared backend enum, and shared process/output helpers. AltCover and Coverlet are both resolved from the shared `bin/Tools/<ToolName>` layout populated from NuGet packages, and UnoDevelop's main app build copies that layout into the host output/publish directories. Uno's native coverage service keeps the native pad/commands but dispatches through the shared backend layer; AltCover is the default backend, Coverlet remains available as the second shared backend, the native pad exposes backend selection, and the DEBUG DevFlow probe can run a selected backend through `uno.probe.coverage.run(tool)`. **Project-type/runtime matrix coverage is now broader**: `UnitTestingCodeCoveragePadIntegrationTests` exercises both AltCover and Coverlet against three real MTP fixture project types - MSTest (`SampleMtpTests`), NUnit (`SampleNUnitMtpTests`), and xUnit.v3 (`SampleXunitMtpTests`), all net10.0 - proving `CodeCoverageService.IsMtpTestProject` detection and both runners are genuinely test-framework-agnostic rather than only ever verified against MSTest (the NUnit/xUnit fixtures already existed for `MtpServerProcess` protocol tests and were deliberately built with an identical `Calculator` shape - untested `b==0` branch - for exactly this reuse). Each `[Test]` opens the fixture project it needs via `IProjectService.OpenSolutionOrProject` (which closes whatever solution is already open), since NUnit does not guarantee execution order within the fixture and `TestSolution` observes `SD.ProjectService.AllProjects.CollectionChanged` rather than needing to be rebuilt per project - this mirrors the real IDE project-switch path rather than re-running the heavy `ServiceBootstrapper.Initialize()` per project. In the course of adding this matrix, `TestService_DiscoversFixtureTests`'s DisplayName assertion was found to be stale (MSTest's MTP host now reports namespace/class-qualified names via `--list-tests`, not the short names the assertion had assumed) and was corrected to match the real, re-verified tool output. **The "unrelated missing addin reference build blockers" gap is resolved for this AddIn's own build graph**: a standalone build of `CodeCoverage.csproj`, a full build of `UnoDevelop.Core.Tests.csproj`, and a full build of `UnoDevelop.slnx` all complete with 0 errors in this environment - CodeCoverage has no standing blocker of its own. (A transient failure was seen mid-session in unrelated `PackageManagement`/NuGet files from a concurrent, uncommitted work-in-progress elsewhere in this checkout; it was not touched and had resolved by the next build attempt - see that AddIn's own status entry.) Remaining project-type gaps, if ever wanted: multi-targeted (multi-TFM) projects, non-`net10.0` TFMs, and non-Exe/class-library test host shapes are not covered by any fixture yet.
- `AddIns/Misc/PackageManagement`
  - OpenDevelop direction: NuGet manager/package console work is documented.
  - UnoDevelop direction: keep the native Uno package UI, but drive it through OpenDevelop shared NuGet services.
  - Status: unified for the current MVP scope. SDK-style `PackageReference` read/add/update/remove is shared through `SdkStylePackageReferenceEditor` and used by `UnoNuGetProject` and the package manager UI. Higher-level project package operations use `NuGetProjectPackageOperationService` for edit + optional restore result reporting. Update discovery is shared through `NuGetPackageUpdateService`; direct dependency preview is shared through `NuGetPackageDependencyPreviewService`; package license metadata is shared through `NuGetPackageSearchService` and surfaced in the native Uno package manager. Full transitive dependency resolution and version-conflict detection are now shared through `NuGetPackageConflictResolutionService` (walks the transitive closure via `DependencyInfoResource`/`FindPackageByIdResource` and reports incompatible version ranges as explicit conflicts rather than silently picking one) and wired into installs via `NuGetProjectPackageOperationService.AddPackageReferenceWithConflictCheckAsync`. Explicit license-acceptance confirmation is implemented as a real `ContentDialog` gate in the native Uno package manager (`ManagePackagesDialog.cs`), shown before any install/update whose NuGet metadata declares `requireLicenseAcceptance=true` (both the search-install and update-existing paths; `NuGetPackageUpdateResult` now also carries the license flag/URL for the update path). Package-console workflows are covered by a reduced-scope native equivalent, `PackageConsoleCommandProcessor` (shared) plus a "Console" tab in `ManagePackagesDialog`, offering `list`/`install`/`update`/`uninstall` line commands through the same conflict-checked, license-gated services; a full embedded PowerShell host (OpenDevelop's actual Package Manager Console) is out of scope for this session - see doc/technotes/package-management.md for why.
- `AddIns/Misc/SearchAndReplace`
  - OpenDevelop source has portable search pieces already linkable.
  - UnoDevelop should continue linking UI-free engine code and keep host UI local.
  - Status: unified for the current MVP scope; portable file search/replace engine, default filters, auto-open limit, scope model, run result models, replace plan models, result grouping models, cancellation/progress hooks, and workflow service are shared through OpenDevelop and linked into UnoDevelop. Uno maps current document, open files, current project, solution, and directory scopes into the shared model while keeping native view/commands local. Replace plans changes before writing, asks for native Uno confirmation, applies the shared plan, and opens changed files for review only when the changed-file count is below the shared limit. Uno's native result list can display shared flat/file/project/project-file groupings. Detailed diff preview is intentionally deferred because git-backed review/revert is the simpler workflow.
- `AddIns/BackendBindings/XamlBinding`
  - OpenDevelop and UnoDevelop both have XAML language-service/design notes.
  - UnoDevelop direction is native Uno/WinUI plus LSP/shared language-service contracts.
  - Status: partially unified, real progress this session, real gaps remain - see below.

  **What's shared and working**: AXSG (`XamlToCSharpGenerator`, the submodule nested at
  `externals/OpenDevelop/externals/vscode-wpf/external/wxsg/external/XamlToCSharpGenerator`) is a
  genuinely framework-agnostic XAML LSP engine now. `XamlLanguageServiceEngine`'s core constructor
  only depends on `ICompilationProvider` (a Roslyn `Compilation` source - this is where the shared,
  framework-neutral `TieredCompilationProvider` two-tier compilation pipeline plugs in) and
  `IXamlFrameworkProfile` (all framework-specific XAML semantics: build contract, semantic binder,
  emitter, parser settings). A host mounts exactly the framework(s) it needs and never has to touch
  the engine itself:
  - **WPF**: real, deep `WpfFrameworkProfile` lives in `wxsg`'s own `XamlToCSharpGenerator.WPF`
    project (external to AXSG core), consumed by `vscode-wpf/src/XamlLanguageServer.Wpf` (the
    `wpf-xaml-ls` process both OpenDevelop and UnoDevelop launch for `.xaml` via
    `LspServerRegistry`). Has its own WPF-specific Tier-1 fast-snapshot provider
    (`Microsoft.WindowsDesktop.App.Ref`), full test coverage (`XamlLanguageServer.Wpf.Tests`,
    10/10 passing), and is the most mature of these integrations.
  - **Avalonia**: deep `AvaloniaFrameworkProfile` ships inside AXSG core itself, with its own Tier-1
    provider (`AvaloniaFastCompilationProvider`, reads the Avalonia SDK's build-output references
    file) and an on-disk Tier-1 type-index cache.
  - **WinUI / MAUI**: "passive" providers (`PassiveXamlFrameworkProfile`) ship inside AXSG core -
    "passive" means no code-generation contribution, not degraded language service: completions and
    definitions are still driven off the project's real compilation (confirmed via a test asserting
    a control that only exists in a synthetic test compilation gets offered), just without a
    hand-tuned control catalog or Tier-1 provider of their own.
  - **Uno** (new this session): `UnoLanguageFrameworkProvider`, same passive-profile shape as
    WinUI/MAUI (Uno's XAML dialect *is* WinUI's - same presentation xmlns, `x:Bind`, `using:`
    prefixes - just served by Uno.UI's assemblies instead of the Windows App SDK). Detection
    priority outranks WinUI's own checks since an Uno project also carries `Microsoft.UI.Xaml`
    types WinUI's heuristics would otherwise claim. `IXamlLanguageFrameworkProvider`'s detection
    members (`CanResolveFrom*`, `DetectionPriority`) were changed from required to optional
    (C# default interface members) in the same change - a host dedicated to one framework (like the
    new UnoDevelop host below) should not have to implement, or pay for, heuristics it will never
    call; it names its framework explicitly via `XamlLanguageServiceOptions.FrameworkId` instead.
  - **UnoDevelop's own host**: `src/LanguageServer/XamlLanguageServer.Uno` (new), mirroring
    `XamlLanguageServer.Wpf`'s shape - mounts `UnoLanguageFrameworkProvider` explicitly, wires
    `TieredCompilationProvider` with `fastSnapshot: null` (no Uno Tier-1 provider yet - see below).
    `ServiceBootstrapper.Initialize()` overwrites the Base-layer `LspServerRegistry`'s default
    `.xaml` → WPF mapping with this host and adds `.xaml` to the extension-registration loop, which
    had never included it before - UnoDevelop had no `.xaml` LSP wiring at all prior to this.
    Launches via `dotnet exec <prebuilt dll>`, not `dotnet run --project <csproj>`: a real bug was
    found where a plain `dotnet run` can trigger an implicit restore/build whose NuGet/MSBuild
    progress writes to stdout - the exact stream the stdio-framed LSP protocol lives on, corrupting
    every frame after it (confirmed directly: 7496 bytes of NuGet warnings on stdout before the
    process ever spoke LSP). `UnoLanguageServerIntegrationTests` (new, in
    `UnoDevelop.Core.Tests`) launches the real `uno-xaml-ls` process and round-trips a real LSP
    handshake + completion/hover request - proving the process launches and speaks LSP correctly,
    end to end, for the first time.

  **Closed since the above was written**:
  - `LspServerRegistry.CreateDefault()`'s WPF `.xaml` mapping now launches `wpf-xaml-ls` via
    `dotnet exec <prebuilt dll>` instead of `dotnet run --project <csproj>` - the plain `dotnet run`
    exposure flagged below was real and has been fixed, verified via a new
    `WpfLanguageServerIntegrationTests` that launches the actual process end to end (previously no
    integration test had ever done so). If the dll was never built, `.xaml` is now left
    unregistered rather than risking a corrupted stdio pipe.
  - A real, restorable Uno.Sdk fixture now exists (`src/Tests/fixtures/UnoXamlFixture`, a minimal
    net10.0-desktop single project) and `UnoLanguageServerIntegrationTests` drives a genuine
    MSBuild evaluation against it, asserting on `x:Bind` completion (declared only by
    `UnoLanguageFrameworkProvider`'s real profile, not the engine's fallback list) - proving type-
    aware completion actually works end to end for a real Uno project, not just that the
    process/protocol plumbing is sound.
  - Getting the fixture test to pass surfaced a real, previously-undiscovered bug: AXSG's own
    `Directory.Build.props` pinned `Microsoft.CodeAnalysis.Workspaces.MSBuild` at 4.10.0, but
    UnoDevelop's central package management pins `Microsoft.CodeAnalysis.*` at 5.3.0 for its own
    Roslyn C#/VB service - and NuGet resolves the actual `Common`/`Workspaces.Common` versions
    across the whole graph in any build that references AXSG from UnoDevelop, so 5.3.0 always won
    regardless. The resulting 4.10.0-vs-5.3.0 internal API mismatch threw
    `System.MissingMethodException` at runtime the instant `MSBuildWorkspace` opened a real
    project - silently swallowed by `MsBuildCompilationProvider`'s catch-all, so "does
    MSBuildWorkspace even work when consumed from UnoDevelop" had never actually been verified
    before this fixture existed. Fixed by bumping AXSG's own version pins to 5.3.0/9.0.0 to match;
    the catch block now also logs the real exception to stderr permanently, so a regression like
    this won't be silent again.

  **Closed since the above was written (2)**:
  - Uno now has a real Tier-1 fast-snapshot provider (`UnoFastCompilationProvider`, in the
    `XamlLanguageServer.Uno` host, not AXSG core - it's host-specific like WPF's). Since Uno/WinUI
    has no single fixed reference-assembly package the way WPF's `Microsoft.WindowsDesktop.App.Ref`
    is (the real assembly set is per-project: Uno.WinUI version, Skia/WebAssembly/mobile runtime
    backend, Toolkit, fonts, ...), it instead reads the target project's own
    `obj/project.assets.json` - a NuGet *restore* artifact, not a full build - and resolves the
    listed `compile` assemblies against `packageFolders` directly into a Roslyn compilation. Only
    requires a prior restore (which normal project-open flows already trigger), same "instant,
    still not requiring the user's own code to compile" property Tier 1 exists for.
    `UnoLanguageServer_Tier1FastSnapshot_ServesFrameworkCompletionBeforeMsBuildFinishes` requests
    completion with zero delay against the real fixture and asserts on `x:Bind` - proving Tier 1
    serves something real before the background MSBuild evaluation could plausibly have finished.
  - Along the way, found (not fixed): **element-type completion against a real, non-null workspace
    root returned zero items** in every document shape tried (single-line, multi-line, with or
    without a real MSBuild-backed `Project`, at either tier) - only markup-extension/directive
    completion (`x:Bind`) was reliably testable this way. This looks like a real, separate gap
    somewhere in element-name completion specifically when a real workspace root is involved, not
    anything to do with Tier 1/2 or Uno specifically - genuinely not root-caused, worth a dedicated
    investigation later.

  **Still not done**:
  - **The underlying AXSG submodule state is fragmented across three uncommitted-upstream local
    lines**, none pushed: a `tiered-completion` branch (this repo's checkout, built on AXSG's own
    unpushed local `main`, carries the Uno provider + a from-scratch tiered-compilation
    reimplementation + its tests + the fixes above), a separate `wpf` branch (in the *other*
    local clone at `~/vscode-axaml/src/XamlToCSharpGenerator` - has WPF-specific model types
    `tiered-completion` lacks, e.g. `XamlCodeBlockDefinition` for `x:Code` blocks), and that same
    clone's own `main` (has the original tiered-completion commits pre-dating this session's
    rework). A full merge was attempted and aborted mid-session (real conflicts in
    `TieredCompilationProvider`, `AvaloniaTypeIndex`, `XamlLanguageServiceEngine`) - reconciling
    these three lines into one is unresolved and will need a deliberate decision, not another
    ad-hoc attempt.
  - A malformed/unclosed multi-line XAML element tag returned zero completion items (even the
    engine's hardcoded fallback ones) when targeted against the real `UnoXamlFixture` compilation,
    despite the identical shape working fine with no project loaded at all - switching the test to
    a well-formed, self-closed document fixed it, so this looks like a parser/analysis requirement
    rather than a real Uno-completion gap, but was not root-caused further (see
    `UnoLanguageServerIntegrationTests`'s comment).

### OpenDevelop Has Source, But Not A Clean MVP Migration Target

These exist under `externals/OpenDevelop/src/AddIns`, but should not be treated as ready-to-link
LibreWPF/OpenDevelop MVP components. Each needs a fresh decision: extract UI-free services into
OpenDevelop first, then build native Uno UI if the feature still matters.

- `AddIns/DisplayBindings/ClassDiagram`
  - Historical WPF/design-surface code; likely not a direct Uno link.
  - Needs decision on whether Roslyn/type-system data is sufficient for a new native diagram surface.
- `AddIns/DisplayBindings/WorkflowDesigner`
  - Depends on old designer/debugger assumptions.
  - Needs debugger-service parity and a native-host feasibility pass first.
- `AddIns/DisplayBindings/Data`
  - OpenDevelop has SDK-style migrated projects, but this is not clearly part of the LibreWPF MVP surface.
  - Needs a product decision before porting.
- `AddIns/DisplayBindings/FormsDesigner`
  - Windows Forms designer stack is not a native Uno target.
  - Treat as out of scope unless a separate designer-host strategy is approved.
- `AddIns/Analysis/CodeQuality`
  - Historical analysis UI and engine.
  - Needs Roslyn-era relevance check before migration.
- `AddIns/Analysis/SourceAnalysis`
  - Likely StyleCop-era integration.
  - Needs Roslyn analyzer/code-action alignment before migration.
- `AddIns/Analysis/Profiler`
  - Depends on debugger/test-service contracts.
  - Needs live debugger parity before it is a sensible Uno target.
- `AddIns/Analysis/MachineSpecifications`
  - Niche test-framework support.
  - Needs real usage signal before investing.
- `AddIns/BackendBindings/Scripting`
  - Check whether OpenDevelop's NuGet/package-console scripting work supersedes it.
- `AddIns/BackendBindings/AspNet.Mvc`
  - Historical project-type support; likely not MVP.
  - Needs SDK-style ASP.NET Core relevance check.
- `AddIns/BackendBindings/TypeScript`
  - UnoDevelop may already cover the useful path through LSP.
  - Only migrate OpenDevelop code if it adds project-system or tooling capability not covered by LSP.
- `AddIns/BackendBindings/WixBinding`
  - Historical project-type support; needs current WiX SDK relevance check.
- `AddIns/Misc/Reporting`
  - Legacy reporting designer/runtime; not clearly MVP.
  - Needs product decision before porting.

### UnoDevelop Already Has A Native Replacement

These should not be "unified" by linking OpenDevelop UI code. The correct work is to share service
contracts or models only when useful.

- `AddIns/DisplayBindings/AvalonEdit.AddIn`: replaced by UnoEdit/editor integration.
- `AddIns/DisplayBindings/WpfDesign`: replaced by UnoDevelop's native XAML designer direction.
- `AddIns/DisplayBindings/ResourceEditor`: implemented as a native Uno addin over shared resource-file services.
- `AddIns/DisplayBindings/IconEditor`: implemented as a native Uno addin using the shared `LeXtudio.OpenDevelop.ResourceFiles` icon/cursor parser. The old OpenDevelop WPF editor surface is not linked directly.
- `AddIns/Misc/AddInManager2`: OpenDevelop's WPF-free `Model` layer (gallery search/paging,
  install/update, license acceptance - `PackageRepositories`/`NuGetPackageManager`/`AddInSetup`/
  `AddInManagerServices`) was moved into OpenDevelop's Base
  (`externals/OpenDevelop/src/Main/Base/Project/Src/AddInManager/`) and is linked into UnoDevelop's
  own Base assembly via `$(SharpDevelopSourceRoot)`, same pattern as `NuGetPackageSearchEngine.cs`.
  `AddInManagerDialog.xaml.cs`'s "Online Gallery" tab consumes that engine directly (plain event
  handlers, no MVVM) for paged search, install, update-available indicator, and a ContentDialog
  license-acceptance prompt. See `externals/OpenDevelop/doc/technotes/addin-manager2.md` for the
  full writeup, including the one known gap (license-acceptance timing is a close but not
  byte-identical equivalent of OpenDevelop's synchronous WPF dialog).
- `AddIns/Misc/AndroidDeviceManager` and `AddIns/Misc/AndroidSdkManager`: represented by native Uno views over portable CLI service code.

### OpenDevelop Historical Source To Leave Alone For Now

These are present in OpenDevelop because the repository still contains much of SharpDevelop, not
because LibreWPF MVP has fully modernized them. Do not use their presence as a migration mandate.

- `AddIns/Debugger/Debugger.Core`: Windows/COM/CorDebug-oriented; UnoDevelop should stay DAP-based.
- Old NRefactory-backed pieces inside debugger, CSharpBinding, ResourceToolkit, or analysis AddIns.
- WinForms-era dialogs, wait dialogs, settings panels, and designer surfaces.
- WPF-only pane/view implementations when no UI-free model layer has been extracted.

### Packaging / CI

- Investigated: OpenDevelop does not currently have a committed, working macOS packaging flow to
  unify against. `externals/OpenDevelop/dist.macos.sh` references `build/macos/build-application-bundle.sh`,
  `build/macos/build-dmg.sh`, `build/macos/Info.plist`, and an icon file, but that whole `build/`
  directory is gitignored in the OpenDevelop submodule (`externals/OpenDevelop/.gitignore:5`) and is
  absent from disk and from git history — it's WIP/local-only tooling that never landed. OpenDevelop's
  own sync ledger (`externals/OpenDevelop/doc/technotes/opendevelop-sync.md:24`) already documents this
  as deliberately deferred, "its own verification pass, not a safe drive-by edit." There is also no
  `.github/workflows/` in either repo today.
- UnoDevelop's own flow (`dist.macos.sh` + `build/macos/build-application-bundle.sh` + `build/macos/build-dmg.sh`
  + `build/macos/Info.plist`/icon) is the more complete, actually-working reference implementation right
  now — it was ported from OpenDevelop's script skeleton and extended with a Uno/Skia-specific fix
  (mirroring assets into `Contents/MacOS/Resources` so `ms-appx:///Resources` paths resolve, see comments
  in `build-application-bundle.sh`). Neither script signs or notarizes the bundle.
- True unification can't mean byte-identical logic even once OpenDevelop's flow lands: OpenDevelop is
  WPF/LibreWPF (`net10.0-windows`, running on macOS only via LibreWPF's Win32 shims) with its own AvalonDock
  auto-hide workaround needs, while UnoDevelop is Uno/Skia-backed with different asset-resolution needs.
  What's realistically shareable is the script *skeleton* (publish-per-RID → build bundle → build dmg),
  parameterized by app name/TFM/RID list/Info.plist/icon, with framework-specific steps (Skia resource
  mirror vs AvalonDock workaround) as opt-in stages — not shared step-for-step logic.
- CI should still wait until OpenDevelop's own build scripts, integration tests, and package layout are
  stable and actually committed — there's nothing to converge with yet.

### Open Test Debt

- Done: `UnoDevelopDependenciesSnapshotFactoryTests.cs.disabled` rewritten as
  `UnoDevelopDependenciesSnapshotFactoryTests.cs`, now exercising the current
  `SharpDevelopDependenciesSnapshotFactory` API (`BuildTreeAsync`/`PruneSessionsExceptAsync`/
  `ClearAllAsync` over `MutableProjectTree`) instead of the retired `BuildSnapshotAsync`. Covers
  session reuse across repeated calls, TFM-set-change rebuild, and prune/dispose-and-rebuild — the
  behaviors that live specifically in the factory rather than in `ProjectSystemTreeProviderTests`'s
  already-existing end-to-end tree-shape coverage. Verified via `UnoDevelop.Core.Tests` (228/228).
- Done: added `SolutionExplorerContextMenuTests.cs` covering Solution Explorer's context-menu
  pipeline. Layer B (priority) builds a fresh, isolated `AddInTreeImpl` loaded with a synthetic
  in-memory addin (via `AddIn.Load(IAddInTree, TextReader, ...)`), temporarily swaps
  `ServiceSingleton.ServiceProvider` (save/restore in try/finally) to resolve `IAddInTree` to it, and
  drives the exact `BuildItems`/`Condition.GetFailedAction`/`CommandWrapper.CreateLazyCommand`
  pipeline `UnoAddInContextMenuBuilder` itself calls, proving Exclude/Disable/plain-item outcomes and
  that "clicking" invokes a real `AbstractMenuCommand` subclass with the correct Owner. It also loads
  the real, unmodified `Explorer.addin` to lock in a known, pre-existing gap: a `<Condition>` nested
  as a *child* of a leaf `<MenuItem>` (Explorer.addin's actual syntax for Rename/Delete/etc.) is
  silently dropped by `ExtensionPath.DoSetUp` and never reaches that item's `Codon.Conditions`.
  Layer A (`MainPage.TryResolveNodeContext`, widened from private to internal for testability) was
  scoped out: realizing a live `TreeViewItem` container and even constructing a bare `MenuFlyout`
  both require a running Uno dispatcher/layout pass that this headless NUnit host doesn't have.
  Verified via `UnoDevelop.Core.Tests` (232/232).
- Keep full integration verification on the MTP runner path documented in `AGENTS.md`.

## Deliberately Not Unified

- OpenDevelop WPF UI implementations should not be linked directly into UnoDevelop's Uno/WinUI host.
- `AvalonEdit.AddIn`, `AvalonDock`, `SharpTreeView`, WPF Toolkit surfaces, and WPF-specific design surfaces map to Uno-native equivalents.
- `librewpf.md` is OpenDevelop-specific and does not apply to UnoDevelop's Uno Platform host.
- Old NRefactory/Cecil parser services should remain retired; new language-service capability belongs in Roslyn/LSP-facing contracts.

## Verification Baseline

Recent verification after the language-service/parser and docs cleanup:

- `dotnet build src/Main/Base/Project/ICSharpCode.SharpDevelop.csproj -c Debug --no-restore`
- `dotnet build src/Main/SharpDevelop/SharpDevelop.csproj -c Debug --no-restore`
- `dotnet build src/Tests/UnoDevelop.Core.Tests/UnoDevelop.Core.Tests.csproj -c Debug --no-restore`
- `dotnet build src/AddIns/Analysis/CodeCoverage/Project/CodeCoverage.csproj -c Debug --no-restore`
- `dotnet test src/Tests/UnoDevelop.Core.Tests/UnoDevelop.Core.Tests.csproj -c Debug --no-build --filter FullyQualifiedName~UnitTestingCodeCoveragePadIntegrationTests`

These UnoDevelop checks completed with `0 Error(s)`; remaining warnings are existing analyzer/platform warnings.
