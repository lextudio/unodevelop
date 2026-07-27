using System.IO;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project;
using NUnit.Framework;
using UnoDevelop.AddIns.Analysis.CodeCoverage;
using UnoDevelop.Services;
using ICSharpCode.UnitTesting.Simple;

namespace UnoDevelop.Core.Tests;

// Exercises the UnitTesting and CodeCoverage addins together against a real
// MTP fixture project (Tests/Fixtures/SampleMtpTests), the way a user driving
// the "Run Tests" and "Run Tests with Coverage" pad buttons back-to-back would:
// both features must agree on which project is an MTP test project, and a
// coverage run must reflect the same tests TestService discovers.
//
// This spawns real `dotnet` subprocesses (build/run/coverlet) against the
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
    [Timeout(180_000)]
    public void TestService_DiscoversFixtureTests()
    {
        var testService = new TestService();
        var tests = testService.GetTests();

        // MSTest's MTP host reports short method names (not namespace/class-qualified) via
        // --list-tests, so TestService.GetTests() reflects that shape rather than inventing
        // qualification the underlying tool doesn't provide.
        Assert.That(tests.Select(t => t.FullyQualifiedName),
            Is.EquivalentTo(new[] { "Add_ReturnsSum", "Divide_ReturnsQuotient" }));
    }

    [Test]
    [Timeout(180_000)]
    public void CodeCoverageService_RunsAgainstSameFixtureProject_AsTestService()
    {
        var testService = new TestService();
        var discoveredTests = testService.GetTests();
        Assert.That(discoveredTests, Is.Not.Empty, "TestService found no tests; coverage run would have nothing to measure.");

        CodeCoverageService.Instance.RunAllTestsWithCoverageAsync().GetAwaiter().GetResult();

        var session = CodeCoverageService.Instance.CurrentSession;
        Assert.That(CodeCoverageService.Instance.IsRunning, Is.False, "Service should report not-running once the awaited run has completed.");
        Assert.That(session.Results, Is.Not.Empty, () => "No coverage results produced. Log:\n" + string.Join('\n', session.LogLines));

        // Calculator.Divide's b==0 branch is intentionally left untested by the fixture,
        // so coverage should land strictly between 0% and 100% - proof the same MTP
        // project TestService just discovered tests in was actually instrumented and run.
        Assert.That(session.CoveragePercent, Is.GreaterThan(0));
        Assert.That(session.CoveragePercent, Is.LessThan(100));
    }

    private static string ResolveFixtureProjectPath()
    {
        var baseDirectory = System.AppContext.BaseDirectory;
        var path = Path.Combine(baseDirectory, "..", "..", "..", "..", "Fixtures", "SampleMtpTests", "SampleMtpTests.csproj");
        return Path.GetFullPath(path);
    }
}
