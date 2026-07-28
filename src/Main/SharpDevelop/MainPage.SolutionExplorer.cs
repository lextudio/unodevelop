using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using UnoDevelop.Services;
using UnoDevelop.Workbench;
using ICSharpCode.SharpDevelop.Services;

namespace UnoDevelop;

public partial class MainPage
{
    private SolutionExplorerPad SolutionExplorerPad
        => _solutionExplorerPad ?? throw new InvalidOperationException("Solution Explorer pad has not been loaded.");

    private TreeView SolutionTree => SolutionExplorerPad.Tree;

    private System.Windows.Controls.ToolBar SolutionExplorerToolbar => SolutionExplorerPad.Toolbar;

    private async Task LoadSolutionTreeAsync()
    {
        var projectRoot = Directory.GetCurrentDirectory();
        SolutionTree.RootNodes.Clear();

        if (_projectService?.CurrentSolution is not null)
        {
            SolutionTree.RootNodes.Add(await SolutionExplorerTreeBuilder.CreateSolutionNodeAsync(_projectService.CurrentSolution));
            return;
        }

        var solutionPath = SolutionExplorerTreeBuilder.ResolveBestSolutionPath(projectRoot);
        if (solutionPath != null)
        {
            SolutionTree.RootNodes.Add(await SolutionExplorerTreeBuilder.CreateSolutionNodeAsync(solutionPath, projectRoot));
            return;
        }

        var rootItem = new ProjectBrowserNodeContext(Path.GetFileName(projectRoot), projectRoot, true);
        SolutionTree.RootNodes.Add(SolutionExplorerTreeBuilder.CreateDirectoryNode(rootItem, 0, 3));
    }

    private void EnsureProjectServiceSolutionLoaded(string projectRoot)
    {
        if (_projectService is null)
        {
            return;
        }

        if (_projectService.CurrentSolution?.Projects?.Count > 0)
        {
            return;
        }

        var lastDir = SD.PropertyService.Get("UnoDevelop.LastOpenDirectory", "");
        if (!string.IsNullOrEmpty(lastDir) && Directory.Exists(lastDir))
        {
            var lastSolution = SolutionExplorerTreeBuilder.ResolveBestSolutionPath(lastDir);
            if (lastSolution != null)
            {
                _projectService.OpenSolutionOrProject(FileName.Create(lastSolution)!);
                return;
            }
        }

        var solutionPath = SolutionExplorerTreeBuilder.ResolveBestSolutionPath(projectRoot);
        if (solutionPath != null)
        {
            _projectService.OpenSolutionOrProject(FileName.Create(solutionPath)!);
        }
    }

    internal void OnSolutionTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var context = args.InvokedItem switch
        {
            ProjectBrowserNodeContext ctx => ctx,
            TreeViewNode node => node.Content as ProjectBrowserNodeContext,
            _ => null
        };

        if (context is null || !context.IsFileLike || context.Kind == ProjectBrowserNodeKind.MissingFile || _workbench is null)
        {
            return;
        }

