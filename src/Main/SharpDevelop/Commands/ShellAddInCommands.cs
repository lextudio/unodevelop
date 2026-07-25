using System;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using UnoDevelop.Services;

namespace UnoDevelop.Commands;

internal sealed class OpenSolutionOrProjectShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = RunAsync();

    private static async Task RunAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Solution or Project",
            Filter = "Solution and Project files|*.sln;*.slnx;*.csproj|All files|*.*",
            FilterIndex = 1,
        };
        if (await dlg.ShowDialogAsync() != true || string.IsNullOrEmpty(dlg.FileName))
            return;

        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        var fileName = FileName.Create(dlg.FileName);
        if (fileName is null) return;

        if (!projectService.OpenSolutionOrProject(fileName))
        {
            ServiceSingleton.GetRequiredService<IMessageService>()
                .ShowError("Failed to open: " + dlg.FileName);
        }
    }
}

internal sealed class CloseSolutionShellCommand : AbstractMenuCommand
{
    public override bool IsEnabled =>
        ServiceSingleton.GetRequiredService<IProjectService>().CurrentSolution?.Projects?.Count > 0;

    public override void Run()
    {
        ServiceSingleton.GetRequiredService<IProjectService>().CloseSolution();
    }
}

internal sealed class ExitShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var shutdownService = ServiceSingleton.GetRequiredService<IShutdownService>();
        if (!shutdownService.Shutdown())
        {
            ServiceSingleton.GetRequiredService<IMessageService>()
                .ShowWarning("Shutdown is currently blocked: " + shutdownService.CurrentReasonPreventingShutdown);
            return;
        }

        App.MainWindow?.Close();
    }
}

internal sealed class BuildSolutionShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.BuildSolutionAsync();
}

internal sealed class CancelBuildShellCommand : AbstractMenuCommand
{
    public override void Run() => MainPage.Current?.CancelBuild();
}

internal sealed class RunWithoutDebuggingShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.RunWithoutDebuggingAsync();
}

internal sealed class StopRunShellCommand : AbstractMenuCommand
{
    public override void Run() => MainPage.Current?.StopRunOrDebug();
}

internal sealed class DebugShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.StartDebugAsync();
}

internal sealed class ContinueDebugShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.ContinueDebugAsync();
}

internal sealed class StepOverDebugShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.StepOverDebugAsync();
}

internal sealed class StepIntoDebugShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.StepIntoDebugAsync();
}

internal sealed class StepOutDebugShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.StepOutDebugAsync();
}

internal sealed class RefreshTestsShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.RefreshTestsAsync();
}

internal sealed class RunAllTestsShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.RunAllTestsAsync();
}

internal sealed class StopTestsShellCommand : AbstractMenuCommand
{
    public override void Run() => MainPage.Current?.StopTests();
}

internal sealed class RunSelectedTestShellCommand : AbstractMenuCommand
{
    public override void Run() => MainPage.Current?.RunSelectedTest();
}

internal sealed class ExpandAllTestsShellCommand : AbstractMenuCommand
{
    public override void Run() => MainPage.Current?.ExpandAllTests();
}

internal sealed class CollapseAllTestsShellCommand : AbstractMenuCommand
{
    public override void Run() => MainPage.Current?.CollapseAllTests();
}
