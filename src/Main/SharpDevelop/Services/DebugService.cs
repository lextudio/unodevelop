using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Debugger.AddIn.Service.Dap;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using UnoDevelop.Debugger;

namespace UnoDevelop.Services;

// Thin glue between UnoDevelop's IDebuggerService/pads contract and the shared DapSession
// (Debugger.AddIn/Service/Dap - see doc/technotes/debugging.md). Owns only what's genuinely
// UnoDevelop-specific: MSBuild build+TargetPath resolution, the per-request result cache the pads
// rely on, and translating DapModels' typed shapes to this host's own StackFrameInfo/VariableInfo/
// ThreadInfo/ModuleInfo records.
internal sealed class DebugService : IDisposable, IDebuggerService
{
    private DapSession? _session;
    private int _activeThreadId;
    private int _currentStopSequence;
    private string? _currentFile;
    private int _currentLine;
    private readonly ConcurrentDictionary<int, IReadOnlyList<StackFrameInfo>> _cachedStackFrames = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<VariableInfo>> _cachedLocals = new();

    public bool IsDebugging => _session?.IsRunning ?? false;

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
        ResetState();

        output.AppendLine("> Building for debug...");
        var targetDll = await ResolveBuildOutputAsync(projectPath, output);
        if (targetDll is null)
        {
            output.AppendLine("ERROR: build failed or target DLL not found.");
            return;
        }

        output.AppendLine("> Starting debug adapter...");

        var session = new DapSession(
            clientId: "UnoDevelop",
            log: Dbg,
            sharpDbgArtifactsPathFromRepoRoot: ["externals", "OpenDevelop", "externals", "sharpdbg"]);
        session.Started += () => DebugStarted?.Invoke(this, EventArgs.Empty);
        session.Stopped += args => OnStopped(args.ThreadId, args.Reason);
        session.Continued += OnContinued;
        session.Exited += OnExited;
        session.OutputReceived += text => output.AppendLine(text.TrimEnd('\n', '\r'));
        _session = session;

