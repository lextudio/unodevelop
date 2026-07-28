using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class XmlEditorTests
{
    readonly UnoDevelopAppFixture _app;

    public XmlEditorTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task OpenXmlFile_AttachesXmlTreeView()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.XmlFixtureFilePath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var status = await _app.InvokeAsync("ide-xml-tree-status");
        Assert.True(status.GetProperty("found").GetBoolean(), status.ToString());
        Assert.Equal("ICSharpCode.XmlEditor.XmlTreeView", status.GetProperty("viewType").GetString());
    }

    [Fact]
    public async Task OpenXmlFile_XmlTreeViewTabTitleIsNotEmpty()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.XmlFixtureFilePath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var status = await _app.InvokeAsync("ide-xml-tree-status");
        Assert.True(status.GetProperty("found").GetBoolean(), status.ToString());
        Assert.False(string.IsNullOrEmpty(status.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task OpenNonXmlFile_DoesNotAttachXmlTreeView()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.DebugTestProgramPath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var status = await _app.InvokeAsync("ide-xml-tree-status");
        Assert.False(status.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task OpenXmlFile_UsesXmlFoldingStrategy()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.XmlFixtureFilePath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var foldings = await _app.InvokeAsync("ide-editor-foldings");
        Assert.True(foldings.GetProperty("found").GetBoolean(), foldings.ToString());
        Assert.Equal("XmlFoldingStrategy", foldings.GetProperty("strategy").GetString());
        Assert.True(foldings.GetProperty("count").GetInt32() >= 1, foldings.ToString());
    }
}
