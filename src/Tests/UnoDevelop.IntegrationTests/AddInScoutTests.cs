using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

/// <summary>
/// Covers AddIn Scout / AddInManager2 parity (opendevelop-sync.md Phase 3): the enable/disable
/// toggle for already-loaded AddIns (via the real upstream AddInManager.Enable/Disable +
/// SaveAddInConfiguration), and full NuGet-based install/uninstall (via
/// AddInPackageManagerService: real NuGet.Protocol search/download against a local
/// folder-based feed fixture - UNODEVELOP_ADDIN_NUGET_SOURCE, set by UnoDevelopAppFixture - so
/// the test is deterministic and network-independent while still exercising the real download +
/// extract + AddInManager.AddExternalAddIns/RemoveExternalAddIns registration code).
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

    const string TestPackageId = "UnoDevelop.Tests.SampleAddIn";
    const string TestPackageVersion = "1.0.0";
    const string TestAddInIdentity = "UnoDevelop.Tests.SampleAddIn";

    [Fact]
    public async Task SearchNuGet_FindsFixturePackageOnLocalFeed()
    {
        await _app.InvokeAsync("ide-open-addin-scout");

        var search = await _app.InvokeAsync("ide-addin-nuget-search", TestPackageId);
        Assert.True(search.GetProperty("found").GetBoolean());
        var results = search.GetProperty("results").EnumerateArray().ToList();
        var match = results.FirstOrDefault(r => r.GetProperty("id").GetString() == TestPackageId);
        Assert.True(match.ValueKind != JsonValueKind.Undefined, "Expected local NuGet feed fixture package to be found");
        Assert.Equal(TestPackageVersion, match.GetProperty("version").GetString());
    }

    [Fact]
    public async Task InstallThenUninstall_RegistersAndRemovesAddIn()
    {
        await _app.InvokeAsync("ide-open-addin-scout");

        // Clean up any leftovers from a previous interrupted run before asserting install state.
        await _app.InvokeAsync("ide-addin-nuget-uninstall", TestAddInIdentity);

        var install = await _app.InvokeAsync("ide-addin-nuget-install", TestPackageId, TestPackageVersion);
        Assert.True(install.GetProperty("success").GetBoolean(), install.TryGetProperty("installError", out var err) ? err.GetString() : null);
        Assert.False(string.IsNullOrEmpty(install.GetProperty("installDirectory").GetString()));
        Assert.True(install.GetProperty("addInFiles").GetArrayLength() > 0);

        var listAfterInstall = await _app.InvokeAsync("ide-addin-scout-list");
        var installed = listAfterInstall.GetProperty("addIns").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("identity").GetString() == TestAddInIdentity);
        Assert.True(installed.ValueKind != JsonValueKind.Undefined, "Expected the newly installed AddIn to appear in AddIn Scout");
        Assert.True(installed.GetProperty("enabled").GetBoolean());
        Assert.False(installed.GetProperty("preinstalled").GetBoolean());

        var uninstall = await _app.InvokeAsync("ide-addin-nuget-uninstall", TestAddInIdentity);
        Assert.True(uninstall.GetProperty("success").GetBoolean());

        var listAfterUninstall = await _app.InvokeAsync("ide-addin-scout-list");
        var stillPresent = listAfterUninstall.GetProperty("addIns").EnumerateArray()
            .Any(a => a.GetProperty("identity").GetString() == TestAddInIdentity);
        Assert.False(stillPresent, "Expected the AddIn to be gone from AddIn Scout after uninstall");
    }
}
