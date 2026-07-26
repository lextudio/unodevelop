using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.Diagnostics.NETCore.Client;
using UnoDevelop.Debugger;

namespace UnoDevelop.Services;

internal sealed class DebugService : IDisposable, IDebuggerService
{
    private Process? _adapterProcess;
    private Process? _debuggeeProcess;
    private DapClient? _dap;
    private CancellationTokenSource? _cts;
    private int _activeThreadId;
    private int _currentStopSequence;
    private string? _currentFile;
    private int _currentLine;
    private int _session; // incremented each StartAsync, lets Exited handler ignore stale sessions
    private readonly ConcurrentDictionary<int, IReadOnlyList<StackFrameInfo>> _cachedStackFrames = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<VariableInfo>> _cachedLocals = new();
    private readonly ConcurrentDictionary<int, ThreadInfo> _cachedThreads = new();
    private readonly ConcurrentDictionary<string, ModuleInfo> _cachedModules = new();

    public bool IsDebugging => _debuggeeProcess is { HasExited: false };

    public bool IsProcessRunning => IsDebugging;

    public bool HasCache => _cachedStackFrames.Count > 0;

    public int CurrentThreadId => _activeThreadId;

    public int CurrentStopSequence => _currentStopSequence;

    public string? CurrentFile => _currentFile;

    public int CurrentLine => _currentLine;

    public event EventHandler? DebugStarted;
    public event EventHandler? DebugStopped;
    // (threadId, reason)
    public event Action<int, string>? Stopped;
    public event Action? Continued;
    public event Action? ThreadsChanged;
    // (filePath, lineNumber 1-based) — fired after stackTrace resolves on stopped
    public event Action<string, int>? ExecutionPositionChanged;

    // ── Public API ──────────────────────────────────────────────────────────

