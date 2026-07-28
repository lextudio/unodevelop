using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.CodeCoverage;
using ICSharpCode.UnitTesting;
using ICSharpCode.UnitTesting.Mtp;
using NUnit.Framework;
using UnoDevelop.AddIns.Analysis.CodeCoverage;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests;

// Exercises the UnitTesting and CodeCoverage addins together against a real
// MTP fixture project (Tests/Fixtures/SampleMtpTests), the way a user driving
// the "Run Tests" and "Run Tests with Coverage" pad buttons back-to-back would:
// both features must agree on which project is an MTP test project, and a
// coverage run must reflect the same tests the classic ICSharpCode.UnitTesting
// backend discovers (see doc/technotes/unit-testing.md).
//
// This spawns real `dotnet` subprocesses (build/run/coverage tool) against the
// fixture project, so it is slower than the rest of the suite.
[TestFixture]
public sealed class UnitTestingCodeCoveragePadIntegrationTests
{
    private static readonly string FixtureProjectPath = ResolveFixtureProjectPath();

    [OneTimeSetUp]
    public void OpenFixtureSolution()
    {
        Assert.That(File.Exists(FixtureProjectPath), Is.True, $"Fixture project not found: {FixtureProjectPath}");

        ServiceBootstrapper.Initialize();

        var projectService = (IProjectService)ServiceSingleton.ServiceProvider.GetService(typeof(IProjectService));
        var opened = projectService.OpenSolutionOrProject(FileName.Create(FixtureProjectPath));
        Assert.That(opened, Is.True, "Failed to open the SampleMtpTests fixture project.");
    }

    [Test]
    public void TestService_DiscoversFixtureTests()
    {
        // Deliberately no [Timeout]: NUnit enforces a [Timeout] by running the method body on a
        // *different* thread than OneTimeSetUp, which breaks SDTestService.OpenSolution's
        // SD.MainThread.VerifyAccess() - the DispatcherMessageLoop created in OneTimeSetUp is bound
        // to that thread, and nothing pumps it in this headless test host, so any cross-thread
        // Invoke onto it throws instead of marshaling. Running on the same thread as OneTimeSetUp
        // avoids the cross-thread Invoke entirely (CheckAccess() is already true).
        // The registered instance, not `new SDTestService()` - a second, independent SDTestService
        // would build its own separate TestSolution/ITestProject tree from scratch instead of
        // observing the one ServiceBootstrapper.Initialize() already wired up and that real DevFlow
        // actions (ide-refresh-tests etc.) actually drive.
        var testService = SD.MainThread.InvokeIfRequired(() => (ITestService)SD.GetRequiredService<ITestService>());
        var solution = SD.MainThread.InvokeIfRequired(() => testService.OpenSolution);

        // The tree shows a fast, approximate (Roslyn-scanned) list immediately and confirms it
        // against the real MTP test host (MtpTestProject.DiscoverTestsAsync); this test cares about
        // the MTP-confirmed shape specifically, so it awaits that pass rather than asserting on
        // whichever answer happens to be in the tree first.
        var leafTests = GetConfirmedLeafTests(solution);

        // MSTest's MTP host reports short method names (not namespace/class-qualified) via
        // --list-tests, so the discovered DisplayName reflects that shape rather than inventing
        // qualification the underlying tool doesn't provide.
        Assert.That(leafTests.Select(t => t.DisplayName),
            Is.EquivalentTo(new[] { "Add_ReturnsSum", "Divide_ReturnsQuotient" }));
    }

    [TestCase(CodeCoverageToolKind.AltCover)]
    [TestCase(CodeCoverageToolKind.Coverlet)]
    public void CodeCoverageService_RunsAgainstSameFixtureProject_AsTestService(CodeCoverageToolKind coverageTool)
    {
        // The registered instance, not `new SDTestService()` - a second, independent SDTestService
        // would build its own separate TestSolution/ITestProject tree from scratch instead of
        // observing the one ServiceBootstrapper.Initialize() already wired up and that real DevFlow
        // actions (ide-refresh-tests etc.) actually drive.
        var testService = SD.MainThread.InvokeIfRequired(() => (ITestService)SD.GetRequiredService<ITestService>());
        var solution = SD.MainThread.InvokeIfRequired(() => testService.OpenSolution);
        var discoveredTests = GetConfirmedLeafTests(solution);
        Assert.That(discoveredTests, Is.Not.Empty, "No tests discovered; coverage run would have nothing to measure.");

        CodeCoverageService.Instance.CoverageTool = coverageTool;
        CodeCoverageService.Instance.RunAllTestsWithCoverageAsync().GetAwaiter().GetResult();

        var session = CodeCoverageService.Instance.CurrentSession;
        Assert.That(CodeCoverageService.Instance.IsRunning, Is.False, "Service should report not-running once the awaited run has completed.");
        Assert.That(session.Title, Does.StartWith(coverageTool.ToString()));
        Assert.That(session.Results, Is.Not.Empty, () => "No coverage results produced. Log:\n" + string.Join('\n', session.LogLines));

        // Calculator.Divide's b==0 branch is intentionally left untested by the fixture,
        // so coverage should land strictly between 0% and 100% - proof the same MTP
        // project the test service just discovered tests in was actually instrumented and run.
        Assert.That(session.CoveragePercent, Is.GreaterThan(0));
        Assert.That(session.CoveragePercent, Is.LessThan(100));
    }

    // Awaits the real MTP discovery pass rather than polling the tree for a settled state:
    // MtpTestProject.RefreshAsync completes exactly when the tree reflects the MTP host's answer,
    // so there is no window in which a caller has to guess whether discovery is still running.
    private static List<MtpTestMethod> GetConfirmedLeafTests(ITestSolution solution)
    {
        var refreshes = solution.NestedTests
            .OfType<MtpTestProject>()
            .Select(project => project.RefreshAsync())
            .ToList();
        Task.WhenAll(refreshes).GetAwaiter().GetResult();

        return EnumerateLeafTests(solution).ToList();
    }

    private static IEnumerable<MtpTestMethod> EnumerateLeafTests(ITest test)
    {
        if (test is MtpTestMethod method)
        {
            yield return method;
            yield break;
        }
        foreach (var child in test.NestedTests)
            foreach (var leaf in EnumerateLeafTests(child))
                yield return leaf;
    }

    private static string ResolveFixtureProjectPath()
    {
        var baseDirectory = System.AppContext.BaseDirectory;
        var path = Path.Combine(baseDirectory, "..", "..", "..", "..", "Fixtures", "SampleMtpTests", "SampleMtpTests.csproj");
        return Path.GetFullPath(path);
    }
}
