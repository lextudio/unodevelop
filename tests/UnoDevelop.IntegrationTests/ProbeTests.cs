using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class ProbeTests
{
    readonly UnoDevelopAppFixture _app;
    public ProbeTests(UnoDevelopAppFixture app) => _app = app;

    [Fact]
    public async Task ProbeIsRunning_ReturnsStatus()
    {
        var result = await _app.InvokeAsync("uno.probe.tests.is-running");
        Assert.NotNull(result);
    }
}
