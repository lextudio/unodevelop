using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace UnoDevelop.IntegrationTests;

/// <summary>
/// Covers GitStatusService (Solution Explorer's git-status decoration - lives directly in the
/// app, not the pluggable GitAddIn, since the tree must show status regardless of whether that
/// AddIn is enabled). Uses a disposable, isolated git repository under the OS temp directory
/// rather than the real UnoDevelop repo's own working tree, so the test's expected statuses don't
/// depend on whatever happens to be modified/staged in this checkout when the suite runs.
/// </summary>
[Collection("UnoDevelop app")]
public sealed class GitStatusTests : IDisposable
{
    readonly UnoDevelopAppFixture _app;
    readonly string _repoDir;

    public GitStatusTests(UnoDevelopAppFixture app)
    {
        _app = app;
        _repoDir = Path.Combine(Path.GetTempPath(), "unodevelop-git-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repoDir, recursive: true); } catch { }
    }

    static void RunGit(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git");
        process.WaitForExit(10_000);
        Assert.True(process.ExitCode == 0, $"git {arguments} failed: {process.StandardError.ReadToEnd()}");
    }

    [Fact]
    public async Task GitStatus_ReflectsModifiedUntrackedAndCleanFiles()
    {
        var projectPath = Path.Combine(_repoDir, "GitStatusFixture.csproj");
        var cleanFilePath = Path.Combine(_repoDir, "CleanFile.cs");
        var modifiedFilePath = Path.Combine(_repoDir, "ModifiedFile.cs");
        var untrackedFilePath = Path.Combine(_repoDir, "UntrackedFile.cs");

        File.WriteAllText(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(cleanFilePath, "namespace GitStatusFixture;\npublic class CleanFile { }\n");
        File.WriteAllText(modifiedFilePath, "namespace GitStatusFixture;\npublic class ModifiedFile { }\n");

        RunGit(_repoDir, "init");
        RunGit(_repoDir, "config user.email test@example.com");
        RunGit(_repoDir, "config user.name Test");
        RunGit(_repoDir, "add -A");
        RunGit(_repoDir, "commit -m initial");

        // Diverge from the commit: modify a tracked file, add a new untracked one. CleanFile is
        // left untouched as the "no status" control.
        File.AppendAllText(modifiedFilePath, "// modified after commit\n");
        File.WriteAllText(untrackedFilePath, "namespace GitStatusFixture;\npublic class UntrackedFile { }\n");

        await _app.InvokeAsync("ide-open-project", projectPath);

        // OnSolutionOpened/OnCurrentSolutionChanged (which trigger the tree rebuild and git status
        // refresh) are async-void event handlers fired without being awaited by OpenSolutionOrProject,
        // so ide-open-project can return before the refresh actually finishes - poll instead of
        // asserting immediately.
        var modifiedStatus = await _app.PollAsync("ide-git-status",
            s => s.GetProperty("status").GetString() != "None",
            args: new object[] { modifiedFilePath });
        Assert.Equal("Modified", modifiedStatus.GetProperty("status").GetString());

        var untrackedStatus = await _app.InvokeAsync("ide-git-status", untrackedFilePath);
        Assert.Equal("Untracked", untrackedStatus.GetProperty("status").GetString());

        var cleanStatus = await _app.InvokeAsync("ide-git-status", cleanFilePath);
        Assert.Equal("None", cleanStatus.GetProperty("status").GetString());
    }
}
