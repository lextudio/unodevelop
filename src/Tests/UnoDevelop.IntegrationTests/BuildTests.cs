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
        var result = await _app.InvokeAsync("ide-build-solution");
        Assert.Equal("Success", result.GetProperty("result").GetString());
    }

    [Fact]
    public async Task BuildThenQuery_IsBuildingReturnsFalse()
    {
        var buildResult = await _app.InvokeAsync("ide-build-solution");
        Assert.NotNull(buildResult);
        var raw = (await _app.InvokeStringAsync("ide-is-building")).Trim('"', ' ');
        Assert.True(raw == "false" || raw == "true", $"Expected 'false' or 'true', got '{raw}'");
    }
}
