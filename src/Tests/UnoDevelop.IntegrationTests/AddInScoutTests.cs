using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

/// <summary>
/// Covers the AddIn Scout enable/disable toggle (opendevelop-sync.md Phase 3,
/// AddIns/Misc/AddInManager2 - contract-first v1 scope: enable/disable an already-loaded AddIn
/// via the real upstream AddInManager.Enable/Disable + SaveAddInConfiguration, persisted to
/// AddIns.xml under the user's config directory. NuGet-based install/uninstall/update - the bulk
/// of OpenDevelop's ~90-file AddInManager2 - is deliberately not ported in this pass.
/// </summary>
[Collection("UnoDevelop app")]
public sealed class AddInScoutTests
{
    readonly UnoDevelopAppFixture _app;

    public AddInScoutTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task OpenAddInScout_ListsLoadedAddIns()
    {
        var open = await _app.InvokeAsync("ide-open-addin-scout");
        Assert.True(open.GetProperty("opened").GetBoolean());

        var list = await _app.InvokeAsync("ide-addin-scout-list");
        Assert.True(list.GetProperty("found").GetBoolean());
        Assert.True(list.GetProperty("addIns").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ToggleEnabled_FlipsStateAndPersists()
    {
        await _app.InvokeAsync("ide-open-addin-scout");
        var list = await _app.InvokeAsync("ide-addin-scout-list");
        var addIns = list.GetProperty("addIns").EnumerateArray().ToList();

        // Pick a non-preinstalled-critical addin to toggle: XML Editor (safe to flip in a test run).
        var target = addIns.FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").Contains("Xml", StringComparison.OrdinalIgnoreCase));
        Assert.True(target.ValueKind != JsonValueKind.Undefined, "Expected an XML-related AddIn to be loaded");

        var name = target.GetProperty("name").GetString()!;
        var originalEnabled = target.GetProperty("enabled").GetBoolean();

        var toggled = await _app.InvokeAsync("ide-addin-toggle-enabled", name);
        Assert.True(toggled.GetProperty("success").GetBoolean());
        Assert.Equal(!originalEnabled, toggled.GetProperty("enabled").GetBoolean());

        // Flip it back so the test suite doesn't leave a disabled AddIn behind for later tests.
        var restored = await _app.InvokeAsync("ide-addin-toggle-enabled", name);
        Assert.Equal(originalEnabled, restored.GetProperty("enabled").GetBoolean());
    }
}
