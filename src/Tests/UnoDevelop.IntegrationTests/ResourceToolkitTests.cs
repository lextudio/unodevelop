using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

/// <summary>
/// Covers a contract-first v1 slice of OpenDevelop's Hornung.ResourceToolkit
/// (opendevelop-sync.md Phase 3, AddIns/Misc/ResourceToolkit): resolving a resource key to its
/// value by searching .resx files under a directory. OpenDevelop's original addin resolves
/// resource references at the editor caret via NRefactory/Roslyn AST visitors plus code
/// completion/tooltips/refactoring - none of that is ported here; this is deliberately just the
/// directory-scoped key -> value lookup, the core lookup primitive everything else builds on.
/// </summary>
[Collection("UnoDevelop app")]
public sealed class ResourceToolkitTests
{
    readonly UnoDevelopAppFixture _app;

    public ResourceToolkitTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task ResolveResourceKey_FindsValueInResxUnderDirectory()
    {
        var directory = Path.GetDirectoryName(_app.ResourceFixtureFilePath)!;

        var result = await _app.InvokeAsync("ide-resolve-resource-key", directory, "Greeting");
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal("Hello from resx", result.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ResolveResourceKey_UnknownKey_ReturnsNotFound()
    {
        var directory = Path.GetDirectoryName(_app.ResourceFixtureFilePath)!;

        var result = await _app.InvokeAsync("ide-resolve-resource-key", directory, "DoesNotExist");
        Assert.False(result.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task ResolveResourceAtCursor_BclPattern_ResolvesViaResx()
    {
        var content = File.ReadAllText(_app.ResourceUsageFixtureFilePath);
        var offset = content.IndexOf("Greeting", StringComparison.Ordinal) + 1;

        var result = await _app.InvokeAsync("ide-resolve-resource-at-cursor", _app.ResourceUsageFixtureFilePath, offset);
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal("Greeting", result.GetProperty("key").GetString());
        Assert.Equal("BclResourceManager", result.GetProperty("kind").GetString());
        Assert.True(result.GetProperty("resolved").GetBoolean());
        Assert.Equal("Hello from resx", result.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ResolveResourceAtCursor_CoreResourceServicePattern_IsDetected()
    {
        var content = File.ReadAllText(_app.ResourceUsageFixtureFilePath);
        var offset = content.IndexOf("SomeCoreKey", StringComparison.Ordinal) + 1;

        var result = await _app.InvokeAsync("ide-resolve-resource-at-cursor", _app.ResourceUsageFixtureFilePath, offset);
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal("SomeCoreKey", result.GetProperty("key").GetString());
        Assert.Equal("CoreResourceService", result.GetProperty("kind").GetString());
        // Not asserting on value/error - "SomeCoreKey" isn't a real registered string resource,
        // this only verifies the pattern is correctly detected and SD.ResourceService is invoked.
    }

    [Fact]
    public async Task ResolveResourceAtCursor_NonResourcePosition_ReturnsNotFound()
    {
        var content = File.ReadAllText(_app.ResourceUsageFixtureFilePath);
        var offset = content.IndexOf("class ResourceUsage", StringComparison.Ordinal) + 1;

        var result = await _app.InvokeAsync("ide-resolve-resource-at-cursor", _app.ResourceUsageFixtureFilePath, offset);
        Assert.False(result.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task ResolveResourceAtCursor_VBBclPattern_ResolvesViaResx()
    {
        var content = File.ReadAllText(_app.ResourceUsageVBFixtureFilePath);
        var offset = content.IndexOf("Greeting", StringComparison.Ordinal) + 1;

        var result = await _app.InvokeAsync("ide-resolve-resource-at-cursor", _app.ResourceUsageVBFixtureFilePath, offset);
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal("Greeting", result.GetProperty("key").GetString());
        Assert.Equal("BclResourceManager", result.GetProperty("kind").GetString());
        Assert.True(result.GetProperty("resolved").GetBoolean());
        Assert.Equal("Hello from resx", result.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ResolveResourceAtCursor_VBCoreResourceServicePattern_IsDetected()
    {
        var content = File.ReadAllText(_app.ResourceUsageVBFixtureFilePath);
        var offset = content.IndexOf("SomeCoreKey", StringComparison.Ordinal) + 1;

        var result = await _app.InvokeAsync("ide-resolve-resource-at-cursor", _app.ResourceUsageVBFixtureFilePath, offset);
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Equal("SomeCoreKey", result.GetProperty("key").GetString());
        Assert.Equal("CoreResourceService", result.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task FindUnusedResourceKeys_FindsDeliberatelyUnusedStringKey()
    {
        var directory = Path.GetDirectoryName(_app.ResourceFixtureFilePath)!;

        var result = await _app.InvokeAsync("ide-find-unused-resource-keys", directory);
        Assert.True(result.GetProperty("found").GetBoolean());

        var unused = result.GetProperty("unused").EnumerateArray()
            .Select(e => e.GetProperty("key").GetString())
            .ToList();
        Assert.Contains("UnusedString", unused);
        Assert.DoesNotContain("Greeting", unused); // referenced from ResourceUsage.cs/.vb
    }

    [Fact]
    public async Task RenameResourceKey_RenamesInResxAndUpdatesReferencingFiles()
    {
        var directory = Path.GetDirectoryName(_app.RenameResourceFixtureFilePath)!;
        var csFilePath = Path.Combine(directory, "RenameUsage.cs");

        try
        {
            var rename = await _app.InvokeAsync("ide-rename-resource-key", directory, "OldKeyName", "NewKeyName");
            Assert.True(rename.GetProperty("success").GetBoolean());
            Assert.Contains(csFilePath, rename.GetProperty("updatedFiles").EnumerateArray().Select(e => e.GetString()));

            var renamed = await _app.InvokeAsync("ide-resolve-resource-key", directory, "NewKeyName");
            Assert.True(renamed.GetProperty("found").GetBoolean());
            Assert.Equal("Some renamable value", renamed.GetProperty("value").GetString());

            var oldGone = await _app.InvokeAsync("ide-resolve-resource-key", directory, "OldKeyName");
            Assert.False(oldGone.GetProperty("found").GetBoolean());

            var updatedContent = File.ReadAllText(csFilePath);
            Assert.Contains("\"NewKeyName\"", updatedContent);
            Assert.DoesNotContain("\"OldKeyName\"", updatedContent);
        }
        finally
        {
            // Restore fixture state for repeatability (tests aren't guaranteed run order/isolation).
            await _app.InvokeAsync("ide-rename-resource-key", directory, "NewKeyName", "OldKeyName");
        }
    }

    [Fact]
    public async Task Complete_InsideResourceKeyLiteral_OffersResxKeys()
    {
        var content = File.ReadAllText(_app.ResourceUsageFixtureFilePath);
        // Position inside the (empty-quoted-would-be) key argument is approximated by using the
        // existing "Greeting" call site's opening quote position - completion should still surface
        // all .resx keys as candidates alongside whatever Roslyn itself proposes at that position.
        var offset = content.IndexOf("\"Greeting\"", StringComparison.Ordinal) + 1;

        var result = await _app.InvokeAsync("ide-complete", _app.ResourceUsageFixtureFilePath, offset);
        Assert.True(result.GetProperty("triggered").GetBoolean());

        var labels = result.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("label").GetString())
            .ToList();
        Assert.Contains("Greeting", labels);
    }
}
