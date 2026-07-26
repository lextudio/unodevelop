using System;
using System.IO;
using System.Linq;
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
        Assert.Equal(_app.VBFixtureSolutionPath, current.GetProperty("solution").GetString());
    }

    [Fact]
    public async Task VBSolutionTree_ShowsSourceFileNode()
    {
        await _app.InvokeAsync("ide-open-project", _app.VBFixtureSolutionPath);
        var projects = await _app.InvokeAsync("ide-list-projects");
        var names = projects.GetProperty("projects").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(names, n => n != null && n.Contains("VBFixture", StringComparison.OrdinalIgnoreCase));
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
    public async Task OpenVBFile_DisplaysInAvalonEdit()
    {
        await _app.InvokeAsync("ide-open-project", _app.VBFixtureSolutionPath);
        var vbPath = Path.Combine(Path.GetDirectoryName(_app.VBFixtureSolutionPath)!, "Class1.vb");
        var openResult = await _app.InvokeAsync("ide-open-file", vbPath);
        Assert.True(openResult.GetProperty("opened").GetBoolean());
    }
}
