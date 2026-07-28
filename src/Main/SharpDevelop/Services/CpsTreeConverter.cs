// Slice 7: bridge CPS IProjectTree → TreeViewNode / ProjectBrowserNodeContext.
// See docs/project-system.md.

using System;
using System.IO;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.ProjectSystem;
using ICSharpCode.SharpDevelop.Services;

namespace UnoDevelop.Services;

/// <summary>
/// Converts a CPS <see cref="IProjectTree"/> hierarchy produced by
/// <see cref="UnoDevelopProjectTreeProvider"/> into <see cref="TreeViewNode"/>
/// items that the Solution Explorer UI already knows how to display.
/// </summary>
internal static class CpsTreeConverter
{
    /// <summary>
    /// Converts the root CPS tree node for a project into a <see cref="TreeViewNode"/>.
    /// Returns null when the node kind cannot be determined.
    /// </summary>
    public static TreeViewNode? ToTreeViewNode(IProjectTree node, IProject? project = null)
    {
        var kind = ResolveKind(node);
        if (kind == ProjectBrowserNodeKind.Unknown)
            return null;

        var projectPathHint = node.Root?.FilePath;
        var includeHint = ResolveIncludeHint(node, projectPathHint);

        var context = new ProjectBrowserNodeContext(
            Name:             node.Caption,
            FullPath:         node.FilePath ?? string.Empty,
            IsDirectory:      node.IsFolder,
            Kind:             kind,
            BoundItem:        project,
            BoundProjectTree: node,
            ProjectPathHint:  projectPathHint,
            IncludeHint:      includeHint,
            GitStatus:        GitStatusService.GetStatus(node.FilePath));

        var tvNode = new TreeViewNode
        {
            Content    = context,
            IsExpanded = node.IsRoot
                || node.Flags.Contains(ProjectTreeFlags.Common.DependenciesFolder)
                || node.Flags.Contains(ProjectTreeFlags.Common.ReferencesFolder),
        };

        foreach (var child in node.Children)
        {
            var childTv = ToTreeViewNode(child, project);
            if (childTv is not null)
                tvNode.Children.Add(childTv);
        }

        return tvNode;
    }

    private static string? ResolveIncludeHint(IProjectTree node, string? projectPathHint)
    {
        if (!string.IsNullOrWhiteSpace(node.BrowseObjectProperties?.ItemName))
        {
            return node.BrowseObjectProperties.ItemName;
        }

        if (string.IsNullOrWhiteSpace(node.FilePath) || string.IsNullOrWhiteSpace(projectPathHint))
        {
            return null;
        }

        try
        {
            var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPathHint));
            if (string.IsNullOrWhiteSpace(projectDir))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(node.FilePath);
            if (string.Equals(fullPath, Path.GetFullPath(projectPathHint), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = Path.GetRelativePath(projectDir, fullPath);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                return null;
            }

            return relative.Replace(Path.DirectorySeparatorChar, '\\');
        }
        catch
        {
            return null;
        }
    }

    // Flag -> Kind mapping is now shared (ProjectBrowserTreeKindResolver, see
    // doc/technotes/solution-explorer.md) - OpenDevelop's ProjectBrowserTreeBuilder uses the same
    // resolver.
    private static ProjectBrowserNodeKind ResolveKind(IProjectTree node) =>
        ProjectBrowserTreeKindResolver.ResolveKind(node);
}
