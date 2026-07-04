using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Workbench;
using ICSharpCode.Core;

namespace UnoDevelop.Services;

internal sealed class RunService
{
    private Process? _process;
    private CancellationTokenSource? _cts;

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; } // process was never started or already disposed
        }
    }

    public event EventHandler? RunStarted;
    public event EventHandler? RunStopped;

    public async Task StartAsync(string projectPath, IOutputCategory output, bool build = true)
    {
        if (IsRunning) return;

        output.AppendLine($"> dotnet run --project \"{projectPath}\"");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\"",
            WorkingDirectory = System.IO.Path.GetDirectoryName(projectPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };
        _process.Exited += (_, _) =>
        {
            output.AppendLine($"\n> Process exited with code {(_process.HasExited ? _process.ExitCode : 0)}.");
            RunStopped?.Invoke(this, EventArgs.Empty);
        };

        try
        {
            _process.Start();
        }
        catch (Exception ex)
        {
            output.AppendLine($"ERROR: failed to start process: {ex.Message}");
            _process = null;
            RunStopped?.Invoke(this, EventArgs.Empty);
            return;
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        RunStarted?.Invoke(this, EventArgs.Empty);

        await Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process = null;
    }
}
