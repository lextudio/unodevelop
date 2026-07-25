using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Services;

internal static class SolutionExplorerTreeBuilder
{
    // When false (default), bin/obj/.git and hidden files/folders are filtered out
    // of the directory tree. Toggled by the "Show All Files" toolbar command,
    // mirroring WPF SharpDevelop's ProjectBrowser ShowAll toggle.
    public static bool ShowAllFiles { get; set; }

    // Project paths visited during the current CreateSolutionNodeAsync call, used to prune
    // DependenciesSnapshotSession instances (Microsoft.VisualStudio.ProjectSystem.Managed,
    // slice 47) for projects no longer in the solution. There's no reliable "project removed"
    // event to hook per-project cleanup off of (see docs/project-system.md, Slice 48), so instead
    // every full rebuild reconciles sessions against whichever projects were actually visited.
    // Single-threaded like the rest of Solution Explorer's rebuild path (see MainPage.xaml.cs's
    // _isRefreshingSolutionTree guard), so a plain static list is safe.
    private static readonly List<string> VisitedProjectPaths = new();

    public static string? ResolveBestSolutionPath(string projectRoot)
    {
        var candidates = new[]
        {
            Path.Combine(projectRoot, "UnoDevelop.slnx"),
            Path.Combine(projectRoot, "src", "UnoDevelop.slnx"),
            Path.Combine(projectRoot, "UnoDevelop.sln"),
            Path.Combine(projectRoot, "src", "UnoDevelop.sln"),
            Path.Combine(projectRoot, "Main", "SharpDevelop", "SharpDevelop.csproj"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var found = Directory.EnumerateFiles(projectRoot, "*.slnx", SearchOption.AllDirectories).FirstOrDefault();
        return found
            ?? Directory.EnumerateFiles(projectRoot, "*.sln", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
    }

    public static async Task<TreeViewNode> CreateSolutionNodeAsync(ISolution solution)
    {
        VisitedProjectPaths.Clear();

        var rootNode = new TreeViewNode
        {
            Content = new SolutionExplorerNodeContext(solution.Name, solution.FileName.ToString(), true, SolutionExplorerNodeKind.Solution, solution),
            IsExpanded = true
        };

        foreach (var item in solution.Items.CreateSnapshot())
        {
            var child = await CreateTreeNodeFromSolutionItemAsync(item);
            if (child is not null)
            {
                rootNode.Children.Add(child);
            }
        }

        await Microsoft.VisualStudio.ProjectSystem.Tree.Dependencies.SharpDevelopDependenciesSnapshotFactory.PruneSessionsExceptAsync(VisitedProjectPaths);

        return rootNode;
    }

    public static async Task<TreeViewNode> CreateSolutionNodeAsync(string solutionPath, string projectRoot)
    {
        VisitedProjectPaths.Clear();

        var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
        var rootNode = new TreeViewNode
        {
            Content = new SolutionExplorerNodeContext(solutionName, solutionPath, true, SolutionExplorerNodeKind.Solution),
            IsExpanded = true
        };

        XDocument document;
        try
        {
            document = XDocument.Load(solutionPath);
        }
        catch
        {
            return rootNode;
        }

        var projectPaths = document.Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(projectRoot, path!)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var projectPath in projectPaths)
        {
            rootNode.Children.Add(await CreateProjectNodeAsync(projectPath));
        }

        await Microsoft.VisualStudio.ProjectSystem.Tree.Dependencies.SharpDevelopDependenciesSnapshotFactory.PruneSessionsExceptAsync(VisitedProjectPaths);

        return rootNode;
    }

    private static bool IsHidden(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & System.IO.FileAttributes.Hidden) == System.IO.FileAttributes.Hidden;
        }
        catch
        {
            return false;
        }
    }