        try
        {
            // The session itself spawns the debuggee suspended and attaches to it (rather than the
            // adapter launching it via "launch") to match SharpDbg's own out-of-process test
            // practice more closely - see DapLaunchMode.AttachToSuspendedProcess.
            await session.StartAsync(targetDll, Path.GetDirectoryName(projectPath), breakAtBeginning: false,
                DapLaunchMode.AttachToSuspendedProcess);

            // Breakpoints must be sent after the session starts but before configurationDone -
            // most DAP adapters (including SharpDbg) ignore breakpoints set any later.
            await SyncAllBreakpointsAsync();

            await session.ConfigurationDoneAsync();
            output.AppendLine($"> Debugging: {Path.GetFileName(targetDll)}");
        }
        catch (Exception ex)
        {
            output.AppendLine($"ERROR: DAP handshake failed: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        var session = _session;
        _session = null;
        ResetState();
        session?.Stop();
        session?.Dispose();
    }

    public Task ContinueAsync() => WithSession(s => { s.Continue(); return Task.CompletedTask; });

    public Task StepOverAsync() => WithSession(s => { s.StepOver(); return Task.CompletedTask; });

    public Task StepInAsync() => WithSession(s => { s.StepInto(); return Task.CompletedTask; });

    public Task StepOutAsync() => WithSession(s => { s.StepOut(); return Task.CompletedTask; });

    public Task PauseAsync() => WithSession(s => { s.Break(); return Task.CompletedTask; });

    public async Task<VariableInfo?> EvaluateAsync(string expression, int frameId = 0)
    {
        if (_session is null || !IsDebugging) return null;
        try
        {
            var result = await _session.EvaluateAsync(expression, frameId == 0 ? (int?)null : frameId, "hover");
            return new VariableInfo(expression, result.Value, result.Type ?? string.Empty, result.VariablesReference, expression);
        }
        catch { return null; }
    }

    public async Task SetBreakpointsAsync(string filePath, IReadOnlyList<int> lines)
    {
        if (_session is null) return;
        Dbg($"SetBreakpointsAsync: file={filePath} lines=[{string.Join(",", lines)}]");
        await _session.SetBreakpointsAsync(filePath, lines.Select(line => (line, (string?)null, (string?)null)).ToList());
    }

    public void Dispose() => Stop();

    // ── Private helpers ─────────────────────────────────────────────────────

    private Task WithSession(Func<DapSession, Task> action) =>
        _session is null ? Task.CompletedTask : action(_session);

    private void ResetState()
    {
        _cachedStackFrames.Clear();
        _cachedLocals.Clear();
        _activeThreadId = 0;
        _currentFile = null;
        _currentLine = 0;
    }

    private async Task SyncAllBreakpointsAsync()
    {
        try
        {
            var allBookmarks = SD.BookmarkManager.Bookmarks.Where(b => b.FileName != null).ToList();
            var byFile = allBookmarks.GroupBy(b => b.FileName.ToString(), StringComparer.OrdinalIgnoreCase);
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
    }

    private void OnStopped(int threadId, string reason)
    {
        _activeThreadId = threadId;
        _currentStopSequence++;
        _cachedStackFrames.Clear();
        _cachedLocals.Clear();
        _ = HandleStoppedAsync(threadId, reason);
    }

    private void OnContinued()
    {
        _cachedStackFrames.Clear();
        _cachedLocals.Clear();
        _currentFile = null;
        _currentLine = 0;
        ExecutionPositionChanged?.Invoke(string.Empty, 0);
        Continued?.Invoke();
    }

    private void OnExited()
    {
        ResetState();
        ExecutionPositionChanged?.Invoke(string.Empty, 0);
        DebugStopped?.Invoke(this, EventArgs.Empty);
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
        var frames = await GetStackFramesAsync(threadId);
        var f = frames.FirstOrDefault();
        if (f is not null && !string.IsNullOrEmpty(f.FilePath) && f.Line > 0)
        {
            _currentFile = f.FilePath;
            _currentLine = f.Line;
            ExecutionPositionChanged?.Invoke(f.FilePath!, f.Line);
        }
    }

    private async Task PrefetchAndCacheAsync(int threadId)
    {
        // GetStackFramesAsync already caches by threadId, so just prime the cache.
        await GetStackFramesAsync(threadId);
    }

    private static StackFrameInfo ToStackFrameInfo(DapStackFrameInfo f) =>
        new(f.Id, f.Name, f.FilePath, f.Line);

    private static VariableInfo ToVariableInfo(DapVariableInfo v) =>
        new(v.Name, v.Value, v.Type ?? string.Empty, v.VariablesReference, v.EvaluateName);

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

    // ── IDebuggerService implementation ────────────────────────────────────────

    public async Task<IReadOnlyList<StackFrameInfo>> GetStackFramesAsync(int threadId)
    {
        if (_session is null) return Array.Empty<StackFrameInfo>();
        if (_cachedStackFrames.TryGetValue(threadId, out var cachedHit))
            return cachedHit;
        if (!IsDebugging) return Array.Empty<StackFrameInfo>();
        try
        {
            var frames = await _session.GetStackFramesAsync(threadId, levels: 10);
            var result = frames.Select(ToStackFrameInfo).ToList();
            _cachedStackFrames[threadId] = result;
            return result;
        }
        catch (Exception ex) { Dbg($"GetStackFramesAsync: exception {ex.Message}"); return Array.Empty<StackFrameInfo>(); }
    }

    public async Task<IReadOnlyList<VariableInfo>> GetLocalsAsync(int frameId)
    {
        if (_session is null) return Array.Empty<VariableInfo>();
        if (_cachedLocals.TryGetValue(frameId, out var cachedHit))
            return cachedHit;
        if (!IsDebugging) return Array.Empty<VariableInfo>();
        try
        {
            var scopes = await _session.GetScopesAsync(frameId);
            var scope = scopes.FirstOrDefault();
            if (scope is null || scope.VariablesReference == 0) return Array.Empty<VariableInfo>();

            var vars = await _session.GetVariablesAsync(scope.VariablesReference);
            var result = vars.Select(ToVariableInfo).ToList();
            _cachedLocals[frameId] = result;
            return result;
        }
        catch (Exception ex) { Dbg($"GetLocalsAsync: exception {ex.Message}"); return Array.Empty<VariableInfo>(); }
    }

    public async Task<IReadOnlyList<VariableInfo>> GetChildrenAsync(int variablesReference)
    {
        if (_session is null || !IsDebugging) return Array.Empty<VariableInfo>();
        try
        {
            var vars = await _session.GetVariablesAsync(variablesReference);
            return vars.Select(ToVariableInfo).ToList();
        }
        catch { return Array.Empty<VariableInfo>(); }
    }

    public async Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync()
    {
        if (_session is null || !IsDebugging) return Array.Empty<ThreadInfo>();
        try
        {
            var threads = await _session.GetThreadsAsync();
            return threads.Select(t => new ThreadInfo(t.Id, t.Name)).OrderBy(t => t.Id).ToList();
        }
        catch { return Array.Empty<ThreadInfo>(); }
    }

    public async Task<IReadOnlyList<ModuleInfo>> GetModulesAsync()
    {
        if (_session is null) return Array.Empty<ModuleInfo>();
        var modules = await _session.GetModulesAsync();
        return modules
            .Select((m, i) => new ModuleInfo(i + 1, m.Name, m.Path, m.IsOptimized))
            .OrderBy(m => m.Name)
            .ToList();
    }

    private static void Dbg(string msg)
    {
        try { File.AppendAllText("/tmp/unodevelop-debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] DebugService: {msg}\n"); } catch { }
    }
}
