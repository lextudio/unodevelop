using System.Text.Json;
using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class DevFlowAgentTests
{
    readonly UnoDevelopAppFixture _app;
    public DevFlowAgentTests(UnoDevelopAppFixture app) => _app = app;

    [Fact]
    public async Task AgentStatus_ReturnsOk()
    {
        var status = await _app.GetStatusAsync();
        Assert.NotNull(status);
    }

    [Fact]
    public async Task VersionAction_ReturnsVersionInfo()
    {
        var result = await _app.InvokeAsync("ide-version");
        Assert.NotNull(result);
        Assert.True(result.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task IsBuilding_ReturnsFalseAtStartup()
    {
        var result = await _app.InvokeAsync("ide-is-building");
        Assert.NotNull(result);
    }
}
