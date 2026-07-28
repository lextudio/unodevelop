using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnoDevelop.Services;
using System.Runtime.InteropServices;
using ICSharpCode.SharpDevelop.Services;

namespace UnoDevelop.Commands;

internal abstract class SolutionExplorerCommandBase : AbstractMenuCommand
{
    protected IProjectBrowserController Controller => ServiceSingleton.GetRequiredService<IProjectBrowserController>();

    protected ProjectBrowserNodeContext? OwnerNode => Owner as ProjectBrowserNodeContext;

    protected IProject? ResolveOwnerProject()
    {
        if (OwnerNode is null)
        {
            return null;
        }

        var solution = SD.ProjectService.CurrentSolution;
        if (solution is null || solution.Projects is null)
        {
            return null;
        }

        if (OwnerNode.ProjectPathHint is string hintedPath
            && !string.IsNullOrWhiteSpace(hintedPath))
        {
            try
            {
                var normalizedHint = Path.GetFullPath(hintedPath);
                var byHint = solution.Projects.FirstOrDefault(project =>
                    string.Equals(Path.GetFullPath(project.FileName.ToString()), normalizedHint, StringComparison.OrdinalIgnoreCase));
                if (byHint is not null)
                {
                    return byHint;
                }
            }
            catch
            {
                // Ignore path normalization failures and continue with other resolution paths.
            }
        }

        if (OwnerNode.BoundProjectTree?.Root?.FilePath is string rootPath
            && !string.IsNullOrWhiteSpace(rootPath))
        {
            try
            {
                var normalizedRootPath = Path.GetFullPath(rootPath);
                var byTreeRootPath = solution.Projects.FirstOrDefault(project =>
                    string.Equals(Path.GetFullPath(project.FileName.ToString()), normalizedRootPath, StringComparison.OrdinalIgnoreCase));
                if (byTreeRootPath is not null)
                {
                    return byTreeRootPath;
                }
            }
            catch
            {
                // Ignore path normalization failures and continue with other resolution paths.
            }
        }

        if (!string.IsNullOrWhiteSpace(OwnerNode.FullPath))
        {
            try
            {
                var normalizedNodePath = Path.GetFullPath(OwnerNode.FullPath);
                var byPath = solution.Projects.FirstOrDefault(project =>
                    string.Equals(Path.GetFullPath(project.FileName.ToString()), normalizedNodePath, StringComparison.OrdinalIgnoreCase));
                if (byPath is not null)
                {
                    return byPath;
                }

                if ((OwnerNode.IsFileLike || OwnerNode.Kind == ProjectBrowserNodeKind.Folder)
                    && File.Exists(normalizedNodePath))
                {
                    var byContainingFile = SD.ProjectService.FindProjectContainingFile(FileName.Create(normalizedNodePath));
                    if (byContainingFile is not null)
                    {
                        return byContainingFile;
                    }
                }
            }
            catch
            {
                // Ignore path normalization failures and continue with other resolution paths.
            }
        }

        if (OwnerNode.Kind == ProjectBrowserNodeKind.Project)
        {
            var byName = solution.Projects.FirstOrDefault(project =>
                string.Equals(project.Name, OwnerNode.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        return null;
    }

    protected ProjectItem? ResolveOwnerProjectItem(IProject project)
    {
        if (OwnerNode is null)
        {
            return null;
        }

        var items = project.Items.CreateSnapshot();
        if (!string.IsNullOrWhiteSpace(OwnerNode.IncludeHint))
        {
            var byInclude = items.FirstOrDefault(item =>
                string.Equals(item.Include, OwnerNode.IncludeHint, StringComparison.OrdinalIgnoreCase));
            if (byInclude is not null)
            {
                return byInclude;
            }
        }

        if (!string.IsNullOrWhiteSpace(OwnerNode.FullPath) && File.Exists(OwnerNode.FullPath))
        {
            var normalizedPath = Path.GetFullPath(OwnerNode.FullPath);
            var byPath = items.OfType<FileProjectItem>().FirstOrDefault(item =>
                string.Equals(Path.GetFullPath(item.FileName.ToString()), normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (byPath is not null)
            {
                return byPath;
            }
        }

        return null;
    }
}

internal sealed class RefreshSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.Refresh();
}

internal sealed class OpenSolutionExplorerItemCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.Open(OwnerNode);
}

internal sealed class NewFolderSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.CreateFolder(OwnerNode);
}

internal sealed class NewFileSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.CreateFile(OwnerNode);
}

internal sealed class NewItemSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.AddNewItem(OwnerNode);
}

