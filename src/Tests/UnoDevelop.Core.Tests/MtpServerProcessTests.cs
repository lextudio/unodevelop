using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnoDevelop.UnitTesting.Mtp;

namespace UnoDevelop.Core.Tests;

// Proves out MtpServerProcess (the server-mode JSON-RPC client) end to end against real MTP
// fixture projects, independent of TestService/DotNetTestRunner - this is not wired into the
// pads directly, just validating the transport/protocol client works. Run against three
// different MTP hosts (MSTest, xUnit.net v3, NUnit) because two host-specific quirks were found
// empirically, not documented: (1) sending "tests": null for "run everything" crashes MSTest's
// host outright, and (2) a run filter needs the full node shape (uid + display-name + node-type),
// not just a uid, or the host throws server-side. Both were only MSTest-confirmed before; running
// the same assertions against xUnit.v3 and NUnit checks whether they're MSTest bugs or protocol-
// wide behavior a compliant client must always account for.
//
// Display names are asserted with EndsWith rather than exact equality because verbosity differs
// per host: MSTest and NUnit report short method names ("Add_ReturnsSum"), xUnit.net v3 reports
// namespace-qualified names ("SampleXunitMtpTests.CalculatorTests.Add_ReturnsSum").
public abstract class MtpServerProcessFixtureTestsBase
{
    protected abstract string FixtureDirectoryName { get; }
    protected abstract string ProjectFileName { get; }
    protected abstract string AssemblyFileName { get; }
    protected abstract string AddTestSuffix { get; }
    protected abstract string DivideTestSuffix { get; }

    private string FixtureDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", FixtureDirectoryName));

    private string FixtureProjectPath => Path.Combine(FixtureDirectory, ProjectFileName);

    private string FixtureAssemblyPath => Path.Combine(FixtureDirectory, "bin", "Debug", "net10.0", AssemblyFileName);

    [OneTimeSetUp]
    public async Task BuildFixture()
    {
        Assert.That(File.Exists(FixtureProjectPath), Is.True, $"Fixture project not found: {FixtureProjectPath}");

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"build \"{FixtureProjectPath}\" -tl:off")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.EqualTo(0), () => $"Fixture build failed:\n{stdout}\n{stderr}");
        Assert.That(File.Exists(FixtureAssemblyPath), Is.True, $"Fixture assembly not found after build: {FixtureAssemblyPath}");
    }

    [Test]
    [Timeout(60_000)]
    public async Task InitializeAsync_ReturnsServerCapabilities()
    {
        await using var server = await MtpServerProcess.StartAsync(FixtureAssemblyPath, Path.GetDirectoryName(FixtureAssemblyPath), CancellationToken.None);

        var capabilities = await server.InitializeAsync(CancellationToken.None);

        Assert.That(capabilities.ServerName, Is.Not.Empty);
        Assert.That(capabilities.SupportsDiscovery, Is.True, () => "Server capabilities: " + capabilities);
    }

    [Test]
    [Timeout(60_000)]
    public async Task DiscoverTestsAsync_ReturnsBothFixtureTests()
    {
        await using var server = await MtpServerProcess.StartAsync(FixtureAssemblyPath, Path.GetDirectoryName(FixtureAssemblyPath), CancellationToken.None);
        await server.InitializeAsync(CancellationToken.None);

        var nodes = await server.DiscoverTestsAsync(CancellationToken.None);
        var actionNodes = nodes.Where(n => n.NodeType == "action").ToList();

        Assert.That(actionNodes.Count, Is.EqualTo(2), () => "Process output:\n" + string.Join('\n', server.ProcessOutput));
        Assert.That(actionNodes.Any(n => n.DisplayName.EndsWith(AddTestSuffix, StringComparison.Ordinal)), Is.True);
        Assert.That(actionNodes.Any(n => n.DisplayName.EndsWith(DivideTestSuffix, StringComparison.Ordinal)), Is.True);
    }

    [Test]
    [Timeout(60_000)]
    public async Task RunTestsAsync_ReportsPassedForBothFixtureTests()
    {
        await using var server = await MtpServerProcess.StartAsync(FixtureAssemblyPath, Path.GetDirectoryName(FixtureAssemblyPath), CancellationToken.None);
        await server.InitializeAsync(CancellationToken.None);

        try
        {
            var nodes = await server.RunTestsAsync(CancellationToken.None);
            var actionNodes = nodes.Where(n => n.NodeType == "action").ToList();

            Assert.That(actionNodes.Count, Is.EqualTo(2));
            Assert.That(actionNodes.Select(n => n.ExecutionState), Is.All.EqualTo("passed"),
                () => "Process output:\n" + string.Join('\n', server.ProcessOutput));
        }
        catch (Exception ex)
        {
            throw new Exception("Process output:\n" + string.Join('\n', server.ProcessOutput), ex);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task RunTestsAsync_WithUidFilter_RunsOnlyTheRequestedTest()
    {
        await using var server = await MtpServerProcess.StartAsync(FixtureAssemblyPath, Path.GetDirectoryName(FixtureAssemblyPath), CancellationToken.None);
        var capabilities = await server.InitializeAsync(CancellationToken.None);

        var discovered = await server.DiscoverTestsAsync(CancellationToken.None);
        var addTest = discovered.Single(n => n.NodeType == "action" && n.DisplayName.EndsWith(AddTestSuffix, StringComparison.Ordinal));

        try
        {
            var nodes = await server.RunTestsAsync(new[] { addTest }, CancellationToken.None);
            var actionNodes = nodes.Where(n => n.NodeType == "action").ToList();

            // Proves the filter is honored rather than silently running (or ignoring) everything.
            Assert.That(actionNodes.Count, Is.EqualTo(1));
            Assert.That(actionNodes.Single().DisplayName, Is.EqualTo(addTest.DisplayName));
            Assert.That(actionNodes.Single().ExecutionState, Is.EqualTo("passed"));
        }
        catch (Exception ex)
        {
            throw new Exception("Capabilities: " + capabilities + "\nDiscovered node JSON:\n" + addTest.RawJson + "\nProcess output:\n" + string.Join('\n', server.ProcessOutput), ex);
        }
    }
}

[TestFixture]
public sealed class MtpServerProcessTests_MSTest : MtpServerProcessFixtureTestsBase
{
    protected override string FixtureDirectoryName => "SampleMtpTests";
    protected override string ProjectFileName => "SampleMtpTests.csproj";
    protected override string AssemblyFileName => "SampleMtpTests.dll";
    protected override string AddTestSuffix => "Add_ReturnsSum";
    protected override string DivideTestSuffix => "Divide_ReturnsQuotient";
}

[TestFixture]
public sealed class MtpServerProcessTests_XunitV3 : MtpServerProcessFixtureTestsBase
{
    protected override string FixtureDirectoryName => "SampleXunitMtpTests";
    protected override string ProjectFileName => "SampleXunitMtpTests.csproj";
    protected override string AssemblyFileName => "SampleXunitMtpTests.dll";
    protected override string AddTestSuffix => "Add_ReturnsSum";
    protected override string DivideTestSuffix => "Divide_ReturnsQuotient";
}

[TestFixture]
public sealed class MtpServerProcessTests_NUnit : MtpServerProcessFixtureTestsBase
{
    protected override string FixtureDirectoryName => "SampleNUnitMtpTests";
    protected override string ProjectFileName => "SampleNUnitMtpTests.csproj";
    protected override string AssemblyFileName => "SampleNUnitMtpTests.dll";
    protected override string AddTestSuffix => "Add_ReturnsSum";
    protected override string DivideTestSuffix => "Divide_ReturnsQuotient";
}
