using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.SharpDevelop.Workbench;
using NUnit.Framework;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests;

/// Exercises DebugService's private static build-output-resolution helper
/// (`ResolveBuildOutputAsync`, called by StartAsync before launching the debuggee) in isolation,
/// without needing a live DAP session. It is private, so this reaches it via reflection rather
/// than widening its visibility - it's a pure static helper (project path in, target DLL path or
/// null out) with no DebugService instance state involved.
[TestFixture]
public sealed class DebugServiceBuildResolutionTests
{
    [Test]
    public async Task ResolveBuildOutputAsync_WhenProjectDoesNotExist_ReturnsNullInsteadOfThrowing()
    {
        var method = typeof(DebugService).GetMethod(
            "ResolveBuildOutputAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "DebugService.ResolveBuildOutputAsync not found - check for a rename.");

        var missingProject = Path.Combine(Path.GetTempPath(), $"DoesNotExist-{Guid.NewGuid():N}.csproj");
        var output = new RecordingOutputCategory();

        var task = (Task<string?>)method!.Invoke(null, [missingProject, output])!;
        string? result = null;
        Assert.That(async () => result = await task, Throws.Nothing);
        Assert.That(result, Is.Null);
        // Should have reported something to the output category rather than failing silently.
        Assert.That(output.Lines, Is.Not.Empty);
    }

    private sealed class RecordingOutputCategory : IOutputCategory
    {
        public System.Collections.Generic.List<string> Lines { get; } = new();

        public string DisplayName => "Debug";

        public void Activate(bool bringPadToFront = false) { }

        public void Clear() => Lines.Clear();

        public void AppendText(RichText text) => Lines.Add(text.Text);

        public void AppendLine(RichText text) => Lines.Add(text.Text);
    }
}
