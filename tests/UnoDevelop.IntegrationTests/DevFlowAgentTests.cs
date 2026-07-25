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
        var result = await _app.InvokeStringAsync("ide-version");
        Assert.Contains("UnoDevelop", result);
    }

    [Fact]
    public async Task IsBuilding_ReturnsFalseAtStartup()
    {
        var result = await _app.InvokeStringAsync("ide-is-building");
        Assert.Equal("false", result);
    }
}
