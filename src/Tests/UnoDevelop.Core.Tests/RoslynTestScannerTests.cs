using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ICSharpCode.UnitTesting.Simple;

namespace UnoDevelop.Core.Tests;

// Proves RoslynTestScanner's two claims from doc/technotes/unit-testing.md's "Open idea:
// Roslyn-assisted discovery": (1) it correctly finds attribute-decorated test methods across the
// xunit/NUnit/MSTest fixtures without a build or MTP round trip, and (2) it does so fast enough
// (single-digit milliseconds) to be worth seeding TestService's cache with, versus the ~30-60s an
// MTP discovery round trip can take for a single project (see TestService.DiscoverTestsForProjectApprox/
// DiscoverTestsForProjectViaMtpAsync's timeout comment).
[TestFixture]
public sealed class RoslynTestScannerTests
{
    [Test]
    public void ScanProject_XunitFixture_FindsFactMethods()
    {
        var candidates = RoslynTestScanner.ScanProject(FixtureDirectory("SampleXunitMtpTests"));

        Assert.That(candidates.Select(c => c.DisplayName), Is.EquivalentTo(new[]
        {
            "SampleXunitMtpTests.CalculatorTests.Add_ReturnsSum",
            "SampleXunitMtpTests.CalculatorTests.Divide_ReturnsQuotient",
        }));
    }

    [Test]
    public void ScanProject_NUnitFixture_FindsTestMethods()
    {
        var candidates = RoslynTestScanner.ScanProject(FixtureDirectory("SampleNUnitMtpTests"));

        Assert.That(candidates.Select(c => c.DisplayName), Is.EquivalentTo(new[]
        {
            "SampleNUnitMtpTests.CalculatorTests.Add_ReturnsSum",
            "SampleNUnitMtpTests.CalculatorTests.Divide_ReturnsQuotient",
        }));
    }

    [Test]
    public void ScanProject_MSTestFixture_FindsTestMethods()
    {
        var candidates = RoslynTestScanner.ScanProject(FixtureDirectory("SampleMtpTests"));

        Assert.That(candidates.Select(c => c.DisplayName), Is.EquivalentTo(new[]
        {
            "SampleMtpTests.CalculatorTests.Add_ReturnsSum",
            "SampleMtpTests.CalculatorTests.Divide_ReturnsQuotient",
        }));
    }

    [Test]
    public void ScanProject_TypeFullNameAndMethodName_ReportedSeparately()
    {
        var candidates = RoslynTestScanner.ScanProject(FixtureDirectory("SampleXunitMtpTests"));

        var addTest = candidates.Single(c => c.MethodName == "Add_ReturnsSum");
        Assert.That(addTest.TypeFullName, Is.EqualTo("SampleXunitMtpTests.CalculatorTests"));
    }

    [Test]
    public void ScanProject_NonExistentDirectory_ReturnsEmpty()
    {
        var candidates = RoslynTestScanner.ScanProject(Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid()));
        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public void ScanProject_NullDirectory_ReturnsEmpty()
    {
        Assert.That(RoslynTestScanner.ScanProject(null), Is.Empty);
    }

    [Test]
    public void ScanProject_Calculator_HasNoTestAttributes_NotReported()
    {
        // Calculator.cs (Add/Divide) sits next to CalculatorTests.cs in every fixture and has no
        // test attributes at all - proves the scan is attribute-driven, not "every public method".
        var candidates = RoslynTestScanner.ScanProject(FixtureDirectory("SampleXunitMtpTests"));
        Assert.That(candidates.Any(c => c.TypeFullName.EndsWith("Calculator", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void ScanProject_IsFastEnoughToSeedTestServiceCache()
    {
        // The whole point of the Roslyn pass (see TestService.DiscoverTestsForProjectApprox) is
        // that it's fast enough to return before a caller would otherwise be looking at "no tests
        // yet" for as long as an MTP round trip takes. A generous 5s ceiling for a 2-file fixture
        // still proves the point without being a flaky micro-benchmark on a loaded CI box - the
        // real-world contrast is a 30-60s MTP timeout, not a few hundred milliseconds either way.
        var stopwatch = Stopwatch.StartNew();
        var candidates = RoslynTestScanner.ScanProject(FixtureDirectory("SampleMtpTests"));
        stopwatch.Stop();

        Assert.That(candidates, Is.Not.Empty);
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
            $"Roslyn scan took {stopwatch.ElapsedMilliseconds}ms - too slow to be worth seeding the cache with " +
            "instead of just waiting for MTP confirmation.");
    }

    private static string FixtureDirectory(string fixtureName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var path = Path.Combine(baseDirectory, "..", "..", "..", "..", "Fixtures", fixtureName);
        return Path.GetFullPath(path);
    }
}
