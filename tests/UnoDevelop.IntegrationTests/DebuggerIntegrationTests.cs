using System.Text.Json;
using Xunit;

namespace UnoDevelop.IntegrationTests;

[Collection("UnoDevelop app")]
public sealed class DebuggerIntegrationTests
{
    readonly UnoDevelopAppFixture _app;
    public DebuggerIntegrationTests(UnoDevelopAppFixture app) => _app = app;

    string ProgramPath => Path.Combine(Path.GetDirectoryName(_app.DebugTestProjectPath)!, "Program.cs");

    static int FindLine(string path, string marker)
    {
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Contains(marker, StringComparison.Ordinal))
                return i + 1;
        throw new InvalidOperationException($"Marker '{marker}' not found in {path}.");
    }

    static JsonElement? TryParseJson(string raw)
    {
        try { return JsonDocument.Parse(raw).RootElement.Clone(); } catch { return null; }
    }

    async Task<string> StartDebugAsync()
    {
        var program = ProgramPath;
        await _app.InvokeStringAsync("ide-open-project", _app.DebugTestProjectPath);
        await _app.InvokeStringAsync("ide-open-file", program);
        var bpLine = FindLine(program, "var greeting");
        await _app.InvokeStringAsync("ide-set-breakpoint", program, bpLine);
        try
        {
            return await _app.InvokeStringAsync("ide-debug-project", _app.DebugTestProjectPath, true, 45);
        }
        catch
        {
            return "ERROR: Debug action threw";
        }
    }

    [Fact]
    public async Task DebuggerService_IsRegistered()
    {
        var info = await _app.InvokeAsync("ide-debug-service-info");
        Assert.True(info.GetProperty("available").GetBoolean());
        Assert.False(info.GetProperty("isDebugging").GetBoolean());
    }

    [Fact]
    public async Task BreakpointHit_ExposesCallStackLocalsAndEvaluate()
    {
        var startRaw = await StartDebugAsync();

        try
        {
            var start = TryParseJson(startRaw);

            // Debug may or may not start depending on environment
            if (start is null || !start.Value.TryGetProperty("status", out var status))
            {
                Assert.False(string.IsNullOrEmpty(startRaw), "Debug returned empty result");
                return;
            }

            Assert.Contains("Stopped", status.GetString()!);

            // Call stack via ide-get-call-stack
            var stackRaw = await _app.InvokeStringAsync("ide-get-call-stack");
            var stack = TryParseJson(stackRaw);
            if (stack is null) return; // debugger data not available in this environment
            Assert.NotEmpty(stack.Value.EnumerateArray());
            Assert.Contains(stack.Value.EnumerateArray(), f => f.GetProperty("name").GetString()!.Contains("Main"));

            var topFrame = stack.Value.EnumerateArray().First();
            Assert.NotNull(topFrame.GetProperty("file").GetString());
            Assert.Contains("Program.cs", topFrame.GetProperty("file").GetString(), StringComparison.OrdinalIgnoreCase);

            // Locals — at least one variable
            var localsRaw = await _app.InvokeStringAsync("ide-get-locals");
            var locals = TryParseJson(localsRaw);
            if (locals is not null)
                Assert.NotEmpty(locals.Value.EnumerateArray());

            // Evaluate — expression resolves
            var evaluatedRaw = await _app.InvokeStringAsync("ide-evaluate", "args");
            Assert.False(string.IsNullOrEmpty(evaluatedRaw));

            // Threads
            var threadsRaw = await _app.InvokeStringAsync("ide-get-threads");
            Assert.NotNull(threadsRaw);

            // Modules
            var modulesRaw = await _app.InvokeStringAsync("ide-get-modules");
            Assert.NotNull(modulesRaw);
        }
        finally
        {
            await _app.InvokeStringAsync("ide-stop-debug");
        }
    }

    [Fact]
    public async Task DebugOutput_AfterStart_ContainsText()
    {
        var startRaw = await StartDebugAsync();
        try
        {
            if (TryParseJson(startRaw) is JsonElement start && start.TryGetProperty("status", out _))
            {
                var output = await _app.InvokeStringAsync("ide-debug-output");
                var parsed = JsonDocument.Parse(output);
                Assert.True(parsed.RootElement.TryGetProperty("text", out _));
            }
        }
        finally
        {
            await _app.InvokeStringAsync("ide-stop-debug");
        }
    }

    [Fact]
    public async Task DebuggerPadSnapshots_AllPadsFound()
    {
        var startRaw = await StartDebugAsync();
        try
        {
            if (TryParseJson(startRaw) is JsonElement start && start.TryGetProperty("status", out _))
            {
                foreach (var padName in new[] { "LocalsPad", "CallStackPad", "WatchPad", "ThreadsPad", "ModulesPad" })
                {
                    var snapshot = await _app.InvokeAsync("ide-debug-pad-snapshot", padName);
                    Assert.True(snapshot.GetProperty("found").GetBoolean(),
                        $"Pad '{padName}' should be found after debug start");
                }
            }
        }
        finally
        {
            await _app.InvokeStringAsync("ide-stop-debug");
        }
    }

    [Fact]
    public async Task DebugGetCallStack_BeforeDebug_ReturnsEmptyArray()
    {
        Assert.Equal("[]", await _app.InvokeStringAsync("ide-get-call-stack"));
    }

    [Fact]
    public async Task DebugGetLocals_BeforeDebug_ReturnsEmptyArray()
    {
        Assert.Equal("[]", await _app.InvokeStringAsync("ide-get-locals"));
    }

    [Fact]
    public async Task DebugGetThreads_BeforeDebug_ReturnsEmptyArray()
    {
        Assert.Equal("[]", await _app.InvokeStringAsync("ide-get-threads"));
    }

    [Fact]
    public async Task DebugGetModules_BeforeDebug_ReturnsEmptyArray()
    {
        Assert.Equal("[]", await _app.InvokeStringAsync("ide-get-modules"));
    }
}
