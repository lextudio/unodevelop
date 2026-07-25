using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class BuildTests
{
    readonly UnoDevelopAppFixture _app;
    public BuildTests(UnoDevelopAppFixture app) => _app = app;

    [Fact]
    public async Task BuildFixtureSolution_Succeeds()
    {
        var result = await _app.InvokeStringAsync("ide-build-solution");
        Assert.StartsWith("OK:", result);
        Assert.Contains("Errors=", result);
    }

    [Fact]
    public async Task BuildThenQuery_IsBuildingReturnsFalse()
    {
        await _app.InvokeStringAsync("ide-build-solution");
        var result = await _app.InvokeStringAsync("ide-is-building");
        Assert.Equal("false", result);
    }
}
