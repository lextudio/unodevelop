using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class XamlBindingTests
{
    readonly UnoDevelopAppFixture _app;

    public XamlBindingTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task OpenXamlFile_UsesXmlHighlighting()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.XamlFixtureFilePath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var view = await _app.InvokeAsync("ide-active-view");
        Assert.True(view.GetProperty("active").GetBoolean());
        Assert.Equal("XML", view.GetProperty("syntaxHighlighting").GetString());
    }

    [Fact]
    public async Task XamlFile_HasLspService()
    {
        var status = await _app.InvokeAsync("ide-parser-status", _app.XamlFixtureFilePath);
        Assert.True(status.GetProperty("hasService").GetBoolean());
    }
}
