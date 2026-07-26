using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class VBBindingTests
{
    readonly UnoDevelopAppFixture _app;

    public VBBindingTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task OpenVBSolution_LoadsVBFixture()
    {
        var result = await _app.InvokeAsync("ide-open-project", _app.VBFixtureSolutionPath);
        Assert.True(result.GetProperty("success").GetBoolean());
        var current = await _app.InvokeAsync("ide-current-solution");
        Assert.Contains("VBFixture", current.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task VBBuild_CompilesFixtureProject()
    {
        await _app.InvokeAsync("ide-open-project", _app.VBFixtureSolutionPath);
        var preBuild = Path.Combine(Path.GetDirectoryName(_app.VBFixtureSolutionPath)!, "bin", "Debug", "net10.0", "VBFixture.dll");
        if (File.Exists(preBuild))
            File.Delete(preBuild);
        var result = await _app.InvokeAsync("ide-build-solution");
        Assert.Equal("Success", result.GetProperty("result").GetString());
    }

    [Fact]
    public async Task OpenVBFile_UsesVBHighlighting()
    {
        await _app.InvokeAsync("ide-open-project", _app.VBFixtureSolutionPath);
        var vbPath = Path.Combine(Path.GetDirectoryName(_app.VBFixtureSolutionPath)!, "Class1.vb");
        var openResult = await _app.InvokeAsync("ide-open-file", vbPath);
        Assert.True(openResult.GetProperty("opened").GetBoolean());

        var view = await _app.InvokeAsync("ide-active-view");
        Assert.True(view.GetProperty("active").GetBoolean());
        Assert.Equal("VB", view.GetProperty("syntaxHighlighting").GetString());
    }
}