internal sealed class NewProjectSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.AddNewProject(OwnerNode);
}

internal sealed class AddExistingFileSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.AddExistingFile(OwnerNode);
}

internal sealed class AddExistingFolderSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.AddExistingFolder(OwnerNode);
}

internal sealed class RenameSolutionExplorerItemCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.Rename(OwnerNode);
}

internal sealed class DeleteSolutionExplorerItemCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.Delete(OwnerNode);
}

internal sealed class RemoveFromProjectSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override bool IsEnabled => OwnerNode is not null
        && (OwnerNode.Kind == ProjectBrowserNodeKind.Project
            || OwnerNode.Kind == ProjectBrowserNodeKind.File
            || OwnerNode.Kind == ProjectBrowserNodeKind.Folder);

    public override void Run() => Controller.RemoveFromProject(OwnerNode);
}

internal sealed class RemoveReferenceSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override bool IsEnabled => OwnerNode?.Kind is ProjectBrowserNodeKind.Reference
        or ProjectBrowserNodeKind.ProjectReference
        or ProjectBrowserNodeKind.PackageReference;

    public override void Run() => Controller.RemoveReference(OwnerNode);
}

internal sealed class OpenProjectReferenceSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override bool IsEnabled => OwnerNode?.Kind == ProjectBrowserNodeKind.ProjectReference;

    public override void Run() => Controller.OpenProjectReference(OwnerNode);
}

internal sealed class IncludeInProjectSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override bool IsEnabled => OwnerNode?.Kind == ProjectBrowserNodeKind.GhostFile;

    public override void Run() => Controller.IncludeInProject(OwnerNode);
}

internal sealed class ExcludeFromProjectSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override bool IsEnabled => OwnerNode?.Kind is ProjectBrowserNodeKind.File or ProjectBrowserNodeKind.LinkedFile;

    public override void Run() => Controller.ExcludeFromProject(OwnerNode);
}

internal sealed class OpenWithSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override bool IsEnabled => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public override void Run() => Controller.OpenWith(OwnerNode);
}

internal sealed class CopyPathSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.CopyPath(OwnerNode);
}

internal sealed class OpenFolderSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.OpenFolder(OwnerNode);
}

internal sealed class SetStartupProjectSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run() => Controller.SetStartupProject(OwnerNode);
}

// docs/nuget-manager.md slice 2: read-only installed-packages view.
internal sealed class ManageNuGetPackagesSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run()
    {
        var project = ResolveOwnerProject();
        if (project is not null)
        {
            _ = PackageManagementBridge.ShowManagePackagesAsync(project);
        }
    }
}

internal static class PackageManagementBridge
{
    public static Task ShowManagePackagesAsync(IProject project)
    {
        var type = ResolvePackageManagementType("UnoDevelop.AddIns.ManagePackagesDialog");
        var method = type?.GetMethod("ShowAsync", BindingFlags.Public | BindingFlags.Static);
        if (method?.Invoke(null, new object?[] { project, MainPage.Current?.XamlRoot }) is Task task)
        {
            return task;
        }

        ServiceSingleton.GetRequiredService<IMessageService>().ShowError("Package Management addin is not available.");
        return Task.CompletedTask;
    }

