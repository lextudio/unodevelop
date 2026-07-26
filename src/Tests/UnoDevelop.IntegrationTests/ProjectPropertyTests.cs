using System.IO;
using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class ProjectPropertyTests
{
    readonly UnoDevelopAppFixture _app;
    public ProjectPropertyTests(UnoDevelopAppFixture app) => _app = app;

    [Fact]
    public async Task GetTargetFramework_ReturnsValue()
    {
        var result = await _app.InvokeStringAsync(
            "ide-get-target-framework", _app.FixtureSolutionPath);
        Assert.False(string.IsNullOrEmpty(result));
    }

    [Fact]
    public async Task SetTargetFramework_ChangesValue()
    {
        var original = await _app.InvokeStringAsync(
            "ide-get-target-framework", _app.FixtureSolutionPath);

        var setResult = await _app.InvokeStringAsync(
            "ide-set-target-framework", _app.FixtureSolutionPath, "net10.0");
        Assert.Equal("OK", setResult);

        var updated = await _app.InvokeStringAsync(
            "ide-get-target-framework", _app.FixtureSolutionPath);
        Assert.Equal("net10.0", updated);

        // Restore original value
        await _app.InvokeStringAsync(
            "ide-set-target-framework", _app.FixtureSolutionPath, original);
    }

    [Fact]
    public async Task SetProjectProperty_ReadWriteRoundtrip()
    {
        var testProp = "UnoDevelop_TestProperty";
        var testValue = "test_value_42";

        var setResult = await _app.InvokeStringAsync(
            "ide-set-project-property", _app.FixtureSolutionPath, testProp, testValue);
        Assert.Equal("OK", setResult);

        var readResult = await _app.InvokeStringAsync(
            "ide-get-project-property", _app.FixtureSolutionPath, testProp);
        Assert.Equal(testValue, readResult);

        // Clean up
        await _app.InvokeStringAsync(
            "ide-set-project-property", _app.FixtureSolutionPath, testProp, "");
    }

    [Fact]
    public async Task GetProjectProperty_NonExistent_ReturnsEmpty()
    {
        var result = await _app.InvokeStringAsync(
            "ide-get-project-property", _app.FixtureSolutionPath, "UnoDevelop_NonExistent_XYZ");
        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetTargetFramework_AfterReopen_Persists()
    {
        var original = await _app.InvokeStringAsync(
            "ide-get-target-framework", _app.FixtureSolutionPath);

        await _app.InvokeStringAsync(
            "ide-set-target-framework", _app.FixtureSolutionPath, "net10.0");

        // Re-open the solution to verify persistence
        await _app.InvokeStringAsync("ide-open-project", _app.FixtureSolutionPath);

        var afterReopen = await _app.InvokeStringAsync(
            "ide-get-target-framework", _app.FixtureSolutionPath);
        Assert.Equal("net10.0", afterReopen);

        // Restore original
        await _app.InvokeStringAsync(
            "ide-set-target-framework", _app.FixtureSolutionPath, original);
    }
}
