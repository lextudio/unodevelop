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

// Exercises the UnitTesting and CodeCoverage addins together against real
// MTP fixture projects (Tests/Fixtures/Sample{Mtp,NUnitMtp,XunitMtp}Tests), the way a user
// driving the "Run Tests" and "Run Tests with Coverage" pad buttons back-to-back would: both
// features must agree on which project is an MTP test project, and a coverage run must reflect
// the same tests the classic ICSharpCode.UnitTesting backend discovers (see
// doc/technotes/unit-testing.md).
//
// Three fixture project types are covered - MSTest, NUnit, and xUnit.v3, all net10.0 MTP
// projects - because CodeCoverageService.IsMtpTestProject and the AltCover/Coverlet runners
// are meant to be test-framework-agnostic (they drive the produced test host executable
// generically; see AltCoverCoverageRunner/CoverletCoverageRunner), and the pre-existing test here
// only ever proved that against MSTest. SampleNUnitMtpTests/SampleXunitMtpTests already existed
// as fixtures for MtpServerProcess protocol tests and were written with a deliberately identical
// Calculator shape (untested b==0 branch) precisely so they could be reused here too.
//
// This spawns real `dotnet` subprocesses (build/run/coverage tool) against the fixture projects,
// so it is slower than the rest of the suite.
[TestFixture]
public sealed class UnitTestingCodeCoveragePadIntegrationTests
{
    private static readonly string MtpFixtureProjectPath = ResolveFixtureProjectPath("SampleMtpTests");
    private static readonly string NUnitFixtureProjectPath = ResolveFixtureProjectPath("SampleNUnitMtpTests");
    private static readonly string XunitFixtureProjectPath = ResolveFixtureProjectPath("SampleXunitMtpTests");

    [OneTimeSetUp]
    public void InitializeServices()
    {
        Assert.That(File.Exists(MtpFixtureProjectPath), Is.True, $"Fixture project not found: {MtpFixtureProjectPath}");
        Assert.That(File.Exists(NUnitFixtureProjectPath), Is.True, $"Fixture project not found: {NUnitFixtureProjectPath}");
        Assert.That(File.Exists(XunitFixtureProjectPath), Is.True, $"Fixture project not found: {XunitFixtureProjectPath}");

        ServiceBootstrapper.Initialize();

        // Each [Test] below opens the fixture project it needs (OpenFixture), since NUnit does
        // not guarantee execution order within a fixture and several tests switch the open project.
    }

    [Test]
    public void TestService_DiscoversFixtureTests()
    {
        // Ensure the MSTest fixture is the open project - NUnit does not guarantee test
        // execution order within a fixture, and other test cases here (the NUnit/xUnit
        // project-type matrix) switch the open project.
        OpenFixture(MtpFixtureProjectPath);

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

        // MSTest's MTP host reports namespace/class-qualified method names via --list-tests (this
        // shape was previously believed to be short/unqualified; re-verified this session against
        // the fixture's actual current MSTest package version - the assertion below reflects the
        // real current tool output, not an assumption), so the discovered DisplayName reflects
        // that qualified shape.
        Assert.That(leafTests.Select(t => t.DisplayName),
            Is.EquivalentTo(new[] { "SampleMtpTests.CalculatorTests.Add_ReturnsSum", "SampleMtpTests.CalculatorTests.Divide_ReturnsQuotient" }));
    }

    [TestCase(CodeCoverageToolKind.AltCover)]
    [TestCase(CodeCoverageToolKind.Coverlet)]
    public void CodeCoverageService_RunsAgainstSameFixtureProject_AsTestService(CodeCoverageToolKind coverageTool)
    {
        // Ensure the MSTest fixture is the open project - other test cases in this fixture
        // (the NUnit/xUnit project-type matrix below) switch the open project, and NUnit does
        // not guarantee test execution order within a fixture.
        OpenFixture(MtpFixtureProjectPath);
        RunCoverageAgainstOpenProjectAndAssert(coverageTool);
    }

    // "Broader project-type/runtime matrix coverage": the same coverage service/runners exercised
    // above against an MSTest project must also work, unmodified, against NUnit and xUnit.v3 MTP
    // projects - proving CodeCoverageService.IsMtpTestProject's detection and the AltCover/Coverlet
    // runners are genuinely test-framework-agnostic rather than only ever tested against MSTest.
    [TestCase(CodeCoverageToolKind.AltCover)]
    [TestCase(CodeCoverageToolKind.Coverlet)]
    public void CodeCoverageService_RunsAgainstNUnitFixtureProject(CodeCoverageToolKind coverageTool)
    {
        OpenFixture(NUnitFixtureProjectPath);
        RunCoverageAgainstOpenProjectAndAssert(coverageTool);
    }

    [TestCase(CodeCoverageToolKind.AltCover)]
    [TestCase(CodeCoverageToolKind.Coverlet)]
    public void CodeCoverageService_RunsAgainstXunitFixtureProject(CodeCoverageToolKind coverageTool)
    {
        OpenFixture(XunitFixtureProjectPath);
        RunCoverageAgainstOpenProjectAndAssert(coverageTool);
    }

    private static void RunCoverageAgainstOpenProjectAndAssert(CodeCoverageToolKind coverageTool)
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

    // Opens the given fixture project as the active solution. UnoProjectService.OpenSolutionOrProject
    // closes whatever solution is currently open before loading the new one (see
    // UnoProjectService.cs), and TestSolution observes SD.ProjectService.AllProjects.CollectionChanged
    // rather than being rebuilt per-project, so re-opening a different fixture mid-process (instead
    // of re-running ServiceBootstrapper.Initialize() per fixture) reflects the same project-switch
    // path a real user driving the IDE would exercise.
    private static void OpenFixture(string fixtureProjectPath)
    {
        SD.MainThread.InvokeIfRequired(() =>
        {
            var projectService = (IProjectService)ServiceSingleton.ServiceProvider.GetService(typeof(IProjectService));
            var opened = projectService.OpenSolutionOrProject(FileName.Create(fixtureProjectPath));
            Assert.That(opened, Is.True, $"Failed to open fixture project: {fixtureProjectPath}");
        });
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

    private static string ResolveFixtureProjectPath(string fixtureProjectName)
    {
        var baseDirectory = System.AppContext.BaseDirectory;
        var path = Path.Combine(baseDirectory, "..", "..", "..", "..", "Fixtures", fixtureProjectName, fixtureProjectName + ".csproj");
        return Path.GetFullPath(path);
    }
}
