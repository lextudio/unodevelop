using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class SolutionExplorerTests
{
    readonly UnoDevelopAppFixture _app;
    public SolutionExplorerTests(UnoDevelopAppFixture app) => _app = app;

    [Fact]
    public async Task CurrentSolution_ReturnsNonEmpty()
    {
        var result = await _app.InvokeStringAsync("ide-current-solution");
        Assert.NotNull(result);
        Assert.NotEqual("{}", result);
    }

    [Fact]
    public async Task ListProjects_ReturnsProjects()
    {
        var result = await _app.InvokeStringAsync("ide-list-projects");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task OpenThenCloseSolution()
    {
        var open = await _app.InvokeAsync("ide-open-project", _app.FixtureSolutionPath);
        Assert.True(open.GetProperty("success").GetBoolean());

        var close = await _app.InvokeStringAsync("ide-close-solution");
        Assert.NotNull(close);
    }
}
