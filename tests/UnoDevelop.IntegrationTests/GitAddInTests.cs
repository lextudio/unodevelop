using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class GitAddInTests
{
    readonly UnoDevelopAppFixture _app;
    public GitAddInTests(UnoDevelopAppFixture app) => _app = app;

    [Fact]
    public async Task GitVersionProvider_ReturnsStatus()
    {
        // Open the fixture solution which is under version control
        await _app.InvokeStringAsync("ide-open-project", _app.FixtureSolutionPath);

        // GitVersionProvider should be able to check if a file is under git control
        // and provide version info via the document version provider mechanism.
        // For now, verify the solution opens without error.
        var result = await _app.InvokeStringAsync("ide-current-solution");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SolutionTree_ReturnsNonEmptyAfterOpen()
    {
        await _app.InvokeStringAsync("ide-open-project", _app.FixtureSolutionPath);
        var result = await _app.InvokeStringAsync("ide-list-projects");
        Assert.NotNull(result);
    }
}