    public async Task StartAsync(string projectPath, IOutputCategory output)
    {
        if (IsDebugging) return;
        _session++;
        _cachedStackFrames.Clear();
        _cachedLocals.Clear();
        _cachedThreads.Clear();
        _cachedModules.Clear();
        _activeThreadId = 0;
        _currentFile = null;
        _currentLine = 0;

        output.AppendLine("> Building for debug...");
        var targetDll = await ResolveBuildOutputAsync(projectPath, output);
        if (targetDll is null)
        {
            output.AppendLine("ERROR: build failed or target DLL not found.");
            return;
        }

        var adapterDll = ResolveAdapterDll();
        if (adapterDll is null)
        {
            output.AppendLine("ERROR: SharpDbg adapter not found. Build the solution first.");
            return;
        }

        Dbg($"adapter={adapterDll}, target={targetDll}");
        output.AppendLine($"> Starting debug adapter...");

        _cts = new CancellationTokenSource();
        _adapterProcess = LaunchAdapter(adapterDll);

        _dap = new DapClient(
            _adapterProcess.StandardOutput.BaseStream,
            _adapterProcess.StandardInput.BaseStream);

        _dap.EventReceived += (evt, body) => OnDapEvent(evt, body, output);
        _dap.Start();

        var capturedSession = _session;
        _adapterProcess.Exited += (_, _) =>
        {
            if (_session != capturedSession) return;
            Dbg($"Adapter exited. ExitCode={_adapterProcess?.ExitCode}");
        };

        try
        {
            await HandshakeAsync(targetDll, projectPath, output, _cts.Token);
            DebugStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            output.AppendLine($"ERROR: DAP handshake failed: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        var dap = _dap;
        var adapterProcess = _adapterProcess;
        var debuggeeProcess = _debuggeeProcess;
        _dap = null;
        _adapterProcess = null;
        _debuggeeProcess = null;
        _activeThreadId = 0;
        _currentFile = null;
        _currentLine = 0;
        _cachedStackFrames.Clear();
        _cachedLocals.Clear();
        try
        {
            if (dap is not null && adapterProcess is { HasExited: false })
            {
                using var disconnectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                dap.SendRequestAsync("disconnect",
                    new JsonObject { ["terminateDebuggee"] = true },
                    disconnectCts.Token).GetAwaiter().GetResult();
            }
        }
        catch { }
        try { adapterProcess?.Kill(entireProcessTree: true); } catch { }
        try { debuggeeProcess?.Kill(entireProcessTree: true); } catch { }
        dap?.Dispose();
        _dap = null;
        _adapterProcess = null;
        _activeThreadId = 0;
        _currentFile = null;
        _currentLine = 0;
        _cachedStackFrames.Clear();
        _cachedLocals.Clear();
    }

    public Task ContinueAsync() =>
        _dap?.SendRequestAsync("continue",
            new JsonObject { ["threadId"] = _activeThreadId })
        ?? Task.CompletedTask;

    public Task StepOverAsync() =>
        _dap?.SendRequestAsync("next",
            new JsonObject { ["threadId"] = _activeThreadId })
        ?? Task.CompletedTask;

    public Task StepInAsync() =>
        _dap?.SendRequestAsync("stepIn",
            new JsonObject { ["threadId"] = _activeThreadId })
        ?? Task.CompletedTask;

    public Task StepOutAsync() =>
        _dap?.SendRequestAsync("stepOut",
            new JsonObject { ["threadId"] = _activeThreadId })
        ?? Task.CompletedTask;

    public Task PauseAsync() =>
        _dap?.SendRequestAsync("pause",
            new JsonObject { ["threadId"] = _activeThreadId })
        ?? Task.CompletedTask;

    public async Task<VariableInfo?> EvaluateAsync(string expression, int frameId = 0)
    {
        if (_dap is null) return null;
        if (!IsDebugging) return null;
        try
        {
            var args = new JsonObject
            {
                ["expression"] = expression,
                ["frameId"] = frameId,
                ["context"] = "hover",
            };
            var response = await _dap.SendRequestAsync("evaluate", args);
            if (response?["body"] is not JsonObject body) return null;
            return new VariableInfo(
                expression,
                body["result"]?.GetValue<string>() ?? string.Empty,
                body["type"]?.GetValue<string>() ?? string.Empty,
                body["variablesReference"]?.GetValue<int>() ?? 0,
                expression);
        }
        catch { return null; }
    }

    public async Task SetBreakpointsAsync(string filePath, IReadOnlyList<int> lines)
    {
        if (_dap is null) return;

        var bpArray = new System.Text.Json.Nodes.JsonArray();
        foreach (var line in lines)
            bpArray.Add(new JsonObject { ["line"] = line });

        var args = new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = filePath },
            ["breakpoints"] = bpArray,
        };
        Dbg($"SetBreakpointsAsync: file={filePath} lines=[{string.Join(",", lines)}]");
        var response = await _dap.SendRequestAsync("setBreakpoints", args);
        Dbg($"SetBreakpointsAsync: response={response?.ToJsonString() ?? "null"}");
    }

    public void Dispose() => Stop();

    // ── Private helpers ─────────────────────────────────────────────────────