    public static TreeViewNode CreateDirectoryNode(SolutionExplorerNodeContext directory, int depth, int maxDepth)
    {
        var node = new TreeViewNode
        {
            Content = directory with { Kind = SolutionExplorerNodeKind.Folder },
            IsExpanded = depth < 1
        };

        if (depth >= maxDepth || !Directory.Exists(directory.FullPath))
        {
            return node;
        }

        foreach (var childDirectory in Directory.GetDirectories(directory.FullPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(childDirectory);
            if (!ShowAllFiles
                && (string.Equals(folderName, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(folderName, "obj", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(folderName, ".git", StringComparison.OrdinalIgnoreCase)
                    || IsHidden(childDirectory)))
            {
                continue;
            }

            node.Children.Add(CreateDirectoryNode(
                new SolutionExplorerNodeContext(folderName, childDirectory, true),
                depth + 1,
                maxDepth));
        }

        foreach (var childFile in Directory.GetFiles(directory.FullPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!ShowAllFiles && IsHidden(childFile))
            {
                continue;
            }

            node.Children.Add(new TreeViewNode
            {
                Content = new SolutionExplorerNodeContext(Path.GetFileName(childFile), childFile, false, SolutionExplorerNodeKind.File)
            });
        }

        return node;
    }

    private static async Task<TreeViewNode?> CreateTreeNodeFromSolutionItemAsync(ISolutionItem item)
    {
        if (item is ISolutionFolder folder)
        {
            var folderPath = TryGetSolutionItemPath(folder)
                ?? folder.ParentSolution.FileName.ToString();
            var folderNode = new TreeViewNode
            {
                Content = new SolutionExplorerNodeContext(GetSolutionItemDisplayName(folder), folderPath, true, SolutionExplorerNodeKind.Folder, folder),
                IsExpanded = true
            };

            foreach (var childItem in folder.Items.CreateSnapshot())
            {
                var child = await CreateTreeNodeFromSolutionItemAsync(childItem);
                if (child is not null)
                {
                    folderNode.Children.Add(child);
                }
            }

            return folderNode;
        }

        if (item is ISolutionFileItem fileItem)
        {
            var filePath = fileItem.FileName.ToString();
            return new TreeViewNode
            {
                Content = new SolutionExplorerNodeContext(Path.GetFileName(filePath), filePath, false, SolutionExplorerNodeKind.File, fileItem)
            };
        }

        if (item is IProject project)
        {
            var projectPath = TryGetSolutionItemPath(project);
            if (!string.IsNullOrEmpty(projectPath))
            {
                return await CreateProjectNodeAsync(projectPath, GetSolutionItemDisplayName(project), project);
            }

            return new TreeViewNode
            {
                Content = new SolutionExplorerNodeContext(GetSolutionItemDisplayName(project), item.ParentSolution.FileName.ToString(), true, SolutionExplorerNodeKind.Project, project),
                IsExpanded = true
            };
        }

        return null;
    }

    /// <summary>
    /// Rebuilds and splices in-place the <see cref="TreeViewNode"/> for one project, without
    /// rebuilding the rest of the solution tree — used by <c>OnProjectItemCollectionChanged</c>
    /// (MainPage.xaml.cs) so a single project's item add/remove doesn't trigger a full Solution
    /// Explorer refresh. Returns false (caller should fall back to a full refresh) if the project's
    /// node can't be found in the current tree — e.g. before the tree has been built once.
    /// See docs/project-system.md (Slice 49).
    /// </summary>
    public static async Task<bool> RefreshProjectNodeAsync(TreeView tree, string projectPath, IProject? project, string? displayName = null)
    {
        var location = FindNode(tree.RootNodes, projectPath);
        if (location is not ({ } parent, var index, { } oldNode))
        {
            return false;
        }

        var newNode = await CreateProjectNodeViaCpsAsync(projectPath, displayName, project);
        newNode.IsExpanded = oldNode.IsExpanded;

        var wasSelected = tree.SelectedNodes.Contains(oldNode);
        parent[index] = newNode;
        if (wasSelected)
        {
            tree.SelectedNodes.Remove(oldNode);
            tree.SelectedNodes.Add(newNode);
        }

        return true;
    }

    private static (IList<TreeViewNode> Parent, int Index, TreeViewNode Node)? FindNode(IList<TreeViewNode> nodes, string projectPath)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Content is SolutionExplorerNodeContext { Kind: SolutionExplorerNodeKind.Project } context
                && string.Equals(context.FullPath, projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return (nodes, i, nodes[i]);
            }

            var found = FindNode(nodes[i].Children, projectPath);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static Task<TreeViewNode> CreateProjectNodeAsync(string projectPath, string? displayName = null, ISolutionItem? solutionItem = null)
    {
        return solutionItem is IProject liveProject
            ? CreateProjectNodeViaCpsAsync(projectPath, displayName, liveProject)
            : CreateProjectNodeViaCpsAsync(projectPath, displayName);
    }

    private static async Task<TreeViewNode> CreateProjectNodeViaCpsAsync(string projectPath, string? displayName, IProject? project = null)
    {
        VisitedProjectPaths.Add(projectPath);

        var provider = project is not null
            ? new UnoDevelopProjectTreeProvider(project, ShowAllFiles)
            : new UnoDevelopProjectTreeProvider(projectPath, displayName, ShowAllFiles);
        var cpsRoot   = await provider.BuildTreeAsync();
        // Override caption if the caller supplied a display name (e.g. from the solution file).
        if (!string.IsNullOrWhiteSpace(displayName))
            cpsRoot.Caption = displayName;

        var tvNode = CpsTreeConverter.ToTreeViewNode(cpsRoot, project);
        // ToTreeViewNode returns null only for Unknown kind; ProjectRoot is always known.
        return tvNode ?? FallbackEmptyProjectNode(projectPath, displayName, project);
    }

    private static TreeViewNode FallbackEmptyProjectNode(string projectPath, string? displayName, IProject? project)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(projectPath) : displayName;
        return new TreeViewNode
        {
            Content    = new SolutionExplorerNodeContext(name, projectPath, true, SolutionExplorerNodeKind.Project, project),
            IsExpanded = true,
        };
    }

    private static string? TryGetSolutionItemPath(ISolutionItem item)
    {
        if (item is ISolutionFileItem fileItem)
        {
            return fileItem.FileName.ToString();
        }

        var type = item.GetType();

        var projectFilePathProperty = type.GetProperty("ProjectFilePath");
        if (projectFilePathProperty?.GetValue(item) is string projectFilePath
            && !string.IsNullOrWhiteSpace(projectFilePath))
        {
            return projectFilePath;
        }

        var fileNameProperty = type.GetProperty("FileName");
        var fileNameValue = fileNameProperty?.GetValue(item);
        if (fileNameValue is not null)
        {
            var asText = fileNameValue.ToString();
            if (!string.IsNullOrWhiteSpace(asText))
            {
                return asText;
            }
        }

        return null;
    }

    private static string GetSolutionItemDisplayName(ISolutionItem item)
    {
        if (item is ISolutionFolder folder)
        {
            return folder.Name;
        }

        if (item is ISolutionFileItem fileItem)
        {
            return Path.GetFileName(fileItem.FileName.ToString());
        }

        var type = item.GetType();

        var projectNameProperty = type.GetProperty("ProjectName");
        if (projectNameProperty?.GetValue(item) is string projectName && !string.IsNullOrWhiteSpace(projectName))
        {
            return projectName;
        }

        var nameProperty = type.GetProperty("Name");
        if (nameProperty?.GetValue(item) is string name && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var path = TryGetSolutionItemPath(item);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        return item.GetType().Name;
    }

}
