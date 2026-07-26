using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

/// <summary>
/// Covers the WinUI/Uno visual designer (XamlDesigner AddIn, DisplayBindings/XamlDesigner)
/// end to end: opening a .xaml file attaches XamlDesignerViewContent as a secondary view
/// (via DisplayBindingService.AttachSubWindows, wired in MainPage.OpenFileInWorkbench), and
/// its Microsoft.UI.Xaml.Markup.XamlReader-based preview renders (or reports an error) via
/// the ide-xaml-preview-status DevFlow action.
/// </summary>
[Collection("UnoDevelop app")]
public sealed class XamlDesignerTests
{
    readonly UnoDevelopAppFixture _app;

    public XamlDesignerTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task OpenValidXamlFile_DesignerPreviewRenders()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.XamlFixtureFilePath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var status = await _app.InvokeAsync("ide-xaml-preview-status");
        Assert.True(status.GetProperty("found").GetBoolean(), status.ToString());
        Assert.Equal("Design", status.GetProperty("statusText").GetString());
        Assert.True(status.GetProperty("hasRenderedPreview").GetBoolean());
        Assert.Contains(status.GetProperty("views").EnumerateArray(), view => view.GetString() == "Design");
        Assert.Contains(status.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("Type").GetString() == "TextBlock"
            && item.GetProperty("Text").GetString() == "Hello from XAML");
        Assert.Contains(status.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("Type").GetString() == "Button"
            && item.GetProperty("Content").GetString() == "Click me");

        var switched = await _app.InvokeAsync("ide-xaml-switch-view", "Design");
        Assert.True(switched.GetProperty("success").GetBoolean());
        Assert.Equal("Design", switched.GetProperty("activeView").GetString());

