using System.Text.Json;

using Xunit;

namespace UnoDevelop.IntegrationTests;

// End-to-end tests for the UnoDevelop Test panel. Each test is self-contained: it re-opens
// the fixture project when needed so tests are independent of execution order.
//
// The fixture project (SampleTestProject) contains exactly 3 tests:
//   - PassTests.AlwaysPasses  → should produce result "Passing"
//   - FailTests.AlwaysFails   → should produce result "Failing"
//   - SkipTests.AlwaysSkipped → should produce result "Skipped"
[Collection("UnoDevelop app")]
public sealed class TestPanelTests
{
    readonly UnoDevelopAppFixture _app;

    public TestPanelTests(UnoDevelopAppFixture app)
    {
        _app = app;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    static int Count(JsonElement s) => s.GetArrayLength();

    static bool IsRunning(JsonElement s)
        => s.TryGetProperty("isRunning", out var v) && v.GetBoolean();

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshTests_DiscoversSampleProject()
    {
        var state = await _app.InvokeAsync("uno.probe.tests.refresh");

        Assert.True(
            state.TryGetProperty("count", out var countEl) && countEl.GetInt32() >= 3,
            $"Expected >=3 tests after refresh, got: {state}");

        var list = await _app.InvokeAsync("uno.probe.tests.list");
        var tests = list.EnumerateArray().ToList();

        Assert.Contains(tests, t => t.GetProperty("fqn").GetString()?.Contains("AlwaysPasses") == true);
        Assert.Contains(tests, t => t.GetProperty("fqn").GetString()?.Contains("AlwaysFails") == true);
        Assert.Contains(tests, t => t.GetProperty("fqn").GetString()?.Contains("AlwaysSkipped") == true);
    }

    [Fact]
    public async Task RunAllTests_ProducesExpectedResults()
    {
        // Ensure tests are discovered first.
        await _app.InvokeAsync("uno.probe.tests.refresh");

        // Fire-and-forget start.
        await _app.InvokeAsync("uno.probe.tests.run-all");

        // Wait until the run finishes (up to 120 s — dotnet test has a cold-start cost).
        var runningState = await _app.PollAsync(
            "uno.probe.tests.is-running",
            s => !IsRunning(s),
            timeoutMs: 120_000);

        Assert.False(IsRunning(runningState), "Test run did not complete within timeout");

        // Verify results.
        var results = await _app.InvokeAsync("uno.probe.tests.results");
        var items = results.EnumerateArray().ToList();
        Assert.True(items.Count >= 3, $"Expected >=3 result entries, got {items.Count}: {results}");

        var byFqn = items.ToDictionary(
            i => i.GetProperty("fqn").GetString() ?? "",
            i => i.GetProperty("result").GetString() ?? "");

        var passing = byFqn.FirstOrDefault(kv => kv.Key.Contains("AlwaysPasses"));
        var failing = byFqn.FirstOrDefault(kv => kv.Key.Contains("AlwaysFails"));
        var skipped = byFqn.FirstOrDefault(kv => kv.Key.Contains("AlwaysSkipped"));

        Assert.False(string.IsNullOrEmpty(passing.Key), "AlwaysPasses not found in results");
        Assert.False(string.IsNullOrEmpty(failing.Key), "AlwaysFails not found in results");
        Assert.False(string.IsNullOrEmpty(skipped.Key), "AlwaysSkipped not found in results");

        Assert.Equal("Passing", passing.Value);
        Assert.Equal("Failing", failing.Value);
        Assert.Equal("Skipped", skipped.Value);
    }

    [Fact]
    public async Task StopTests_CancelsRunInFlight()
    {
        await _app.InvokeAsync("uno.probe.tests.refresh");

        // Start a run and immediately request stop.
        await _app.InvokeAsync("uno.probe.tests.run-all");
        await _app.InvokeAsync("uno.probe.tests.stop");

        // The service must report IsRunning=false within a few seconds.
        var state = await _app.PollAsync(
            "uno.probe.tests.is-running",
            s => !IsRunning(s),
            timeoutMs: 15_000);

        Assert.False(IsRunning(state), "Test run was not stopped within timeout after StopTests");
    }
}