        OpenFileInWorkbench(context.FullPath);
    }

    internal void OnSolutionTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedNodes.FirstOrDefault()?.Content is ProjectBrowserNodeContext selected)
        {
            _selectedTreeItem = selected;
            PopulateExplorerChrome();
            return;
        }

        _selectedTreeItem = null;
        PopulateExplorerChrome();
    }

    private void CaptureSolutionTreeState()
    {
        _expandedNodeKeys = new HashSet<string>(EnumerateNodeContexts(SolutionTree.RootNodes)
            .Where(entry => entry.node.IsExpanded && entry.context is not null)
            .Select(entry => BuildNodeKey(entry.context!)), StringComparer.OrdinalIgnoreCase);
    }

    private void RestoreSolutionTreeState()
    {
        TreeViewNode? selectedNode = null;
        foreach (var (node, context) in EnumerateNodeContexts(SolutionTree.RootNodes))
        {
            if (context is null)
            {
                continue;
            }

            var key = BuildNodeKey(context);
            if (_expandedNodeKeys.Contains(key))
            {
                node.IsExpanded = true;
            }

            if (_selectedTreeItem is not null && string.Equals(key, BuildNodeKey(_selectedTreeItem), StringComparison.OrdinalIgnoreCase))
            {
                selectedNode = node;
            }
        }

        SolutionTree.SelectedNodes.Clear();
        if (selectedNode is not null)
        {
            SolutionTree.SelectedNodes.Add(selectedNode);
        }
    }

    private static IEnumerable<(TreeViewNode node, ProjectBrowserNodeContext? context)> EnumerateNodeContexts(IList<TreeViewNode> nodes)
    {
        foreach (var node in nodes)
        {
            var context = node.Content as ProjectBrowserNodeContext;
            yield return (node, context);
            foreach (var child in EnumerateNodeContexts(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string BuildNodeKey(ProjectBrowserNodeContext context)
    {
        return $"{context.Kind}|{context.FullPath}";
    }

    // The main-menu bar is static addin content — its items never change with solution
    // state, so populate it exactly once. Re-populating on every solution open/close
    // duplicated the items because Uno's MenuBarItem.Items.Clear() does not reliably
    // empty an already-realized menu.
    private void PopulateMainMenus()
    {
        PopulateAddInMenu(FileMenu, "/UnoDevelop/MainMenu/File");
        PopulateAddInMenu(EditMenu, "/UnoDevelop/MainMenu/Edit");
        PopulateAddInMenu(ProjectMenu, "/UnoDevelop/MainMenu/Project");
        PopulateAddInMenu(BuildMenu, "/UnoDevelop/MainMenu/Build");
        PopulateAddInMenu(DebugMenu, "/UnoDevelop/MainMenu/Debug");
        PopulateAddInMenu(SearchMenu, "/UnoDevelop/MainMenu/Search");
        PopulateAddInMenu(ToolsMenu, "/UnoDevelop/MainMenu/Tools");
        PopulateAddInMenu(WindowMenu, "/UnoDevelop/MainMenu/Window");
        PopulateAddInMenu(HelpMenu, "/UnoDevelop/MainMenu/Help");

        HideEmptyMenus();
    }

    private void HideEmptyMenus()
    {
        foreach (var menu in new[] { FileMenu, EditMenu, ViewMenu, ProjectMenu, BuildMenu,
            DebugMenu, SearchMenu, ToolsMenu, WindowMenu, HelpMenu })
        {
            menu.Visibility = menu.Items.Count > 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    // Refreshes only the solution-dependent chrome (the Solution Explorer toolbar).
    // The static main menus are populated once in the constructor via PopulateMainMenus.
    private void UpdateShellChrome()
    {
        PopulateExplorerChrome();
    }

    private void PopulateExplorerChrome()
    {
        var owner = GetExplorerAddInOwner();
        if (_solutionExplorerPad is null)
        {
            return;
        }

        var toolbarBuilder = ServiceSingleton.ServiceProvider.GetService(typeof(IUnoAddInToolbarBuilder))
            as IUnoAddInToolbarBuilder;
        toolbarBuilder?.PopulateToolbar(SolutionExplorerToolbar, owner, "/SharpDevelop/Pads/ProjectBrowser/ToolBar/Standard");
    }

    private object GetExplorerAddInOwner()
    {
        if (_selectedTreeItem is not null)
        {
            return _selectedTreeItem;
        }

        var state = (_projectService?.CurrentSolution?.Projects?.Count ?? 0) > 0
            ? ProjectBrowserNodeState.SolutionOpen
            : ProjectBrowserNodeState.None;
        return new ProjectBrowserPadContext(state);
    }

    internal void OnSolutionTreeRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var node = TryResolveNodeContext(e.OriginalSource);
        if (node is not null)
        {
            _selectedTreeItem = node;
        }

        if (_selectedTreeItem is null || _contextMenuBuilder is null)
        {
            return;
        }

        var menu = _contextMenuBuilder.CreateContextMenu(_selectedTreeItem, _selectedTreeItem.ContextMenuPath);
        menu.ShowAt(SolutionTree, new FlyoutShowOptions { Position = e.GetPosition(SolutionTree) });
        e.Handled = true;
    }

    internal void OnSolutionTreeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_selectedTreeItem is null)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            _explorerController?.Open(_selectedTreeItem);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.F2)
        {
            _explorerController?.Rename(_selectedTreeItem);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Delete)
        {
            _explorerController?.Delete(_selectedTreeItem);
            e.Handled = true;
        }
    }

    private static ProjectBrowserNodeContext? TryResolveNodeContext(object originalSource)
    {
        for (var current = originalSource as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem item
                && (item.DataContext as TreeViewNode)?.Content is ProjectBrowserNodeContext context)
            {
                return context;
            }
        }

        return null;
    }

    ProjectBrowserNodeContext? IProjectBrowserHost.SelectedNode => _selectedTreeItem;

    void IProjectBrowserHost.RefreshSolutionTree() => _ = RefreshSolutionTreeAsync();

    void IProjectBrowserHost.OpenFileInWorkbench(string filePath) => OpenFileInWorkbench(filePath);

    string? IProjectBrowserHost.ShowInputBox(string title, string prompt, string defaultValue)
        => ServiceSingleton.GetRequiredService<IMessageService>().ShowInputBox(title, prompt, defaultValue);

    bool IProjectBrowserHost.ConfirmDelete(string name)
        => ServiceSingleton.GetRequiredService<IMessageService>()
            .AskQuestion($"Delete '{name}'? This cannot be undone.", "UnoDevelop") == true;

    void IProjectBrowserHost.CloseViewsForPath(string path)
    {
        var toClose = _openFileViews
            .Where(kv => kv.Key.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                && (kv.Key.Length == path.Length
                    || kv.Key[path.Length] == Path.DirectorySeparatorChar
                    || kv.Key[path.Length] == Path.AltDirectorySeparatorChar))
            .Select(kv => kv.Value)
            .ToList();

        foreach (var view in toClose)
        {
            view.WorkbenchWindow?.CloseWindow(force: true);
        }
    }

    void IProjectBrowserHost.RetargetViewForRename(string oldPath, string newPath)
    {
        if (!_openFileViews.TryGetValue(oldPath, out var view) || view is not EditorViewContent editorView)
        {
            return;
        }

        _openFileViews.Remove(oldPath);
        editorView.Retarget(newPath);
        _openFileViews[newPath] = editorView;

        if (_documents.TryGetValue(editorView, out var document))
        {
            document.Title = editorView.IsDirty ? editorView.TabPageText + "*" : editorView.TabPageText;
        }

        _fileService?.NotifyFileRenamed(oldPath, newPath);
    }
}
