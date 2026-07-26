using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

/// <summary>
/// Covers the native Android Device Manager / Android SDK Manager tool views end to end
/// (opendevelop-sync.md Phase 3, AddIns/Misc/AndroidDeviceManager + AndroidSdkManager).
/// This machine has no Android SDK installed, so these tests exercise the graceful
/// "SDK root not set/found" error path rather than a real avdmanager/sdkmanager run -
/// that's still a real, meaningful code path (most dev machines won't have Android SDK either).
/// </summary>
[Collection("UnoDevelop app")]
public sealed class AndroidToolsTests
{
    readonly UnoDevelopAppFixture _app;

    public AndroidToolsTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task OpenDeviceManager_RefreshWithoutSdkPath_ReportsGracefully()
    {
        var open = await _app.InvokeAsync("ide-open-android-device-manager", "");
        Assert.True(open.GetProperty("opened").GetBoolean());

        var refresh = await _app.InvokeAsync("ide-android-device-refresh");
        Assert.True(refresh.GetProperty("success").GetBoolean());

        var list = await _app.InvokeAsync("ide-android-device-list");
        Assert.True(list.GetProperty("found").GetBoolean());
        Assert.Equal(0, list.GetProperty("avds").GetArrayLength());
    }

    [Fact]
    public async Task OpenSdkManager_RefreshWithoutSdkPath_ReportsGracefully()
    {
        var open = await _app.InvokeAsync("ide-open-android-sdk-manager", "");
        Assert.True(open.GetProperty("opened").GetBoolean());

        var refresh = await _app.InvokeAsync("ide-android-sdk-refresh");
        Assert.True(refresh.GetProperty("success").GetBoolean());

        var list = await _app.InvokeAsync("ide-android-sdk-list");
        Assert.True(list.GetProperty("found").GetBoolean());
        Assert.Equal(0, list.GetProperty("packages").GetArrayLength());
    }
}
