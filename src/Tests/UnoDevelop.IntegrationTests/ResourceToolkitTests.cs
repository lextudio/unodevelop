using System.IO;
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
}