    private static Type? ResolvePackageManagementType(string typeName)
    {
        foreach (var addIn in ServiceSingleton.GetRequiredService<IAddInTree>().AddIns)
        {
            if (!addIn.Enabled || !addIn.Manifest.Identities.ContainsKey("UnoDevelop.PackageManagement"))
            {
                continue;
            }

            foreach (var runtime in addIn.Runtimes)
            {
                var type = runtime.LoadedAssembly?.GetType(typeName);
                if (type is not null)
                {
                    return type;
                }
            }
        }

        return Type.GetType(typeName + ", UnoDevelop.PackageManagement", throwOnError: false);
    }
}

// docs/t4-templating.md: per-file "Run Custom Tool" on a single .tt/.t4 file, without the full
// CustomToolsService port (auto-run-on-save, arbitrary ICustomTool registrations).
internal sealed class RunT4CustomToolSolutionExplorerCommand : SolutionExplorerCommandBase
{
    public override void Run()
    {
        var project = ResolveOwnerProject();
        if (project is null
            || ResolveOwnerProjectItem(project) is not ICSharpCode.SharpDevelop.Project.FileProjectItem file
            || !TextTemplatingBridge.IsT4File(file.FileName.ToString()))
        {
            return;
        }

        TextTemplatingBridge.RunIfApplicable(file, project);
    }
}

internal static class TextTemplatingBridge
{
    private const string RunnerTypeName = "ICSharpCode.TextTemplating.T4TemplateRunner";

    public static bool IsT4File(string fileName)
    {
        return Invoke<bool>(nameof(IsT4File), fileName);
    }

    public static void RunIfApplicable(FileProjectItem file, IProject project)
    {
        Invoke<object?>(nameof(RunIfApplicable), file, project);
    }

    private static T Invoke<T>(string methodName, params object[] args)
    {
        var runnerType = ResolveRunnerType();
        if (runnerType is null)
        {
            return default!;
        }

        var method = runnerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        if (method is null)
        {
            return default!;
        }

        var result = method.Invoke(null, args);
        return result is T value ? value : default!;
    }

    private static Type? ResolveRunnerType()
    {
        foreach (var addIn in ServiceSingleton.GetRequiredService<IAddInTree>().AddIns)
        {
            if (!addIn.Enabled || !addIn.Manifest.Identities.ContainsKey("UnoDevelop.TextTemplating"))
            {
                continue;
            }

            foreach (var runtime in addIn.Runtimes)
            {
                var type = runtime.LoadedAssembly?.GetType(RunnerTypeName);
                if (type is not null)
                {
                    return type;
                }
            }
        }

        return Type.GetType(RunnerTypeName + ", UnoDevelop.TextTemplating", throwOnError: false);
    }
}

internal sealed class CollapseAllSolutionExplorerCommand : AbstractMenuCommand
{
    public override void Run() => MainPage.Current?.CollapseAllSolutionTree();
}

// WPF SharpDevelop "Show All Files" toggle: flips the tree's hidden/bin/obj
// filter and rebuilds. Checkable so the toolbar toggle reflects the state.
internal sealed class ToggleShowAllFilesCommand : AbstractCheckableMenuCommand
{
    public override bool IsChecked
    {
        get => SolutionExplorerTreeBuilder.ShowAllFiles;
        set => SolutionExplorerTreeBuilder.ShowAllFiles = value;
    }

    public override void Run()
    {
        IsChecked = !IsChecked;
        ServiceSingleton.GetRequiredService<IProjectBrowserController>().Refresh();
    }
}

// WPF SharpDevelop "Properties" toolbar button: shows the property pad for the
// selected node.
internal sealed class ShowPropertiesForNodeCommand : SolutionExplorerCommandBase
{
    public override void Run() => MainPage.Current?.ShowPropertiesForNode(OwnerNode);
}

internal sealed class BuildSolutionCommand : AbstractMenuCommand
{
    public override void Run() => SD.BuildService.BuildAsync(SD.ProjectService.CurrentSolution,
        new ICSharpCode.SharpDevelop.Project.BuildOptions(ICSharpCode.SharpDevelop.Project.BuildTarget.Build));
}

internal sealed class CancelBuildCommand : AbstractMenuCommand
{
    public override void Run() => SD.BuildService.CancelBuild();
}
