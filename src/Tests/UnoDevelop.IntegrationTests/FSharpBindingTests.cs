using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class FSharpBindingTests
{
    readonly UnoDevelopAppFixture _app;

    public FSharpBindingTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task FSharpAddIn_IsLoaded()
    {
        var status = await _app.InvokeAsync("ide-list-addins");
        var addIns = status.EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("ICSharpCode.FSharpBinding", addIns);
    }

    [Fact]
    public async Task OpenFSharpSolution_LoadsProject()
    {
        var result = await _app.InvokeAsync("ide-open-project", _app.FSharpFixtureSolutionPath);
        Assert.True(result.GetProperty("success").GetBoolean());

        var current = await _app.InvokeAsync("ide-current-solution");
        Assert.Contains("FSharpFixture", current.GetProperty("fileName").GetString());
        var projects = await _app.InvokeAsync("ide-list-projects");
        Assert.Contains("FSharpFixture", projects.EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task OpenFSharpFile_UsesFSharpHighlighting()
    {
        await _app.InvokeAsync("ide-open-project", _app.FSharpFixtureSolutionPath);
        var file = Path.Combine(Path.GetDirectoryName(_app.FSharpFixtureSolutionPath)!, "Program.fs");
        var open = await _app.InvokeAsync("ide-open-file", file);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var view = await _app.InvokeAsync("ide-active-view");
        Assert.Equal("EditorViewContent", view.GetProperty("typeName").GetString()!.Split('+').Last());
        Assert.Equal("F#", view.GetProperty("syntaxHighlighting").GetString());
        Assert.Equal("F#", view.GetProperty("editorSyntaxHighlighting").GetString());
        Assert.Equal("XshdHighlightedLineSource", view.GetProperty("highlightedLineSource").GetString());
    }

    [Fact]
    public async Task FSharpBuild_CompilesFixtureProject()
    {
        await _app.InvokeAsync("ide-open-project", _app.FSharpFixtureSolutionPath);
        var output = Path.Combine(
            Path.GetDirectoryName(_app.FSharpFixtureSolutionPath)!,
            "bin", "Debug", "net10.0", "FSharpFixture.dll");
        if (File.Exists(output))
            File.Delete(output);

        var result = await _app.InvokeAsync("ide-build-solution");
        Assert.True(result.GetProperty("result").GetString() == "Success", result.ToString());
        Assert.True(File.Exists(output));
    }
}
