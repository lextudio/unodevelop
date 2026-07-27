# Integration Testing

Conventions ported from `OpenDevelop/doc/technotes/integration-testing.md` (opendevelop-sync.md
Phase 1.2) and adapted to UnoDevelop's app (`UnoDevelop.exe`/`SharpDevelop.csproj`,
`net10.0-desktop`) instead of `SharpDevelop.exe`.

## What this suite is

`src/Tests/UnoDevelop.IntegrationTests` is a single xunit.v3 project (Microsoft.Testing.Platform
native, not VSTest) that boots the real UnoDevelop app as a child process and drives it end-to-end
over an in-process REST API called the "DevFlow agent" (port 9227 by default). There is no mocked
`IWorkbench`, no fake pads, no fake project system — the whole app runs for real, and tests assert
on what it actually did (opened a solution, ran a build, listed projects, etc.).

The tradeoff is that this suite is slow (one shared app instance per run, but booting it takes real
seconds — the full 35-test run currently takes ~56s) and must be run explicitly, never as part of a
fast inner loop.

## Shared fixture and collection

Every test class:

```csharp
[Collection("UnoDevelop app")]
public sealed class SomeTests
{
    readonly UnoDevelopAppFixture _app;
    public SomeTests(UnoDevelopAppFixture app) => _app = app;
}
```

`UnoDevelopAppFixture` starts **one** UnoDevelop process for the entire test run; every test class
in the collection shares it. Don't add a test class that skips `[Collection("UnoDevelop app")]` —
it will either hang waiting for its own app instance to bind the same port, or run concurrently
against the shared one and corrupt other tests' state.

The fixture exposes `InvokeAsync`/`InvokeStringAsync` (POST to the DevFlow `actions/{action}`
endpoint) and fixture-path properties such as `FixtureSolutionPath`.

Prerequisite before running anything in this project:

```bash
dotnet build src/Main/SharpDevelop/SharpDevelop.csproj -c Debug
```

## Adding a new DevFlow-driven test case

There is no native-dialog automation for the DevFlow agent, so flows that would normally start
from a menu command with a file picker need a DevFlow action that bypasses the dialog and calls
the underlying service directly.

1. Add the action as a `[DevFlowAction("ide-xxx", Description = "...")]`-attributed method in
   `src/Main/SharpDevelop/UnoDevelopDevFlowActions.cs` (or an AddIn-specific
   `<AddIn>DevFlowActions.cs` file for AddIn-only actions, following the `ide-<addin>-<verb>`
   naming already used, e.g. `ide-get-project-property`).
2. Add a fixture under `src/Tests/fixtures/<Name>/` if the flow needs project/solution content,
   and a `LocateXxx()`/property pair on `UnoDevelopAppFixture` following the existing ones.
3. Add the test class: `[Collection("UnoDevelop app")]`, constructor takes `UnoDevelopAppFixture`,
   call `_app.InvokeAsync(...)`, assert on the returned `JsonElement`.

## Running the suite

Verified working (both actually boot the app and run all 35 tests, not just compile):

```
dotnet run --project src/Tests/UnoDevelop.IntegrationTests --no-build
dotnet src/Tests/UnoDevelop.IntegrationTests/bin/Debug/net10.0/UnoDevelop.IntegrationTests.dll
```

`dotnet test src/Tests/UnoDevelop.IntegrationTests` does **not** work yet, unlike OpenDevelop's
equivalent project. OpenDevelop makes plain `dotnet test` work via a repo-wide `global.json`
`"test": { "runner": "Microsoft.Testing.Platform" }` opt-in. Adding that same opt-in to
UnoDevelop's `global.json` was tried and reverted: it forces *every* test project in the repo onto
the MTP runner, and `UnoDevelop.Core.Tests` is still VSTest/NUnit-based
(`NUnit`/`NUnit3TestAdapter`) — the opt-in immediately broke `dotnet test src/Tests/
UnoDevelop.Core.Tests` with "global.json defines test runner to be Microsoft.Testing.Platform...
UnoDevelop.Core.Tests.csproj [is] using VSTest test runner." This project's own
`TestingPlatformDotnetTestSupport=true` and `Microsoft.NET.Test.Sdk`-free package set are already
in place and correct — the only missing piece is the global.json opt-in, which can't land until
`UnoDevelop.Core.Tests` migrates off NUnit/VSTest too. Tracked as follow-up Phase 1 work.

### Running a single test class or method

Don't use `dotnet test --filter "FullyQualifiedName~Foo"` — that's VSTest filter syntax and this
MTP/xunit3 project doesn't honor it the same way; it silently runs the entire suite instead. Use
the xunit v3 runner's own filter flags, passed after `--`:

```bash
dotnet run --project src/Tests/UnoDevelop.IntegrationTests --no-build -- -class "UnoDevelop.IntegrationTests.SolutionExplorerTests"
dotnet run --project src/Tests/UnoDevelop.IntegrationTests --no-build -- -method "UnoDevelop.IntegrationTests.BuildTests.BuildFixtureSolution_Succeeds"
```

Other useful flags (see `-- -help`): `-namespace "name"`, `-trait "name=value"`, `-list tests`,
`-verbose`. Wildcards (`*`) are supported at the start/end of `-class`/`-method`/`-namespace`
values.

Because of the shared single-app-instance collection, never run two invocations of this project
concurrently — they'll both try to bind the same DevFlow port (9227, override via
`DEVFLOW_AGENT_PORT`) and one will lose.

## Current coverage (as of Phase 1 of opendevelop-sync.md)

Verified passing end-to-end (35 tests, 0 failures): `BuildTests`, `DebuggerIntegrationTests`,
`DevFlowAgentTests`, `GitAddInTests`, `ProbeTests`, `ProjectPropertyTests`,
`SolutionExplorerTests`, `TestPanelTests`, `UnitTestingTests`, `VBBindingTests`,
`XamlBindingTests`. This already covers the Phase 0 surface (solution open/close, build
solution/is-building) end-to-end for real, not just by static code reading.

Gaps: no test exercises the Solution Explorer right-click context menu itself (hit-test → flyout
→ command dispatch) — `BuildTests` calls the `ide-build-solution` DevFlow action directly, which
proves the build pipeline works but not that the menu item that triggers it is reachable/enabled
in the UI. Adding that would need either a UI-tree-reading DevFlow action (see OpenDevelop's
`od.ui.tree`/`GetUITreeAsync` pattern, not yet ported here) or a dedicated
`ide-context-menu-items`-style action that returns the resolved menu for a given tree node. Tracked
as follow-up Phase 1 work, not done in this pass.

## Code coverage

Deferred — see opendevelop-sync.md Phase 1.4. Do this only once the harness itself (this doc) is
stable; coverage numbers on a shifting harness aren't actionable.
