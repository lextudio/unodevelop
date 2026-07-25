using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace UnoDevelop.IntegrationTests;

public sealed class UnoDevelopAppFixture : IAsyncLifetime
{
    static readonly int Port = int.TryParse(
        Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT"), out var p) && p > 0 ? p : 9227;
    static readonly string BaseUrl = $"http://localhost:{Port}";

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(240) };
    Process? _app;

    public string UnoDevelopProjectPath { get; } = LocateUnoDevelopProject();
    public string FixtureSolutionPath { get; } = LocateFixture("SampleTestProject", "SampleTestProject.csproj");
    public string DebugTestProjectPath { get; } = LocateFixture("DebugTestApp", "DebugTestApp.csproj");

    public async Task InitializeAsync()
    {
        StopApp();
        await WaitForPortFreeAsync(TimeSpan.FromSeconds(30));
        await StartAsync();
    }

    public async Task DisposeAsync()
    {
        StopApp();
        _http.Dispose();
        await Task.CompletedTask;
    }

    async Task StartAsync()
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(UnoDevelopProjectPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "run", "--project", UnoDevelopProjectPath, "-f", "net10.0-desktop", "--no-build" })
            psi.ArgumentList.Add(a);

        psi.Environment["UNODEVELOP_OPEN_ON_START"] = FixtureSolutionPath;

        _app = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start UnoDevelop");
        _app.OutputDataReceived += (_, _) => { };
        _app.ErrorDataReceived += (_, _) => { };
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();

        await WaitForAgentAsync(TimeSpan.FromSeconds(120));
        await WarmUpAsync(TimeSpan.FromSeconds(60));
    }

    async Task WarmUpAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var s = await InvokeRawAsync("uno.probe.tests.is-running");
                if (s.TryGetProperty("isRunning", out _)) return;
            }
            catch { }
            await Task.Delay(500);
        }
        throw new TimeoutException("UnoDevelop UI did not become responsive to probes within " + timeout);
    }

    void StopApp()
    {
        foreach (var name in new[] { "UnoDevelop", "SharpDbg.Cli", "DebugTestApp" })
        {
            try { foreach (var proc in Process.GetProcessesByName(name)) { try { proc.Kill(true); } catch { } } } catch { }
        }
        try { if (_app is { HasExited: false }) _app.Kill(entireProcessTree: true); } catch { }
        _app = null;
    }

    async Task WaitForAgentAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var resp = await _http.GetAsync($"{BaseUrl}/api/v1/agent/status");
                if (resp.IsSuccessStatusCode) return;
            }
            catch { }
            await Task.Delay(1000);
        }
        throw new TimeoutException($"DevFlow agent did not respond on {BaseUrl} within {timeout}.");
    }

    async Task WaitForPortFreeAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Process.GetProcessesByName("UnoDevelop").Length == 0 && !IsPortInUse(Port))
                return;
            await Task.Delay(500);
        }
    }

    static bool IsPortInUse(int port)
    {
        try
        {
            return System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners().Any(ep => ep.Port == port);
        }
        catch { return false; }
    }

    public async Task<JsonElement> GetStatusAsync()
    {
        using var resp = await _http.GetAsync($"{BaseUrl}/api/v1/agent/status");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> InvokeAsync(string action, params object[] args)
    {
        var state = await InvokeRawAsync(action, args);
        if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("error", out var probeErr))
            throw new InvalidOperationException($"Probe '{action}' reported error: {probeErr.GetString()}");
        return state;
    }

    // Invoke an action and return the raw string from returnValue (for plain-text results).
    public async Task<string> InvokeStringAsync(string action, params object[] args)
    {
        var body = JsonSerializer.Serialize(new { args });
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{BaseUrl}/api/v1/invoke/actions/{action}", content);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Probe '{action}' failed ({(int)resp.StatusCode}): {err}");
        }
        var envelope = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var raw = envelope.TryGetProperty("returnValue", out var rv) ? rv.GetString() : null;
        return raw ?? "";
    }

    async Task<JsonElement> InvokeRawAsync(string action, params object[] args)
    {
        var body = JsonSerializer.Serialize(new { args });
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{BaseUrl}/api/v1/invoke/actions/{action}", content);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Probe '{action}' failed ({(int)resp.StatusCode}): {err}");
        }
        var envelope = await resp.Content.ReadFromJsonAsync<JsonElement>();
        if (envelope.TryGetProperty("returnValue", out var rv))
        {
            if (rv.ValueKind == JsonValueKind.String)
                return JsonDocument.Parse(rv.GetString()!).RootElement.Clone();
            return rv.Clone();
        }
        throw new InvalidOperationException($"Probe '{action}' returned no returnValue: {envelope}");
    }

    public async Task<JsonElement> PollAsync(
        string action, Func<JsonElement, bool> predicate,
        int timeoutMs = 30_000, params object[] args)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        JsonElement last = default;
        while (DateTime.UtcNow < deadline)
        {
            last = await InvokeAsync(action, args);
            if (predicate(last)) return last;
            await Task.Delay(500);
        }
        return last;
    }

    static string LocateUnoDevelopProject()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "Main", "SharpDevelop", "SharpDevelop.csproj");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "Could not locate src/Main/SharpDevelop/SharpDevelop.csproj by walking up from " + AppContext.BaseDirectory);
    }

    static string LocateFixture(string fixtureDir, string projectFile)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "tests", "fixtures", fixtureDir, projectFile);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"Could not locate tests/fixtures/{fixtureDir}/{projectFile} by walking up from " + AppContext.BaseDirectory);
    }
}

[CollectionDefinition("UnoDevelop app")]
public sealed class UnoDevelopAppCollection : ICollectionFixture<UnoDevelopAppFixture> { }
