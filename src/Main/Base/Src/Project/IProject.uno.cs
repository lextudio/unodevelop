using System;
using System.Collections.Generic;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Dom;

namespace ICSharpCode.SharpDevelop.Project;

// Windows-first migration surface for IProject.
// This is intentionally smaller than upstream, but it now carries the
// core solution/build/configuration shape that current UnoDevelop code uses.
public interface IProject : IBuildable, ISolutionItem, IDisposable, IConfigurable
{
    object SyncRoot { get; }

    FileName FileName { get; set; }

    new string Name { get; set; }

    DirectoryName Directory { get; }

    IMutableModelCollection<ProjectItem> Items { get; }

    string RootNamespace { get; }

    IEnumerable<ProjectItem> GetItemsOfType(ItemType type);

    ItemType GetDefaultItemType(string fileName);

    IReadOnlyCollection<ItemType> AvailableFileItemTypes { get; }

    IMutableModelCollection<SolutionSection> ProjectSections { get; }

    ConfigurationMapping ConfigurationMapping { get; }

    bool IsFileInProject(FileName fileName);

    FileProjectItem? FindFile(FileName fileName);

    ProjectItem CreateProjectItem(IProjectItemBackendStore item);

    bool IsStartable { get; }

    void Save();

    void Start(bool withDebugging);
}
