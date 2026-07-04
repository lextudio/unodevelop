// Slice 7: bridge CPS IProjectTree → TreeViewNode / SolutionExplorerNodeContext.
// See docs/project-system.md.

using System;
using System.IO;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.ProjectSystem;

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
        if (kind == SolutionExplorerNodeKind.Unknown)
            return null;

        var projectPathHint = node.Root?.FilePath;
        var includeHint = ResolveIncludeHint(node, projectPathHint);

        var context = new SolutionExplorerNodeContext(
            Name:             node.Caption,
            FullPath:         node.FilePath ?? string.Empty,
            IsDirectory:      node.IsFolder,
            Kind:             kind,
            BoundItem:        project,
            BoundProjectTree: node,
            ProjectPathHint:  projectPathHint,
            IncludeHint:      includeHint);

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

    // ── Flag → Kind mapping ───────────────────────────────────────────────────

    private static SolutionExplorerNodeKind ResolveKind(IProjectTree node)
    {
        var f = node.Flags;

        if (f.Contains(ProjectTreeFlags.Common.ProjectRoot))
            return SolutionExplorerNodeKind.Project;

        if (f.Contains(ProjectTreeFlags.Common.DependenciesFolder))
            return SolutionExplorerNodeKind.DependenciesFolder;

        if (f.Contains(ProjectTreeFlags.Common.ReferencesFolder))
            return SolutionExplorerNodeKind.ReferencesFolder;

        if (f.Contains(ProjectTreeFlags.Common.PackagesFolder))
            return SolutionExplorerNodeKind.PackagesFolder;

        if (f.Contains(ProjectTreeFlags.Common.PackageReference))
            return SolutionExplorerNodeKind.PackageReference;

        if (f.Contains(ProjectTreeFlags.Common.Reference))
        {
            return f.Contains(ProjectTreeFlags.Common.ProjectReference)
                ? SolutionExplorerNodeKind.ProjectReference
                : SolutionExplorerNodeKind.Reference;
        }

        if (f.Contains(ProjectTreeFlags.Common.Folder) ||
            f.Contains(ProjectTreeFlags.Common.VirtualFolder))
            return f.Contains(ProjectTreeFlags.Common.IncludeInProjectCandidate)
                ? SolutionExplorerNodeKind.GhostFolder
                : SolutionExplorerNodeKind.Folder;

        if (f.Contains(ProjectTreeFlags.Common.SourceFile))
        {
            if (f.Contains(ProjectTreeFlags.Common.IncludeInProjectCandidate))
                return SolutionExplorerNodeKind.GhostFile;

            if (!f.Contains(ProjectTreeFlags.Common.FileSystemEntity))
                return SolutionExplorerNodeKind.MissingFile;

            if (f.Contains(ProjectTreeFlags.Common.LinkedFile))
                return SolutionExplorerNodeKind.LinkedFile;

            return SolutionExplorerNodeKind.File;
        }

        return SolutionExplorerNodeKind.Unknown;
    }
}
