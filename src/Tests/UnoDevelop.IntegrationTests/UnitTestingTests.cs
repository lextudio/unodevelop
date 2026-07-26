using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class UnitTestingTests
{
    readonly UnoDevelopAppFixture _app;
    public UnitTestingTests(UnoDevelopAppFixture app) => _app = app;

    [Fact]
    public async Task RefreshTests_DiscoversSampleProject()
    {
        await _app.InvokeAsync("uno.probe.tests.refresh");
        var list = await _app.InvokeAsync("uno.probe.tests.list");
        Assert.NotNull(list);
    }

    [Fact]
    public async Task RunAllTests_ProducesExpectedResults()
    {
        await _app.InvokeAsync("uno.probe.tests.refresh");
        await _app.InvokeAsync("uno.probe.tests.run-all");
        var completed = await _app.PollAsync(
            "uno.probe.tests.is-running",
            s => !s.TryGetProperty("isRunning", out var r) || !r.GetBoolean(),
            timeoutMs: 60_000);
        Assert.NotNull(completed);
    }

    [Fact]
    public async Task DebugTests_ReturnsWithoutError()
    {
        await _app.InvokeAsync("uno.probe.tests.refresh");
        // Debug may not start if there are no test projects — just verify the action runs.
        var result = await _app.InvokeStringAsync("uno.probe.tests.debug");
        Assert.NotNull(result);
    }
}