    private static Process LaunchAdapter(string adapterDll)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{adapterDll}\" --interpreter=vscode --engineLogging=\"{Path.Combine(Path.GetTempPath(), "unodevelop-sharpdbg-adapter.log")}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        return process;
    }

    private async Task HandshakeAsync(string targetDll, string projectPath, IOutputCategory output, CancellationToken ct)
    {
        // 1. initialize
        var initArgs = new JsonObject
        {
            ["clientID"] = "UnoDevelop",
            ["clientName"] = "UnoDevelop",
            ["adapterID"] = "sharpdbg",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["supportsRunInTerminalRequest"] = false,
            ["supportsHandshakeRequest"] = true,
            ["supportsVariableType"] = true,
            ["supportsVariablePaging"] = true,
        };
        var initResponse = await _dap!.SendRequestAsync("initialize", initArgs, ct);
        Dbg($"Initialize response: {initResponse?.ToJsonString() ?? "null"}");

        // 2. Launch the target suspended and attach, matching SharpDbg's own
        // out-of-process test practice more closely than the launch request.
        _debuggeeProcess = LaunchDebuggee(targetDll, Path.GetDirectoryName(projectPath));
        Dbg($"Attach: pid={_debuggeeProcess.Id}");
        var attachArgs = new JsonObject
        {
            ["processId"] = _debuggeeProcess.Id,
            ["console"] = "internalConsole",
            ["justMyCode"] = true,
        };
        await _dap.SendRequestAsync("attach", attachArgs, ct);
        Dbg("Attach response received");

        // 3. Send existing bookmarks as breakpoints before configurationDone,
        //    matching the standard DAP configuration window.
        try
        {
            var allBookmarks = SD.BookmarkManager.Bookmarks.Where(b => b.FileName != null).ToList();
            Dbg($"Handshake: total bookmarks={allBookmarks.Count}");
            foreach (var bm in allBookmarks)
                Dbg($"Handshake: bookmark file={bm.FileName} line={bm.LineNumber}");

            var byFile = allBookmarks
                .GroupBy(b => b.FileName.ToString(), StringComparer.OrdinalIgnoreCase);
            foreach (var group in byFile)
            {
                var lines = group.Select(b => b.LineNumber).OrderBy(x => x).ToList();
                await SetBreakpointsAsync(group.Key, lines);
            }
        }
        catch (Exception ex)
        {
            Dbg($"Sync bookmarks error: {ex.Message}");
        }

        // 4. configurationDone
        Dbg("Sending configurationDone");
        await _dap.SendRequestAsync("configurationDone", null, ct);
        Dbg("configurationDone response received; resuming runtime");
        new DiagnosticsClient(_debuggeeProcess.Id).ResumeRuntime();
        Dbg("runtime resumed");

        output.AppendLine($"> Debugging: {Path.GetFileName(targetDll)}");
    }

    private static Process LaunchDebuggee(string targetDll, string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(targetDll) ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add(targetDll);
        psi.Environment["DOTNET_DefaultDiagnosticPortSuspend"] = "1";
        foreach (var envVar in new[]
        {
            "COMPLUS_FORCEENC", "COMPLUS_ReadyToRun", "COMPLUS_ZapDisable",
            "DOTNET_GCConserveMemory", "DOTNET_GCHeapCount", "DOTNET_GCNoAffinitize",
            "DOTNET_MODIFIABLE_ASSEMBLIES", "DOTNET_MULTILEVEL_LOOKUP", "DOTNET_TieredPGO",
            "DOTNET_gcServer", "_NO_DEBUG_HEAP"
        })
        {
            psi.Environment.Remove(envVar);
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();
        return process;
    }

    private void OnDapEvent(string evt, JsonObject? body, IOutputCategory output)
    {
        Dbg($"DAP event: {evt} body={body?.ToJsonString() ?? "null"}");
        switch (evt)
        {
            case "output":
                var text = body?["output"]?.GetValue<string>() ?? string.Empty;
                output.AppendLine(text.TrimEnd('\n', '\r'));
                break;
            case "thread":
                var threadId = body?["threadId"]?.GetValue<int>() ?? 0;
                if (threadId > 0)
                    _cachedThreads[threadId] = new ThreadInfo(threadId, $"Thread {threadId}");
                ThreadsChanged?.Invoke();
                break;
            case "module":
                if (body?["module"] is JsonObject module)
                {
                    var modulePath = module["path"]?.GetValue<string>();
                    var moduleName = module["name"]?.GetValue<string>() ?? Path.GetFileName(modulePath) ?? string.Empty;
                    var moduleKey = module["id"]?.ToString() ?? modulePath ?? moduleName;
                    _cachedModules[moduleKey] = new ModuleInfo(
                        _cachedModules.Count + 1,
                        moduleName,
                        modulePath,
                        module["isOptimized"]?.GetValue<bool>() ?? false);
                }
                break;
            case "stopped":
                _activeThreadId = body?["threadId"]?.GetValue<int>() ?? 0;
                if (_activeThreadId > 0)
                    _cachedThreads[_activeThreadId] = new ThreadInfo(_activeThreadId, $"Thread {_activeThreadId}");
                var reason = body?["reason"]?.GetValue<string>() ?? "stopped";
                _currentStopSequence++;
                _ = HandleStoppedAsync(_activeThreadId, reason);
                break;
            case "continued":
                _cachedStackFrames.Clear();
                _cachedLocals.Clear();
                _currentFile = null;
                _currentLine = 0;
                ExecutionPositionChanged?.Invoke(string.Empty, 0);
                Continued?.Invoke();
                break;
            case "terminated":
            case "exited":
                _cachedStackFrames.Clear();
                _cachedLocals.Clear();
                _activeThreadId = 0;
                _currentFile = null;
                _currentLine = 0;
                ExecutionPositionChanged?.Invoke(string.Empty, 0);
                DebugStopped?.Invoke(this, EventArgs.Empty);
                // Clear session state so IsDebugging returns false.
                // Do not call Stop() here — it sends a synchronous disconnect
                // that would deadlock while we are inside the DAP read loop.
                var oldProcess = _adapterProcess;
                _dap = null;
                _adapterProcess = null;
                // Kill the orphan adapter on a background thread.
                try { oldProcess?.Kill(entireProcessTree: true); } catch { }
                break;
        }
    }

    private async Task HandleStoppedAsync(int threadId, string reason)
    {
        await PrefetchAndCacheAsync(threadId);
        Stopped?.Invoke(threadId, reason);
        ThreadsChanged?.Invoke();
        _ = FetchExecutionPositionAsync(threadId);
    }

    private async Task FetchExecutionPositionAsync(int threadId)
    {
        if (_dap is null) return;
        if (_cachedStackFrames.TryGetValue(threadId, out var cached) && cached.Count > 0)
        {
            var f = cached[0];
            if (!string.IsNullOrEmpty(f.FilePath) && f.Line > 0)
            {
                _currentFile = f.FilePath;
                _currentLine = f.Line;
                ExecutionPositionChanged?.Invoke(f.FilePath!, f.Line);
            }
            return;
        }
        if (!IsDebugging) return;
        try
        {
            Dbg($"FetchExecutionPositionAsync: stackTrace for thread {threadId}");
            var args = new JsonObject { ["threadId"] = threadId, ["levels"] = 1 };
            var response = await _dap.SendRequestAsync("stackTrace", args);
            var frames = response?["body"]?["stackFrames"] as System.Text.Json.Nodes.JsonArray;
            var frame = frames?.Count > 0 ? frames[0] as JsonObject : null;
            if (frame is null) return;

            var filePath = frame["source"]?["path"]?.GetValue<string>() ?? string.Empty;
            var line = frame["line"]?.GetValue<int>() ?? 0;
            Dbg($"ExecutionPosition: {filePath}:{line}");
            if (!string.IsNullOrEmpty(filePath) && line > 0)
            {
                _currentFile = filePath;
                _currentLine = line;
                ExecutionPositionChanged?.Invoke(filePath, line);
            }
        }
        catch (Exception ex)
        {
            Dbg($"FetchExecutionPositionAsync failed: {ex.Message}");
        }
    }

    private async Task PrefetchAndCacheAsync(int threadId)
    {
        if (_dap is null) return;
        if (!IsDebugging) return;
        try
        {
            Dbg($"PrefetchAndCacheAsync: stack only, thread={threadId}");
            var stArgs = new JsonObject { ["threadId"] = threadId, ["levels"] = 10 };
            var stResp = await _dap.SendRequestAsync("stackTrace", stArgs);
            var frames = stResp?["body"]?["stackFrames"] as JsonArray;
            if (frames is null || frames.Count == 0) return;

            var frameList = new List<StackFrameInfo>(frames.Count);
            foreach (var fn in frames)
            {
                if (fn is not JsonObject f) continue;
                frameList.Add(new StackFrameInfo(
                    f["id"]?.GetValue<int>() ?? 0,
                    f["name"]?.GetValue<string>() ?? string.Empty,
                    f["source"]?["path"]?.GetValue<string>(),
                    f["line"]?.GetValue<int>() ?? 0));
            }
            _cachedStackFrames[threadId] = frameList;
        }
        catch (Exception ex)
        {
            Dbg($"PrefetchAndCacheAsync failed: {ex.Message}");
        }
    }

    // ── IDebuggerService implementation ────────────────────────────────────────

    public async Task<IReadOnlyList<StackFrameInfo>> GetStackFramesAsync(int threadId)
    {
        if (_dap is null) return Array.Empty<StackFrameInfo>();
        if (_cachedStackFrames.TryGetValue(threadId, out var cachedHit))
            return cachedHit;
        if (!IsDebugging)
        { Dbg("GetStackFramesAsync: adapter dead"); return Array.Empty<StackFrameInfo>(); }
        try
        {
            Dbg($"GetStackFramesAsync: thread={threadId}");
            var args = new JsonObject { ["threadId"] = threadId, ["levels"] = 10 };
            var response = await _dap.SendRequestAsync("stackTrace", args);
            var frames = response?["body"]?["stackFrames"] as JsonArray;
            if (frames is null) { Dbg("GetStackFramesAsync: no stackFrames"); return Array.Empty<StackFrameInfo>(); }

            Dbg($"GetStackFramesAsync: got {frames.Count} frames");
            var result = new List<StackFrameInfo>(frames.Count);
            foreach (var frameNode in frames)
            {
                if (frameNode is not JsonObject frame) continue;
                var id = frame["id"]?.GetValue<int>() ?? 0;
                var name = frame["name"]?.GetValue<string>() ?? string.Empty;
                var path = frame["source"]?["path"]?.GetValue<string>();
                var line = frame["line"]?.GetValue<int>() ?? 0;
                Dbg($"GetStackFramesAsync: frame id={id} name={name}");
                result.Add(new StackFrameInfo(id, name, path, line));
            }
            return result;
        }
        catch (Exception ex) { Dbg($"GetStackFramesAsync: exception {ex.Message}"); return Array.Empty<StackFrameInfo>(); }
    }

    public async Task<IReadOnlyList<VariableInfo>> GetLocalsAsync(int frameId)
    {
        if (_dap is null) { Dbg("GetLocalsAsync: _dap is null"); return Array.Empty<VariableInfo>(); }
        if (_cachedLocals.TryGetValue(frameId, out var cachedHit))
            return cachedHit;
        if (!IsDebugging)
        { Dbg("GetLocalsAsync: adapter dead"); return Array.Empty<VariableInfo>(); }
        try
        {
            Dbg($"GetLocalsAsync: frame={frameId}");
            // 1. Get scopes for the frame
            var scopesResponse = await _dap.SendRequestAsync("scopes",
                new JsonObject { ["frameId"] = frameId });
            var scopes = scopesResponse?["body"]?["scopes"] as JsonArray;
            if (scopes is null || scopes.Count == 0)
            { Dbg($"GetLocalsAsync: no scopes: {scopesResponse?.ToJsonString()}"); return Array.Empty<VariableInfo>(); }

            var scopeRef = (scopes[0] as JsonObject)?["variablesReference"]?.GetValue<int>() ?? 0;
            Dbg($"GetLocalsAsync: scopeRef={scopeRef}");
            if (scopeRef == 0) return Array.Empty<VariableInfo>();

            // 2. Get variables in that scope
            var varsResponse = await _dap.SendRequestAsync("variables",
                new JsonObject { ["variablesReference"] = scopeRef });
            var vars = varsResponse?["body"]?["variables"] as JsonArray;
            if (vars is null)
            { Dbg($"GetLocalsAsync: no variables: {varsResponse?.ToJsonString()}"); return Array.Empty<VariableInfo>(); }

            Dbg($"GetLocalsAsync: got {vars.Count} vars");
            var result = new List<VariableInfo>(vars.Count);
            foreach (var varNode in vars)
            {
                if (varNode is not JsonObject v) continue;
                var name = v["name"]?.GetValue<string>() ?? "";
                Dbg($"GetLocalsAsync: var {name}={v["value"]?.GetValue<string>()}");
                result.Add(new VariableInfo(name,
                    v["value"]?.GetValue<string>() ?? string.Empty,
                    v["type"]?.GetValue<string>() ?? string.Empty,
                    v["variablesReference"]?.GetValue<int>() ?? 0,
                    v["evaluateName"]?.GetValue<string>()));
            }
            return result;
        }
        catch (Exception ex) { Dbg($"GetLocalsAsync: exception {ex.Message}"); return Array.Empty<VariableInfo>(); }
    }

    public async Task<IReadOnlyList<VariableInfo>> GetChildrenAsync(int variablesReference)
    {
        if (_dap is null) return Array.Empty<VariableInfo>();
        if (!IsDebugging) return Array.Empty<VariableInfo>();
        try
        {
            var response = await _dap.SendRequestAsync("variables",
                new JsonObject { ["variablesReference"] = variablesReference });
            var vars = response?["body"]?["variables"] as JsonArray;
            if (vars is null) return Array.Empty<VariableInfo>();

            var result = new List<VariableInfo>(vars.Count);
            foreach (var varNode in vars)
            {
                if (varNode is not JsonObject v) continue;
                result.Add(new VariableInfo(
                    v["name"]?.GetValue<string>() ?? string.Empty,
                    v["value"]?.GetValue<string>() ?? string.Empty,
                    v["type"]?.GetValue<string>() ?? string.Empty,
                    v["variablesReference"]?.GetValue<int>() ?? 0,
                    v["evaluateName"]?.GetValue<string>()));
            }
            return result;
        }
        catch { return Array.Empty<VariableInfo>(); }
    }

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync()
    {
        IReadOnlyList<ThreadInfo> threads = _cachedThreads.Values.OrderBy(t => t.Id).ToArray();
        return Task.FromResult(threads);
    }

    public Task<IReadOnlyList<ModuleInfo>> GetModulesAsync()
    {
        IReadOnlyList<ModuleInfo> modules = _cachedModules.Values.OrderBy(m => m.Name).ToArray();
        return Task.FromResult(modules);
    }

    /// Run `dotnet build` and extract the output DLL via MSBuild property.
    private static async Task<string?> ResolveBuildOutputAsync(string projectPath, IOutputCategory output)
    {
        output.AppendLine("> Building debug target...");
        var initialBuildPsi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Debug --nologo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using (var build = Process.Start(initialBuildPsi)!)
        {
            var buildStdout = await build.StandardOutput.ReadToEndAsync();
            var buildStderr = await build.StandardError.ReadToEndAsync();
            await build.WaitForExitAsync();
            if (!string.IsNullOrWhiteSpace(buildStdout)) output.AppendLine(buildStdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(buildStderr)) output.AppendLine(buildStderr.TrimEnd());
            if (build.ExitCode != 0) return null;
        }

        // Ask MSBuild for TargetPath directly — fast, no need to parse build log
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"msbuild \"{projectPath}\" -getProperty:TargetPath -p:Configuration=Debug",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();

        // dotnet msbuild -getProperty outputs just the value on stdout
        var dll = stdout.Trim();
        if (string.IsNullOrWhiteSpace(dll) || !File.Exists(dll))
        {
            // Fall back: build first, then re-query
            output.AppendLine("> Build output not found, running full build...");
            var buildPsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" -c Debug",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var bp = Process.Start(buildPsi)!;
            bp.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            bp.BeginOutputReadLine();
            await bp.WaitForExitAsync();
            if (bp.ExitCode != 0) return null;

            using var p2 = Process.Start(psi)!;
            dll = (await p2.StandardOutput.ReadToEndAsync()).Trim();
            await p2.WaitForExitAsync();
        }

        return File.Exists(dll) ? dll : null;
    }

    /// Find SharpDbg.Cli.dll — next to our own binary in Debugger/, or in the submodule bin dir.
    private static string? ResolveAdapterDll()
    {
        // 1. Bundled next to the app (production / after CopySharpDbgToOutput target)
        var bundled = Path.Combine(AppContext.BaseDirectory, "Debugger", "SharpDbg.Cli.dll");
        if (File.Exists(bundled)) return bundled;

        // 2. Submodule artifacts output (development — SharpDbg uses artifacts/ layout)
        var repo = FindRepoRoot(AppContext.BaseDirectory);
        if (repo is not null)
        {
            foreach (var config in new[] { "debug", "release" })
            {
                var dev = Path.Combine(repo, "externals", "sharpdbg", "artifacts",
                    "bin", "SharpDbg.Cli", config, "SharpDbg.Cli.dll");
                if (File.Exists(dev)) return dev;
            }
        }

        return null;
    }

    private static string? FindRepoRoot(string start)
    {
        var dir = start;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, ".gitmodules"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void Dbg(string msg)
    {
        try { File.AppendAllText("/tmp/unodevelop-debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] DebugService: {msg}\n"); } catch { }
    }
}