        status = await _app.InvokeAsync("ide-xaml-preview-status");
        Assert.Equal("Design", status.GetProperty("activeView").GetString());
    }

    [Fact]
    public async Task OpenInvalidXamlFile_DesignerReportsError()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.InvalidXamlFixtureFilePath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var status = await _app.InvokeAsync("ide-xaml-preview-status");
        Assert.True(status.GetProperty("found").GetBoolean(), status.ToString());
        Assert.StartsWith("Error:", status.GetProperty("statusText").GetString());
        Assert.False(status.GetProperty("hasRenderedPreview").GetBoolean());
        Assert.Empty(status.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task OpenNonXamlFile_DoesNotAttachDesigner()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.DebugTestProgramPath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var status = await _app.InvokeAsync("ide-xaml-preview-status");
        Assert.False(status.GetProperty("found").GetBoolean());

        var pads = await _app.InvokeAsync("ide-xaml-designer-pads");
        Assert.True(pads.GetProperty("toolboxFound").GetBoolean(), pads.ToString());
        Assert.False(pads.GetProperty("toolboxHasProvider").GetBoolean(), pads.ToString());
        Assert.Empty(pads.GetProperty("toolboxItems").EnumerateArray());

        var outline = await _app.InvokeAsync("ide-xaml-outline");
        Assert.True(outline.GetProperty("outlineFound").GetBoolean(), outline.ToString());
        Assert.False(outline.GetProperty("outlineHasProvider").GetBoolean(), outline.ToString());
        Assert.Empty(outline.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task XamlOutlinePad_ShowsElementHierarchy()
    {
        await _app.InvokeAsync("ide-open-file", _app.XamlFixtureFilePath);

        var outline = await _app.InvokeAsync("ide-xaml-outline");
        Assert.True(outline.GetProperty("outlineFound").GetBoolean(), outline.ToString());
        Assert.True(outline.GetProperty("outlineHasProvider").GetBoolean(), outline.ToString());
        Assert.Contains(outline.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("ElementName").GetString() == "Page"
            && item.GetProperty("Depth").GetInt32() == 0);
        Assert.Contains(outline.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("ElementName").GetString() == "StackPanel"
            && item.GetProperty("Depth").GetInt32() == 1);
        Assert.Contains(outline.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("ElementName").GetString() == "Button"
            && item.GetProperty("Depth").GetInt32() >= 2);

        var foldings = await _app.InvokeAsync("ide-editor-foldings");
        Assert.Equal("XmlFoldingStrategy", foldings.GetProperty("strategy").GetString());
        Assert.True(foldings.GetProperty("count").GetInt32() >= 1, foldings.ToString());
    }

    [Fact]
    public async Task XmlFile_UsesXmlFolding()
    {
        await _app.InvokeAsync("ide-open-file", _app.XmlFixtureFilePath);

        var foldings = await _app.InvokeAsync("ide-editor-foldings");
        Assert.True(foldings.GetProperty("found").GetBoolean(), foldings.ToString());
        Assert.Equal("XmlFoldingStrategy", foldings.GetProperty("strategy").GetString());
        Assert.True(foldings.GetProperty("count").GetInt32() >= 3, foldings.ToString());
    }

    [Fact]
    public async Task ToolboxPad_IsIndependentOfSourceAndDesignViews()
    {
        await _app.InvokeAsync("ide-open-file", _app.XamlFixtureFilePath);

        var source = await _app.InvokeAsync("ide-xaml-switch-view", "Code");
        Assert.True(source.GetProperty("success").GetBoolean(), source.ToString());
        Assert.Equal("Code", source.GetProperty("activeView").GetString());

        var pads = await _app.InvokeAsync("ide-xaml-designer-pads");
        Assert.True(pads.GetProperty("toolboxFound").GetBoolean(), pads.ToString());
        Assert.True(pads.GetProperty("toolboxHasProvider").GetBoolean(), pads.ToString());
        Assert.True(pads.GetProperty("propertiesFound").GetBoolean(), pads.ToString());
        Assert.Contains(pads.GetProperty("toolboxItems").EnumerateArray(), item =>
            item.GetProperty("Name").GetString() == "Button"
            && item.GetProperty("Xaml").GetString()!.StartsWith("<Button"));
        Assert.Contains(pads.GetProperty("toolboxGroups").EnumerateArray(), group =>
            group.GetProperty("Name").GetString() == "Layout"
            && group.GetProperty("IsCollapsible").GetBoolean()
            && group.GetProperty("Items").EnumerateArray().Any(item => item.GetString() == "Grid"));
        Assert.Contains(pads.GetProperty("toolboxGroups").EnumerateArray(), group =>
            group.GetProperty("Name").GetString() == "Controls"
            && group.GetProperty("Items").EnumerateArray().Any(item => item.GetString() == "Button"));
        Assert.True(pads.GetProperty("toolboxItems").GetArrayLength() >= 40, pads.ToString());

        var collapsed = await _app.InvokeAsync("ide-xaml-toolbox-group", "Layout", false);
        Assert.True(collapsed.GetProperty("success").GetBoolean(), collapsed.ToString());
        pads = await _app.InvokeAsync("ide-xaml-designer-pads");
        Assert.Contains(pads.GetProperty("toolboxGroups").EnumerateArray(), group =>
            group.GetProperty("Name").GetString() == "Layout"
            && !group.GetProperty("IsExpanded").GetBoolean());

        var expanded = await _app.InvokeAsync("ide-xaml-toolbox-group", "Layout", true);
        Assert.True(expanded.GetProperty("success").GetBoolean(), expanded.ToString());

        var design = await _app.InvokeAsync("ide-xaml-switch-view", "Design");
        Assert.True(design.GetProperty("success").GetBoolean(), design.ToString());
        pads = await _app.InvokeAsync("ide-xaml-designer-pads");
        Assert.True(pads.GetProperty("toolboxFound").GetBoolean(), pads.ToString());
    }

    [Fact]
    public async Task ToolboxDropOnSource_InsertsXamlAtCaret()
    {
        await _app.InvokeAsync("ide-open-file", _app.XamlFixtureFilePath);
        await _app.InvokeAsync("ide-xaml-switch-view", "Code");

        const string snippet = "<TextBlock Text=\"Dropped in source\" />";
        var inserted = await _app.InvokeAsync("ide-xaml-source-insert", snippet);

        Assert.True(inserted.GetProperty("success").GetBoolean(), inserted.ToString());
        Assert.True(inserted.GetProperty("containsSnippet").GetBoolean(), inserted.ToString());
    }

    [Fact]
    public async Task ToolboxDropOnDesign_AddsControlAndUpdatesPropertiesPad()
    {
        await _app.InvokeAsync("ide-open-file", _app.XamlFixtureFilePath);
        await _app.InvokeAsync("ide-xaml-switch-view", "Design");

        var selected = await _app.InvokeAsync("ide-xaml-designer-select", "StackPanel", 0);
        Assert.True(selected.GetProperty("success").GetBoolean(), selected.ToString());

        const string snippet = "<Button Content=\"Dropped in design\" />";
        var added = await _app.InvokeAsync("ide-xaml-designer-add", snippet);
        Assert.True(added.GetProperty("success").GetBoolean(), added.ToString());

        var status = await _app.InvokeAsync("ide-xaml-preview-status");
        Assert.Equal("Button", status.GetProperty("selectedElementType").GetString());
        Assert.True(status.GetProperty("hasSelectionAdorner").GetBoolean(), status.ToString());
        Assert.Contains(status.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("Type").GetString() == "Button"
            && item.GetProperty("Content").GetString() == "Dropped in design");

        var resized = await _app.InvokeAsync("ide-xaml-designer-resize", 40d, 20d);
        Assert.True(resized.GetProperty("success").GetBoolean(), resized.ToString());

        var pads = await _app.InvokeAsync("ide-xaml-designer-pads");
        Assert.Equal("Button",
            pads.GetProperty("propertySnapshot").GetProperty("SelectedType").GetString());
        Assert.True(pads.GetProperty("propertySnapshot").GetProperty("Width").GetDouble() >= 40);
        Assert.True(pads.GetProperty("propertySnapshot").GetProperty("Height").GetDouble() >= 20);
    }
}
