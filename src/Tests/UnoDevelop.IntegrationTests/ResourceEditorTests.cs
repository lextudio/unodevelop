using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

/// <summary>
/// Covers ResourceViewerViewContent (src/Main/SharpDevelop/Workbench/ResourceViewerViewContent.cs)
/// end to end: opening a .resx file lists its string/boolean/binary entries, with binary-ish
/// entries (Icon/Bitmap/Cursor/other) shown as a byte-count DisplaySummary rather than a raw
/// base64 blob (opendevelop-sync.md Phase 3, AddIns/DisplayBindings/ResourceEditor).
/// </summary>
[Collection("UnoDevelop app")]
public sealed class ResourceEditorTests
{
    readonly UnoDevelopAppFixture _app;

    public ResourceEditorTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task OpenResxFile_ListsStringBooleanAndIconEntries()
    {
        var open = await _app.InvokeAsync("ide-open-file", _app.ResourceFixtureFilePath);
        Assert.True(open.GetProperty("opened").GetBoolean());

        var result = await _app.InvokeAsync("ide-resource-entries");
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.True(result.GetProperty("canEdit").GetBoolean());

        var entries = result.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);

        var greeting = entries.Single(e => e.GetProperty("name").GetString() == "Greeting");
        Assert.True(greeting.GetProperty("isEditable").GetBoolean());
        Assert.Equal("Hello from resx", greeting.GetProperty("displaySummary").GetString());

        var isEnabled = entries.Single(e => e.GetProperty("name").GetString() == "IsEnabled");
        Assert.True(isEnabled.GetProperty("isEditable").GetBoolean());
        Assert.Equal("True", isEnabled.GetProperty("displaySummary").GetString());

        var icon = entries.Single(e => e.GetProperty("name").GetString() == "SampleIcon");
        Assert.False(icon.GetProperty("isEditable").GetBoolean());
        var summary = icon.GetProperty("displaySummary").GetString();
        Assert.StartsWith("Icon (", summary);
        Assert.EndsWith("bytes)", summary);
        Assert.DoesNotContain("AAECAwQF", summary); // not the raw base64 blob
    }
}
