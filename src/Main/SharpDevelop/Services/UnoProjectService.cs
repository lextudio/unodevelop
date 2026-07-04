using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Refactoring;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Services;

internal interface IUnoSolutionExplorerService
{
    string CreateFolder(string targetDirectory, string baseName = "NewFolder");
    string CreateFile(string targetDirectory, string baseName = "NewFile", string extension = ".cs", string? initialContent = "// New file\n");
    IReadOnlyList<string> ImportExistingFiles(string targetDirectory, IEnumerable<string> sourcePaths);
    string ImportExistingFolder(string targetDirectory, string sourceDirectory);
    string RenameItem(string sourcePath, bool isDirectory, string newName);
    void DeleteItem(string sourcePath, bool isDirectory);
    bool TryIncludeItemInProject(string itemPath, out string includedItemName);
    bool TryExcludeItemFromProject(string itemPath, bool isDirectory, out string excludedItemName);
    bool TryRemoveItemFromProject(string itemPath, bool isDirectory, out string removedItemName, string? projectPathHint = null, string? includeHint = null);
    bool TryRemoveReference(string? projectPathHint, string include, SolutionExplorerNodeKind kind, out string removedName);
    bool TryRemoveProject(string projectPath, out string removedProjectName);
    bool TrySetStartupProject(string projectPath, out IProject? project);
}

internal sealed class UnoProjectService : IProjectService, IProjectServiceRaiseEvents
    , IUnoSolutionExplorerService
{
    internal sealed record ProjectDisplayItem(string PhysicalPath, string DisplayPath, string? DependentUpon = null, bool IsLinked = false, bool Exists = true, ProjectItem? ProjectItem = null);

    private readonly SynchronizedModelCollection<IProject> _allProjects = new(new SimpleModelCollection<IProject>());
    private readonly IReadOnlyList<ProjectBindingDescriptor> _projectBindings;
    private ISolution _currentSolution;
    private IProject _currentProject;

    public ISolution CurrentSolution => _currentSolution;

    public event PropertyChangedEventHandler<ISolution>? CurrentSolutionChanged;

    public event EventHandler<SolutionEventArgs>? SolutionOpened;

    public event EventHandler<SolutionClosingEventArgs>? SolutionClosing;

    public event EventHandler<SolutionEventArgs>? SolutionClosed;

    public IProject CurrentProject
    {
        get => _currentProject;
        set
        {
            if (ReferenceEquals(_currentProject, value))
            {
                return;
            }

            var old = _currentProject;
            _currentProject = value;
            CurrentProjectChanged?.Invoke(this, new PropertyChangedEventArgs<IProject>(old, _currentProject));
        }
    }

    public event PropertyChangedEventHandler<IProject>? CurrentProjectChanged;

    public IModelCollection<IProject> AllProjects => _allProjects;

    public event EventHandler<ProjectEventArgs>? ProjectCreated;

    public event EventHandler<SolutionEventArgs>? SolutionCreated;

    public event EventHandler<ProjectItemEventArgs>? ProjectItemAdded;

    public event EventHandler<ProjectItemEventArgs>? ProjectItemRemoved;

    public IReadOnlyList<TargetFramework> TargetFrameworks { get; } = Array.Empty<TargetFramework>();

    public IReadOnlyList<ProjectBindingDescriptor> ProjectBindings => _projectBindings;

    public UnoProjectService()
    {
        _projectBindings = new[]
        {
            new ProjectBindingDescriptor(
                new UnoCSharpProjectBinding(this),
                language: "C#",
                projectFileExtension: ".csproj",
                typeGuid: Guid.Empty,
                codeFileExtensions: new[] { ".cs" })
        };

        _currentSolution = new UnoSolutionModel("", "Untitled", _allProjects);
        _currentProject = null!;
    }

    public IProject FindProjectContainingFile(FileName fileName)
    {
        var targetPath = fileName.ToString();
        foreach (var project in _allProjects)
        {
            if (project is UnoProjectModel model && targetPath.StartsWith(model.ProjectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return project;
            }
        }

        return null!;
    }

    private static void Dbg(string msg)
    {
        try { File.AppendAllText("/tmp/unodevelop-debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] UnoProjectService: {msg}\n"); } catch { }
    }

    public bool OpenSolutionOrProject(FileName fileName)
    {
        var path = fileName.ToString();
        Dbg($"OpenSolutionOrProject: path={path}, exists={File.Exists(path)}");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Dbg("FAIL: path empty or file not found");
            return false;
        }

        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            Dbg("loading as .csproj");
            try
            {
                var solution = UnoSolutionModel.FromProject(path, _allProjects);
                Dbg($"FromProject OK, projects={solution.Projects.Count}");
                return OpenSolution(solution);
            }
            catch (Exception ex)
            {
                Dbg($"EXCEPTION in FromProject: {ex}");
                return false;
            }
        }

        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            Dbg("loading as .sln");
            var solution = UnoSolutionModel.FromSln(path, _allProjects);
            return OpenSolution(solution);
        }

        Dbg("loading via LoadSolutionFile");
        var loaded = LoadSolutionFile(fileName, new DummyProgressMonitor());
        return OpenSolution(loaded);
    }

    public bool OpenSolution(FileName fileName)
    {
        return OpenSolutionOrProject(fileName);
    }

    public bool OpenSolution(ISolution solution)
    {
        Dbg($"OpenSolution START: solution={solution.Name}, project count={solution.Projects.Count}");
        var projectSnapshot = solution.Projects.CreateSnapshot();
        var old = _currentSolution;
        _currentSolution = solution;

        _allProjects.Clear();
        _allProjects.AddRange(projectSnapshot);

        _currentProject = _allProjects.FirstOrDefault()!;
        foreach (var project in _allProjects)
        {
            Dbg($"OpenSolution project: {project.Name}, fileName={project.FileName}");
            project.ProjectLoaded();
            if (SD.BookmarkManager is UnoBookmarkManager bm)
                bm.LoadFromProject(project);
        }
        if (SD.BookmarkManager is UnoBookmarkManager bm2)
            bm2.LoadFromSolution(solution);
        Dbg($"OpenSolution END: loaded {_allProjects.Count} projects");
        if (solution.FileName is { } fn && !string.IsNullOrEmpty(fn.ToString()))
        {
            var recentOpen = ServiceSingleton.ServiceProvider.GetService(typeof(IRecentOpen)) as IRecentOpen;
            recentOpen?.AddRecentProject(fn);
        }
        CurrentSolutionChanged?.Invoke(this, new PropertyChangedEventArgs<ISolution>(old, _currentSolution));
        SolutionOpened?.Invoke(this, new SolutionEventArgs(_currentSolution));
        return true;
    }

    public bool CloseSolution(bool allowCancel = true)
    {
        Dbg($"CloseSolution START: allowCancel={allowCancel}, currentSolution is null? {_currentSolution is null}");
        if (_currentSolution is null)
        {
            Dbg("CloseSolution: no current solution, returning early");
            return true;
        }

        var closing = new SolutionClosingEventArgs(_currentSolution, allowCancel);
        SolutionClosing?.Invoke(this, closing);
        if (allowCancel && closing.Cancel)
        {
            Dbg("CloseSolution: cancelled by handler");
            return false;
        }

        var closedSolution = _currentSolution;
        Dbg($"CloseSolution: saving {_allProjects.Count} projects");
        foreach (var project in _allProjects)
        {
            Dbg($"CloseSolution saving project: {project.Name}");
            if (SD.BookmarkManager is UnoBookmarkManager bm)
            {
                Dbg($"CloseSolution: calling SaveToProject for {project.Name}");
                bm.SaveToProject(project);
            }
            else
            {
                Dbg($"CloseSolution: SD.BookmarkManager is NOT UnoBookmarkManager (type={SD.BookmarkManager?.GetType().Name ?? "null"})");
            }
            project.SavePreferences();
        }
        Dbg("CloseSolution: saving solution-wide bookmarks");
        if (SD.BookmarkManager is UnoBookmarkManager bmSolution)
            bmSolution.SaveToSolution(closedSolution);
        Dbg("CloseSolution: saving solution preferences");
        closedSolution.SavePreferences();

        var old = _currentSolution;
        _allProjects.Clear();
        _currentProject = null!;
        _currentSolution = new UnoSolutionModel("", "Untitled", _allProjects);
        Dbg("CloseSolution: solution replaced with empty model");
        CurrentSolutionChanged?.Invoke(this, new PropertyChangedEventArgs<ISolution>(old, _currentSolution));
        SolutionClosed?.Invoke(this, new SolutionEventArgs(closedSolution));
        Dbg("CloseSolution END");
        return true;
    }

    public bool IsSolutionOrProjectFile(FileName fileName)
    {
        var extension = Path.GetExtension(fileName.ToString());
        return string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || _projectBindings.Any(binding => string.Equals(binding.ProjectFileExtension, extension, StringComparison.OrdinalIgnoreCase));
    }

    public ISolution LoadSolutionFile(FileName fileName, IProgressMonitor progress)
    {
        if (fileName.ToString().EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return UnoSolutionModel.FromSln(fileName.ToString(), _allProjects);
        }

        return UnoSolutionModel.FromSlnx(fileName.ToString(), _allProjects);
    }

    public ISolution CreateEmptySolutionFile(FileName fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName.ToString());
        var solution = new UnoSolutionModel(fileName.ToString(), name, _allProjects);
        SolutionCreated?.Invoke(this, new SolutionEventArgs(solution));
        return solution;
    }

    public IProject LoadProject(ProjectLoadInformation info)
    {
        var project = new UnoProjectModel(info);
        ProjectCreated?.Invoke(this, new ProjectEventArgs(project));
        return project;
    }

    public void RaiseProjectCreated(ProjectEventArgs e)
    {
        ProjectCreated?.Invoke(this, e);
    }

    public void RaiseSolutionCreated(SolutionEventArgs e)
    {
        SolutionCreated?.Invoke(this, e);
    }

    public void RaiseProjectItemAdded(ProjectItemEventArgs e)
    {
        ProjectItemAdded?.Invoke(this, e);
    }

    public void RaiseProjectItemRemoved(ProjectItemEventArgs e)
    {
        ProjectItemRemoved?.Invoke(this, e);
    }

    public string CreateFolder(string targetDirectory, string baseName = "NewFolder")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var folderName = CreateUniqueName(targetDirectory, baseName, string.Empty, createDirectory: true);
        var folderPath = Path.Combine(targetDirectory, folderName);
        Directory.CreateDirectory(folderPath);
        RaiseProjectItemChange(folderPath, isAdded: true);
        return folderPath;
    }

    public string CreateFile(string targetDirectory, string baseName = "NewFile", string extension = ".cs", string? initialContent = "// New file\n")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var fileName = CreateUniqueName(targetDirectory, baseName, extension, createDirectory: false);
        var filePath = Path.Combine(targetDirectory, fileName);
        File.WriteAllText(filePath, initialContent ?? string.Empty);
        RaiseProjectItemChange(filePath, isAdded: true);
        return filePath;
    }

    public IReadOnlyList<string> ImportExistingFiles(string targetDirectory, IEnumerable<string> sourcePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var project = ResolveProjectForPath(targetDirectory) as UnoProjectModel
            ?? throw new InvalidOperationException("Cannot resolve the target project for the selected directory.");
        var imported = new List<string>();
        foreach (var rawPath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var sourcePath = Path.GetFullPath(rawPath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The selected file does not exist.", sourcePath);
            }

            var targetPath = AddExistingFileToProject(project.ProjectFilePath, project.ProjectDirectory, targetDirectory, sourcePath);

            RaiseProjectItemChange(targetPath, isAdded: true);
            imported.Add(targetPath);
        }

        return imported;
    }

    public string ImportExistingFolder(string targetDirectory, string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        var project = ResolveProjectForPath(targetDirectory) as UnoProjectModel
            ?? throw new InvalidOperationException("Cannot resolve the target project for the selected directory.");
        var sourcePath = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException("The selected folder does not exist: " + sourcePath);
        }

        string? firstImportedPath = null;
        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                     .Where(IsSupportedProjectItemPath)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativeChildPath = Path.GetRelativePath(sourcePath, filePath);
            var childTargetDirectory = Path.Combine(targetDirectory, Path.GetFileName(sourcePath), Path.GetDirectoryName(relativeChildPath) ?? string.Empty);
            var importedPath = AddExistingFileToProject(project.ProjectFilePath, project.ProjectDirectory, childTargetDirectory, filePath);
            firstImportedPath ??= importedPath;
            RaiseProjectItemChange(importedPath, isAdded: true);
        }

        return firstImportedPath ?? Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
    }

    public string RenameItem(string sourcePath, bool isDirectory, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var parentDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException("Cannot resolve the parent directory for the selected item.");
        }

        var targetPath = Path.Combine(parentDirectory, newName);
        if (ResolveProjectForPath(sourcePath) is UnoProjectModel project)
        {
            UpdateProjectEntriesForRename(project.ProjectFilePath, project.ProjectDirectory, sourcePath, targetPath, isDirectory);
        }

        if (isDirectory)
        {
            Directory.Move(sourcePath, targetPath);
        }
        else
        {
            File.Move(sourcePath, targetPath, overwrite: false);
        }

        RaiseProjectItemChange(sourcePath, isAdded: false);
        RaiseProjectItemChange(targetPath, isAdded: true);
        return targetPath;
    }

    public void DeleteItem(string sourcePath, bool isDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (isDirectory)
        {
            Directory.Delete(sourcePath, recursive: true);
        }
        else
        {
            File.Delete(sourcePath);
        }

        RaiseProjectItemChange(sourcePath, isAdded: false);
    }

    public bool TryRemoveItemFromProject(string itemPath, bool isDirectory, out string removedItemName, string? projectPathHint = null, string? includeHint = null)
    {
        removedItemName = Path.GetFileName(itemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return TryRemoveItemFromProjectCore(itemPath, isDirectory, projectPathHint, includeHint);
    }

    public bool TryExcludeItemFromProject(string itemPath, bool isDirectory, out string excludedItemName)
    {
        excludedItemName = Path.GetFileName(itemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return TryRemoveItemFromProjectCore(itemPath, isDirectory);
    }

    public bool TryIncludeItemInProject(string itemPath, out string includedItemName)
    {
        includedItemName = Path.GetFileName(itemPath);
        if (ResolveProjectForPath(itemPath) is not UnoProjectModel project)
        {
            return false;
        }

        if (!File.Exists(project.ProjectFilePath) || !File.Exists(itemPath))
        {
            return false;
        }

        if (!IncludeFileInProject(project.ProjectFilePath, project.ProjectDirectory, itemPath))
        {
            return false;
        }

        RaiseProjectItemChange(itemPath, isAdded: true);
        return true;
    }

    private bool TryRemoveItemFromProjectCore(string itemPath, bool isDirectory, string? projectPathHint = null, string? includeHint = null)
    {
        if (ResolveProjectForItemRemoval(itemPath, projectPathHint) is not UnoProjectModel project)
        {
            return false;
        }

        if (!File.Exists(project.ProjectFilePath))
        {
            return false;
        }

        // Prefer removing through the bound upstream ProjectItem when we have one and it belongs
        // to this loaded project: this correctly handles linked files, dependent files, and other
        // metadata-bearing items instead of guessing at the raw project XML. Fall back to the XML
        // path manipulation for fallback (non-loaded) projects or directories.
        var changed = TryRemoveBoundProjectItem(project, FindBoundProjectItemForRemoval(project, itemPath, includeHint))
            || (isDirectory
                ? RemoveDirectoryFromProject(project.ProjectFilePath, project.ProjectDirectory, itemPath)
                : RemoveFileFromProject(project.ProjectFilePath, project.ProjectDirectory, itemPath));
        if (!changed)
        {
            return false;
        }

        RaiseProjectItemChange(itemPath, isAdded: false);
        return true;
    }

    private IProject? ResolveProjectForItemRemoval(string itemPath, string? projectPathHint)
    {
        if (!string.IsNullOrWhiteSpace(projectPathHint))
        {
            var normalizedHint = Path.GetFullPath(projectPathHint);
            var hinted = _currentSolution.Projects
                .CreateSnapshot()
                .FirstOrDefault(project =>
                    string.Equals(GetSolutionItemPath(project), normalizedHint, StringComparison.OrdinalIgnoreCase));
            if (hinted is not null)
            {
                return hinted;
            }
        }

        return ResolveProjectForPath(itemPath);
    }

    private static ProjectItem? FindBoundProjectItemForRemoval(UnoProjectModel project, string itemPath, string? includeHint)
    {
        var normalizedPath = Path.GetFullPath(itemPath);
        foreach (var item in project.Items.CreateSnapshot())
        {
            var includeMatches = !string.IsNullOrWhiteSpace(includeHint)
                && string.Equals(item.Include, includeHint, StringComparison.OrdinalIgnoreCase);
            if (includeMatches)
            {
                return item;
            }

            if (item is FileProjectItem fileItem
                && string.Equals(Path.GetFullPath(fileItem.FileName.ToString()), normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static bool TryRemoveBoundProjectItem(UnoProjectModel project, ICSharpCode.SharpDevelop.Project.ProjectItem? boundItem)
    {
        if (boundItem is null || !ReferenceEquals(boundItem.Project, project))
        {
            return false;
        }

        if (!project.Items.Remove(boundItem))
        {
            return false;
        }

        project.Save();
        return true;
    }

    public bool TryRemoveReference(string? projectPathHint, string include, SolutionExplorerNodeKind kind, out string removedName)
    {
        removedName = include;
        if (string.IsNullOrWhiteSpace(include))
        {
            return false;
        }

        if (ResolveProjectForItemRemoval(itemPath: string.Empty, projectPathHint) is not UnoProjectModel project)
        {
            return false;
        }

        var itemTypeName = kind switch
        {
            SolutionExplorerNodeKind.Reference => "Reference",
            SolutionExplorerNodeKind.ProjectReference => "ProjectReference",
            SolutionExplorerNodeKind.PackageReference => "PackageReference",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(itemTypeName))
        {
            return false;
        }

        var boundItem = project.Items.CreateSnapshot()
            .FirstOrDefault(item =>
                string.Equals(item.ItemType.ItemName, itemTypeName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Include, include, StringComparison.OrdinalIgnoreCase));
        if (TryRemoveBoundProjectItem(project, boundItem))
        {
            RaiseProjectItemChange(project.ProjectFilePath, isAdded: false);
            return true;
        }

        if (!File.Exists(project.ProjectFilePath))
        {
            return false;
        }

        if (!TryRemoveReferenceFromProjectXml(project.ProjectFilePath, itemTypeName, include))
        {
            return false;
        }

        RaiseProjectItemChange(project.ProjectFilePath, isAdded: false);
        return true;
    }

    private static bool TryRemoveReferenceFromProjectXml(string projectPath, string itemTypeName, string include)
    {
        var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectElement = doc.Root;
        if (projectElement is null)
        {
            return false;
        }

        var element = projectElement.Descendants()
            .Where(IsProjectItemElement)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name.LocalName, itemTypeName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Attribute("Include")?.Value, include, StringComparison.OrdinalIgnoreCase));
        if (element is null)
        {
            return false;
        }

        element.Remove();
        SaveProjectDocument(projectPath, doc);
        return true;
    }

    public bool TryRemoveProject(string projectPath, out string removedProjectName)
    {
        removedProjectName = Path.GetFileNameWithoutExtension(projectPath);
        if (_currentSolution is not UnoSolutionModel solutionModel)
        {
            return false;
        }

        if (!solutionModel.TryRemoveProject(projectPath, out var removedProject))
        {
            return false;
        }

        PersistSolutionProjectRemoval(solutionModel, projectPath);
        if (ReferenceEquals(_currentProject, removedProject))
        {
            _currentProject = _allProjects.FirstOrDefault()!;
        }

        CurrentSolutionChanged?.Invoke(this, new PropertyChangedEventArgs<ISolution>(_currentSolution, _currentSolution));
        return true;
    }

    public bool TrySetStartupProject(string projectPath, out IProject? project)
    {
        project = _currentSolution.Projects
            .CreateSnapshot()
            .FirstOrDefault(candidate => string.Equals(GetSolutionItemPath(candidate), projectPath, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            return false;
        }

        _currentSolution.StartupProject = project;
        return true;
    }

    private void RaiseProjectItemChange(string path, bool isAdded)
    {
        var project = ResolveProjectForPath(path);
        if (project is null)
        {
            return;
        }

        var itemType = GetProjectItemType(path);
        var include = Path.GetRelativePath(project.Directory, path).Replace(Path.DirectorySeparatorChar, '\\');
        var args = new ProjectItemEventArgs(project, new FileProjectItem(project, itemType, include));
        if (isAdded)
        {
            RaiseProjectItemAdded(args);
        }
        else
        {
            RaiseProjectItemRemoved(args);
        }
    }

    private IProject? ResolveProjectForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _currentSolution is null)
        {
            return null;
        }

        return _currentSolution.Projects
            .CreateSnapshot()
            .FirstOrDefault(project =>
            {
                var projectPath = GetSolutionItemPath(project);
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    return false;
                }

                var projectDirectory = Path.GetDirectoryName(projectPath);
                return !string.IsNullOrWhiteSpace(projectDirectory)
                    && path.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase);
            });
    }

    internal static string? GetSolutionItemPath(ISolutionItem item)
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
        var asText = fileNameValue?.ToString();
        return string.IsNullOrWhiteSpace(asText) ? null : asText;
    }

    private static string CreateUniqueName(string directory, string baseName, string extension, bool createDirectory)
    {
        for (var i = 0; i < 1000; i++)
        {
            var suffix = i == 0 ? string.Empty : i.ToString();
            var name = baseName + suffix + extension;
            var candidate = Path.Combine(directory, name);
            var exists = createDirectory ? Directory.Exists(candidate) : File.Exists(candidate);
            if (!exists)
            {
                return name;
            }
        }

        return baseName + Guid.NewGuid().ToString("N")[..6] + extension;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            var childTarget = Path.Combine(targetDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, childTarget);
        }
    }

    private static void PersistSolutionProjectRemoval(UnoSolutionModel solutionModel, string projectPath)
    {
        var solutionPath = solutionModel.FileName.ToString();
        if (!File.Exists(solutionPath)
            || !string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rootDir = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        var doc = XDocument.Load(solutionPath, LoadOptions.PreserveWhitespace);
        var projectElements = doc.Descendants("Project")
            .Where(element =>
            {
                var path = element.Attribute("Path")?.Value;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                var resolved = Path.GetFullPath(Path.Combine(rootDir, path));
                return string.Equals(resolved, projectPath, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (projectElements.Count == 0)
        {
            return;
        }

        foreach (var element in projectElements)
        {
            element.Remove();
        }

        using var writer = XmlWriter.Create(solutionPath, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = doc.Declaration is null
        });
        doc.Save(writer);
    }

    internal static IReadOnlyList<ProjectDisplayItem> GetProjectDisplayItems(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            return Array.Empty<ProjectDisplayItem>();
        }

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return Array.Empty<ProjectDisplayItem>();
        }

        var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectElement = doc.Root;
        if (projectElement is null)
        {
            return Array.Empty<ProjectDisplayItem>();
        }

        var included = new Dictionary<string, ProjectDisplayItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateDefaultProjectFiles(projectDirectory))
        {
            included[candidate] = new ProjectDisplayItem(candidate, NormalizeDisplayPath(Path.GetRelativePath(projectDirectory, candidate)), Exists: true);
        }

        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemElement in projectElement.Descendants().Where(IsProjectItemElement))
        {
            ApplyProjectItemMutation(projectDirectory, itemElement, included, removed);
        }

        foreach (var removedPath in removed)
        {
            included.Remove(removedPath);
        }

        return included.Values
            .Where(item => IsSupportedProjectItemPath(item.PhysicalPath))
            .OrderBy(item => item.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<ProjectDisplayItem> GetProjectDisplayItems(IProject project)
    {
        if (project is null)
        {
            return Array.Empty<ProjectDisplayItem>();
        }

        // UnoProjectModel.CreateProjectItem wraps *every* evaluated MSBuild item as a FileProjectItem,
        // so references/packages also surface here. They are rendered separately under the References
        // and Packages folders, so exclude them from the file tree — otherwise a ProjectReference to an
        // out-of-tree .csproj shows up as a linked file and a "<Reference Include='System.Xml'/>"
        // (extension ".Xml") is mistaken for a missing .xml file.
        return project.Items.CreateSnapshot()
            .OfType<FileProjectItem>()
            .Where(item => !IsReferenceItemName(item.ItemType.ItemName))
            .Where(item => IsSupportedProjectItemPath(item.FileName.ToString()))
            .Select(item => new ProjectDisplayItem(
                item.FileName.ToString(),
                NormalizeDisplayPath(item.VirtualName),
                item.DependentUpon,
                item.IsLink,
                File.Exists(item.FileName.ToString()),
                item))
            .OrderBy(item => item.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool RemoveFileFromProject(string projectPath, string projectDirectory, string itemPath)
    {
        var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectElement = doc.Root;
        if (projectElement is null)
        {
            return false;
        }

        var normalizedItemPath = Path.GetFullPath(itemPath);
        var updated = RemoveMatchingIncludes(
            projectDirectory,
            normalizedItemPath,
            itemElement => itemElement.Attribute("Include") is not null || itemElement.Attribute("Update") is not null,
            projectElement);

        updated |= EnsureProjectRemoveEntry(projectElement, projectDirectory, normalizedItemPath, isDirectory: false);
        if (!updated)
        {
            return false;
        }

        SaveProjectDocument(projectPath, doc);
        return true;
    }

    private static bool RemoveDirectoryFromProject(string projectPath, string projectDirectory, string directoryPath)
    {
        var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectElement = doc.Root;
        if (projectElement is null)
        {
            return false;
        }

        var normalizedDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var updated = RemoveMatchingIncludes(
            projectDirectory,
            normalizedDirectoryPath,
            itemElement => itemElement.Attribute("Include") is not null || itemElement.Attribute("Update") is not null,
            projectElement,
            matchDirectoryPrefix: true);

        updated |= EnsureProjectRemoveEntry(projectElement, projectDirectory, normalizedDirectoryPath, isDirectory: true);
        if (!updated)
        {
            return false;
        }

        SaveProjectDocument(projectPath, doc);
        return true;
    }

    private static bool IncludeFileInProject(string projectPath, string projectDirectory, string itemPath)
    {
        var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectElement = doc.Root;
        if (projectElement is null)
        {
            return false;
        }

        var normalizedItemPath = Path.GetFullPath(itemPath);
        var itemType = GetProjectItemType(normalizedItemPath);
        var updated = RemoveMatchingRemoves(projectDirectory, normalizedItemPath, projectElement);
        if (!updated && HasMatchingProjectItem(projectDirectory, normalizedItemPath, itemType, projectElement))
        {
            return false;
        }

        if (!updated)
        {
            updated = AddProjectIncludeEntry(projectElement, projectDirectory, normalizedItemPath, itemType);
        }

        if (!updated)
        {
            return false;
        }

        SaveProjectDocument(projectPath, doc);
        return true;
    }

    private static bool RemoveMatchingIncludes(string projectDirectory, string targetPath, Func<XElement, bool> predicate, XElement projectElement, bool matchDirectoryPrefix = false)
    {
        var updated = false;
        var candidates = projectElement.Descendants().Where(IsProjectItemElement).Where(predicate).ToList();
        foreach (var itemElement in candidates)
        {
            foreach (var attributeName in new[] { "Include", "Update" })
            {
                var attribute = itemElement.Attribute(attributeName);
                if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value))
                {
                    continue;
                }

                var resolvedPath = ResolveProjectItemPath(projectDirectory, attribute.Value);
                var matches = matchDirectoryPrefix
                    ? resolvedPath.StartsWith(targetPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(resolvedPath, targetPath, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(resolvedPath, targetPath, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }

                itemElement.Remove();
                updated = true;
                break;
            }
        }

        return updated;
    }

    private static bool RemoveMatchingRemoves(string projectDirectory, string targetPath, XElement projectElement)
    {
        var updated = false;
        var candidates = projectElement.Descendants()
            .Where(IsProjectItemElement)
            .Where(itemElement => itemElement.Attribute("Remove") is not null)
            .ToList();
        foreach (var itemElement in candidates)
        {
            var attribute = itemElement.Attribute("Remove");
            if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value))
            {
                continue;
            }

            foreach (var removedPath in ExpandProjectItemSpec(projectDirectory, attribute.Value))
            {
                if (!string.Equals(removedPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                itemElement.Remove();
                updated = true;
                break;
            }
        }

        return updated;
    }

    private static bool HasMatchingProjectItem(string projectDirectory, string targetPath, ItemType itemType, XElement projectElement)
    {
        return projectElement.Descendants()
            .Where(IsProjectItemElement)
            .Where(element => string.Equals(element.Name.LocalName, itemType.ItemName, StringComparison.OrdinalIgnoreCase))
            .Any(element =>
            {
                foreach (var attributeName in new[] { "Include", "Update" })
                {
                    var attribute = element.Attribute(attributeName);
                    if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value))
                    {
                        continue;
                    }

                    var resolvedPath = ResolveProjectItemPath(projectDirectory, attribute.Value);
                    if (string.Equals(resolvedPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            });
    }

    private static bool AddProjectIncludeEntry(XElement projectElement, string projectDirectory, string targetPath, ItemType itemType)
    {
        var includeValue = Path.GetRelativePath(projectDirectory, targetPath)
            .Replace(Path.DirectorySeparatorChar, '\\');
        if (includeValue.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        var ns = projectElement.GetDefaultNamespace();
        var itemGroup = new XElement(ns + "ItemGroup");
        itemGroup.Add(new XElement(ns + itemType.ItemName, new XAttribute("Include", includeValue)));
        projectElement.Add(itemGroup);
        return true;
    }

    private static bool EnsureProjectRemoveEntry(XElement projectElement, string projectDirectory, string targetPath, bool isDirectory)
    {
        var relativePath = Path.GetRelativePath(projectDirectory, targetPath)
            .Replace(Path.DirectorySeparatorChar, '\\');
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        var itemTypes = GetItemTypesForPath(targetPath, isDirectory);
        if (itemTypes.Count == 0)
        {
            return false;
        }

        var removeValue = isDirectory ? relativePath.TrimEnd('\\') + "\\**\\*" : relativePath;
        var existing = projectElement.Descendants()
            .FirstOrDefault(element => itemTypes.Contains(new ItemType(element.Name.LocalName))
                && string.Equals(element.Attribute("Remove")?.Value, removeValue, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return false;
        }

        var ns = projectElement.GetDefaultNamespace();
        var itemGroup = new XElement(ns + "ItemGroup");
        foreach (var itemType in itemTypes)
        {
            itemGroup.Add(new XElement(ns + itemType.ItemName, new XAttribute("Remove", removeValue)));
        }

        projectElement.Add(itemGroup);
        return true;
    }

    private static HashSet<ItemType> GetItemTypesForPath(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return new HashSet<ItemType>(new[]
            {
                ItemType.Compile,
                ItemType.None,
                ItemType.Content,
                ItemType.EmbeddedResource,
                ItemType.Page,
                ItemType.ApplicationDefinition
            });
        }

        return new HashSet<ItemType>(new[] { GetProjectItemType(path) });
    }

    // Reference-family item element / MSBuild item names that are not files and must not leak into
    // the file tree (they are rendered under References / Packages instead).
    private static bool IsReferenceItemName(string name) =>
        name is "Reference" or "ProjectReference" or "PackageReference"
            or "Analyzer" or "COMReference" or "FrameworkReference";

    private static void ApplyProjectItemMutation(string projectDirectory, XElement itemElement, IDictionary<string, ProjectDisplayItem> included, HashSet<string> removed)
    {
        if (IsReferenceItemName(itemElement.Name.LocalName))
        {
            return;
        }

        var removeValue = itemElement.Attribute("Remove")?.Value;
        if (!string.IsNullOrWhiteSpace(removeValue))
        {
            foreach (var path in ExpandProjectItemSpec(projectDirectory, removeValue))
            {
                removed.Add(path);
                included.Remove(path);
            }

            return;
        }

        var linkPath = itemElement.Element(itemElement.GetDefaultNamespace() + "Link")?.Value;
        var dependentUpon = itemElement.Element(itemElement.GetDefaultNamespace() + "DependentUpon")?.Value;
        var updateValue = itemElement.Attribute("Update")?.Value;
        if (!string.IsNullOrWhiteSpace(updateValue))
        {
            foreach (var path in ExpandProjectItemSpec(projectDirectory, updateValue, includeMissingExplicitPath: true))
            {
                included[path] = new ProjectDisplayItem(
                    path,
                    NormalizeDisplayPath(linkPath ?? Path.GetRelativePath(projectDirectory, path)),
                    dependentUpon,
                    !string.IsNullOrWhiteSpace(linkPath),
                    File.Exists(path));
                removed.Remove(path);
            }

            return;
        }

        var includeValue = itemElement.Attribute("Include")?.Value;
        if (string.IsNullOrWhiteSpace(includeValue))
        {
            return;
        }

        foreach (var path in ExpandProjectItemSpec(projectDirectory, includeValue, includeMissingExplicitPath: true))
        {
            included[path] = new ProjectDisplayItem(
                path,
                NormalizeDisplayPath(linkPath ?? Path.GetRelativePath(projectDirectory, path)),
                dependentUpon,
                !string.IsNullOrWhiteSpace(linkPath),
                File.Exists(path));
            removed.Remove(path);
        }
    }

    private static string AddExistingFileToProject(string projectPath, string projectDirectory, string targetDirectory, string sourcePath)
    {
        var normalizedSourcePath = Path.GetFullPath(sourcePath);
        var normalizedTargetDirectory = Path.GetFullPath(targetDirectory);
        var linkDisplayPath = BuildLinkDisplayPath(projectDirectory, normalizedTargetDirectory, normalizedSourcePath);
        var includeValue = Path.GetRelativePath(projectDirectory, normalizedSourcePath).Replace(Path.DirectorySeparatorChar, '\\');
        var itemType = GetProjectItemType(normalizedSourcePath);

        var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectElement = doc.Root ?? throw new InvalidOperationException("The selected project file is invalid.");
        var existing = projectElement.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, itemType.ItemName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(element.Attribute("Include")?.Value, includeValue, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            var ns = projectElement.GetDefaultNamespace();
            var itemGroup = new XElement(ns + "ItemGroup");
            var itemElement = new XElement(ns + itemType.ItemName, new XAttribute("Include", includeValue));
            if (!string.IsNullOrWhiteSpace(linkDisplayPath))
            {
                itemElement.Add(new XElement(ns + "Link", linkDisplayPath));
            }

            itemGroup.Add(itemElement);
            projectElement.Add(itemGroup);
            SaveProjectDocument(projectPath, doc);
        }

        return normalizedSourcePath;
    }

    private static string? BuildLinkDisplayPath(string projectDirectory, string targetDirectory, string sourcePath)
    {
        var sourceUnderProject = IsPathWithinDirectory(sourcePath, projectDirectory);
        var targetUnderProject = IsPathWithinDirectory(targetDirectory, projectDirectory);
        if (!targetUnderProject)
        {
            targetDirectory = projectDirectory;
        }

        var targetRelativeDirectory = Path.GetRelativePath(projectDirectory, targetDirectory);
        if (string.Equals(targetRelativeDirectory, ".", StringComparison.Ordinal))
        {
            targetRelativeDirectory = string.Empty;
        }

        if (sourceUnderProject)
        {
            var sourceRelativePath = Path.GetRelativePath(projectDirectory, sourcePath);
            var sourceFileDirectory = Path.GetDirectoryName(sourceRelativePath) ?? string.Empty;
            if (string.Equals(NormalizeDisplayPath(sourceFileDirectory), NormalizeDisplayPath(targetRelativeDirectory), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var fileName = Path.GetFileName(sourcePath);
        return NormalizeDisplayPath(Path.Combine(targetRelativeDirectory, fileName));
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDisplayPath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '\\').Replace('/', '\\');
    }

    private static void UpdateProjectEntriesForRename(string projectPath, string projectDirectory, string sourcePath, string targetPath, bool isDirectory)
    {
        if (!File.Exists(projectPath))
        {
            return;
        }

        var doc = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectElement = doc.Root;
        if (projectElement is null)
        {
            return;
        }

        var sourceFullPath = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetFullPath = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sourceRelative = NormalizeDisplayPath(Path.GetRelativePath(projectDirectory, sourceFullPath));
        var targetRelative = NormalizeDisplayPath(Path.GetRelativePath(projectDirectory, targetFullPath));
        var sourceFileName = Path.GetFileName(sourceFullPath);
        var targetFileName = Path.GetFileName(targetFullPath);
        var changed = false;

        foreach (var itemElement in projectElement.Descendants().Where(IsProjectItemElement))
        {
            changed |= RenameProjectItemAttribute(projectDirectory, itemElement, "Include", sourceFullPath, targetFullPath, isDirectory);
            changed |= RenameProjectItemAttribute(projectDirectory, itemElement, "Update", sourceFullPath, targetFullPath, isDirectory);
            changed |= RenameProjectItemAttribute(projectDirectory, itemElement, "Remove", sourceFullPath, targetFullPath, isDirectory);

            var linkElement = itemElement.Element(itemElement.GetDefaultNamespace() + "Link");
            if (linkElement is not null && !string.IsNullOrWhiteSpace(linkElement.Value))
            {
                var linkValue = NormalizeDisplayPath(linkElement.Value);
                var renamedLink = RenameDisplayPath(linkValue, sourceRelative, targetRelative, sourceFileName, targetFileName, isDirectory);
                if (!string.Equals(linkValue, renamedLink, StringComparison.OrdinalIgnoreCase))
                {
                    linkElement.Value = renamedLink;
                    changed = true;
                }
            }

            var dependentUponElement = itemElement.Element(itemElement.GetDefaultNamespace() + "DependentUpon");
            if (dependentUponElement is not null
                && !isDirectory
                && string.Equals(dependentUponElement.Value, sourceFileName, StringComparison.OrdinalIgnoreCase))
            {
                dependentUponElement.Value = targetFileName;
                changed = true;
            }
        }

        if (changed)
        {
            SaveProjectDocument(projectPath, doc);
        }
    }

    private static bool RenameProjectItemAttribute(string projectDirectory, XElement itemElement, string attributeName, string sourceFullPath, string targetFullPath, bool isDirectory)
    {
        var attribute = itemElement.Attribute(attributeName);
        if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value))
        {
            return false;
        }

        var originalValue = NormalizeDisplayPath(attribute.Value);
        if (attributeName == "Remove" && originalValue.Contains("**\\*", StringComparison.Ordinal))
        {
            var originalPrefix = originalValue[..originalValue.IndexOf("\\**\\*", StringComparison.Ordinal)];
            var sourceRelative = NormalizeDisplayPath(Path.GetRelativePath(projectDirectory, sourceFullPath));
            if (isDirectory && string.Equals(originalPrefix, sourceRelative, StringComparison.OrdinalIgnoreCase))
            {
                attribute.Value = NormalizeDisplayPath(Path.GetRelativePath(projectDirectory, targetFullPath)) + "\\**\\*";
                return true;
            }
        }

        if (originalValue.Contains('*') || originalValue.Contains('?'))
        {
            return false;
        }

        var resolvedPath = ResolveProjectItemPath(projectDirectory, originalValue);
        if (isDirectory)
        {
            if (!resolvedPath.StartsWith(sourceFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(resolvedPath, sourceFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suffix = resolvedPath.Length == sourceFullPath.Length
                ? string.Empty
                : resolvedPath[sourceFullPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            attribute.Value = NormalizeDisplayPath(Path.GetRelativePath(projectDirectory, Path.Combine(targetFullPath, suffix)));
            return true;
        }

        if (!string.Equals(resolvedPath, sourceFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        attribute.Value = NormalizeDisplayPath(Path.GetRelativePath(projectDirectory, targetFullPath));
        return true;
    }

    private static string RenameDisplayPath(string originalValue, string sourceRelative, string targetRelative, string sourceFileName, string targetFileName, bool isDirectory)
    {
        if (isDirectory)
        {
            if (string.Equals(originalValue, sourceRelative, StringComparison.OrdinalIgnoreCase))
            {
                return targetRelative;
            }

            if (originalValue.StartsWith(sourceRelative + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return targetRelative + originalValue[sourceRelative.Length..];
            }

            return originalValue;
        }

        if (string.Equals(originalValue, sourceRelative, StringComparison.OrdinalIgnoreCase))
        {
            return targetRelative;
        }

        if (string.Equals(Path.GetFileName(originalValue), sourceFileName, StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(originalValue);
            return string.IsNullOrWhiteSpace(parent)
                ? targetFileName
                : NormalizeDisplayPath(Path.Combine(parent, targetFileName));
        }

        return originalValue;
    }

    private static IEnumerable<string> ExpandProjectItemSpec(string projectDirectory, string itemSpec, bool includeMissingExplicitPath = false)
    {
        foreach (var rawSpec in itemSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalizedSpec = rawSpec.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (normalizedSpec.Contains("**", StringComparison.Ordinal) || normalizedSpec.Contains('*') || normalizedSpec.Contains('?'))
            {
                foreach (var path in ExpandWildcardSpec(projectDirectory, normalizedSpec))
                {
                    yield return path;
                }

                continue;
            }

            var resolvedPath = ResolveProjectItemPath(projectDirectory, normalizedSpec);
            if (File.Exists(resolvedPath) || includeMissingExplicitPath)
            {
                yield return resolvedPath;
            }
        }
    }

    private static IEnumerable<string> ExpandWildcardSpec(string projectDirectory, string itemSpec)
    {
        var firstWildcardIndex = itemSpec.IndexOfAny(new[] { '*', '?' });
        var prefix = firstWildcardIndex <= 0 ? string.Empty : itemSpec[..firstWildcardIndex];
        var lastSeparatorIndex = prefix.LastIndexOf(Path.DirectorySeparatorChar);
        var prefixDirectory = lastSeparatorIndex >= 0 ? prefix[..lastSeparatorIndex] : string.Empty;
        var searchRoot = string.IsNullOrWhiteSpace(prefixDirectory)
            ? projectDirectory
            : ResolveProjectItemPath(projectDirectory, prefixDirectory);
        if (!Directory.Exists(searchRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(projectDirectory, file);
            if (MatchesProjectSpec(relativePath, itemSpec))
            {
                yield return Path.GetFullPath(file);
            }
        }
    }

    private static bool MatchesProjectSpec(string relativePath, string itemSpec)
    {
        var normalizedPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var normalizedSpec = itemSpec.Replace(Path.DirectorySeparatorChar, '/');
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(normalizedSpec)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(
            normalizedPath,
            regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static IEnumerable<string> EnumerateDefaultProjectFiles(string projectDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            if (IsExcludedProjectPath(file, projectDirectory) || !IsSupportedProjectItemPath(file))
            {
                continue;
            }

            yield return Path.GetFullPath(file);
        }
    }

    private static bool IsExcludedProjectPath(string path, string projectDirectory)
    {
        var relativePath = Path.GetRelativePath(projectDirectory, path);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, ".vs", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedProjectItemPath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".props", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".targets", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".resx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectItemElement(XElement element)
    {
        return element.Parent?.Name.LocalName == "ItemGroup";
    }

    private static string ResolveProjectItemPath(string projectDirectory, string itemSpec)
    {
        return Path.GetFullPath(Path.Combine(projectDirectory, itemSpec.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
    }

    private static ItemType GetProjectItemType(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return ItemType.Compile;
        }

        if (string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase))
        {
            return ItemType.Page;
        }

        if (string.Equals(extension, ".resx", StringComparison.OrdinalIgnoreCase))
        {
            return ItemType.EmbeddedResource;
        }

        return ItemType.None;
    }

    private static void SaveProjectDocument(string projectPath, XDocument doc)
    {
        using var writer = XmlWriter.Create(projectPath, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = doc.Declaration is null
        });
        doc.Save(writer);
    }

    private sealed class UnoCSharpProjectBinding : IProjectBinding
    {
        private readonly UnoProjectService _projectService;

        public UnoCSharpProjectBinding(UnoProjectService projectService)
        {
            _projectService = projectService;
        }

        public IProject LoadProject(ProjectLoadInformation info)
        {
            return _projectService.LoadProject(info);
        }

        public IProject CreateProject(ProjectCreateInformation info)
        {
            // CreateInformation can't be passed to MSBuildBasedProject under HAS_UNO yet;
            // fall back to loading by file path if the file already exists.
            var loadInfo = new ProjectLoadInformation(info.Solution, info.FileName, info.ProjectName);
            var project = new UnoProjectModel(loadInfo);
            _projectService.ProjectCreated?.Invoke(_projectService, new ProjectEventArgs(project));
            return project;
        }

        public bool HandlingMissingProject => false;
    }

    private sealed class UnoSolutionModel : ISolution
    {
        private readonly SimpleModelCollection<ISolutionItem> _items = new();
        private readonly SynchronizedModelCollection<IProject> _projects;
        private readonly SimpleModelCollection<SolutionSection> _globalSections = new();
        private readonly Dictionary<string, UnoSolutionFolder> _foldersByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Properties _preferences = new();
        private readonly Properties _sdSettings = new();
        private readonly SimpleConfigurationOrPlatformNameCollection _configurations = new("Debug");
        private readonly SimpleConfigurationOrPlatformNameCollection _platforms = new("AnyCPU");

        private ConfigurationAndPlatform _activeConfiguration = new("Debug", "AnyCPU");

        public UnoSolutionModel(string fileName, string name, SynchronizedModelCollection<IProject> projects)
        {
            FileName = FileName.Create(fileName) ?? new FileName(Path.Combine(System.IO.Directory.GetCurrentDirectory(), name + ".slnx"));
            Name = string.IsNullOrWhiteSpace(name) ? "Solution" : name;
            Directory = DirectoryName.Create(Path.GetDirectoryName(FileName.ToString()) ?? System.IO.Directory.GetCurrentDirectory())!;
            _projects = projects;
            InitializeMSBuildEnvironment();
            MSBuildProjectCollection = new Microsoft.Build.Evaluation.ProjectCollection();
        }

        public static UnoSolutionModel FromSlnx(string solutionPath, SynchronizedModelCollection<IProject> projectStore)
        {
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            var model = new UnoSolutionModel(solutionPath, solutionName, projectStore);
            if (!File.Exists(solutionPath))
            {
                return model;
            }

            var rootDir = Path.GetDirectoryName(solutionPath) ?? System.IO.Directory.GetCurrentDirectory();
            var doc = XDocument.Load(solutionPath);
            var projectPaths = doc.Descendants("Project")
                .Select(element => element.Attribute("Path")?.Value)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(Path.Combine(rootDir, path!)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var projectPath in projectPaths)
            {
                Dbg($"FromSlnx: trying to add project path={projectPath}");
                try { model.AddProjectPath(projectPath); }
                catch (Exception ex) { Dbg($"FromSlnx: failed to add project {projectPath}: {ex.GetType().Name}: {ex.Message}"); }
            }

            Dbg($"FromSlnx: total projects loaded={model._projects.Count}");
            if (model._projects.Count > 0)
            {
                model.StartupProject = model._projects.First();
            }

            return model;
        }

        public static UnoSolutionModel FromProject(string projectPath, SynchronizedModelCollection<IProject> projectStore)
        {
            var solutionName = Path.GetFileNameWithoutExtension(projectPath);
            var syntheticSolutionPath = Path.ChangeExtension(projectPath, ".slnx");
            var model = new UnoSolutionModel(syntheticSolutionPath ?? projectPath, solutionName, projectStore);
            var project = model.AddProjectPath(projectPath);
            model.StartupProject = project;
            return model;
        }

        public static UnoSolutionModel FromSln(string solutionPath, SynchronizedModelCollection<IProject> projectStore)
        {
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            var model = new UnoSolutionModel(solutionPath, solutionName, projectStore);
            if (!File.Exists(solutionPath))
            {
                return model;
            }

            var rootDir = Path.GetDirectoryName(solutionPath) ?? System.IO.Directory.GetCurrentDirectory();
            var projectPaths = File.ReadLines(solutionPath)
                .Select(ParseProjectPathFromSlnLine)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(Path.Combine(rootDir, path!)))
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var projectPath in projectPaths)
            {
                Dbg($"FromSln: trying to add project path={projectPath}");
                try { model.AddProjectPath(projectPath); }
                catch (Exception ex) { Dbg($"FromSln: failed to add project {projectPath}: {ex.GetType().Name}: {ex.Message}"); }
            }

            Dbg($"FromSln: total projects loaded={model._projects.Count}");
            if (model._projects.Count > 0)
            {
                model.StartupProject = model._projects.First();
            }

            return model;
        }

        internal void RegisterProject(IProject project)
        {
            _projects.Add(project);
        }

        internal bool TryRemoveProject(string projectPath, out IProject? removedProject)
        {
            removedProject = _projects.CreateSnapshot()
                .FirstOrDefault(candidate =>
                {
                    var candidatePath = UnoProjectService.GetSolutionItemPath(candidate);
                    return string.Equals(candidatePath, projectPath, StringComparison.OrdinalIgnoreCase);
                });
            if (removedProject is null)
            {
                return false;
            }

            _projects.Remove(removedProject);
            RemoveItemFromFolder(removedProject.ParentFolder, removedProject);

            if (ReferenceEquals(StartupProject, removedProject))
            {
                StartupProject = _projects.FirstOrDefault()!;
            }

            return true;
        }

        private UnoProjectModel AddProjectPath(string projectPath)
        {
            var folder = EnsureProjectParentFolder(projectPath);
            var project = new UnoProjectModel(projectPath, this) { ParentFolder = folder };
            _projects.Add(project);
            if (ReferenceEquals(folder, this))
            {
                _items.Add(project);
            }
            else
            {
                folder.Items.Add(project);
            }

            return project;
        }

        private static string? ParseProjectPathFromSlnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("Project(", StringComparison.Ordinal))
            {
                return null;
            }

            var parts = line.Split('"');
            if (parts.Length < 6)
            {
                return null;
            }

            var relativePath = parts[5];
            return string.IsNullOrWhiteSpace(relativePath) ? null : relativePath;
        }

        private ISolutionFolder EnsureProjectParentFolder(string projectPath)
        {
            var solutionDirectory = Directory.ToString();
            var projectDirectory = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(solutionDirectory) || string.IsNullOrWhiteSpace(projectDirectory))
            {
                return this;
            }

            var relativeDirectory = Path.GetRelativePath(solutionDirectory, projectDirectory);
            if (string.IsNullOrWhiteSpace(relativeDirectory)
                || relativeDirectory == "."
                || relativeDirectory.StartsWith("..", StringComparison.Ordinal))
            {
                return this;
            }

            var segments = relativeDirectory
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(segment => !string.IsNullOrWhiteSpace(segment));

            ISolutionFolder current = this;
            var currentKey = string.Empty;
            foreach (var segment in segments)
            {
                currentKey = string.IsNullOrEmpty(currentKey)
                    ? segment
                    : Path.Combine(currentKey, segment);

                if (_foldersByKey.TryGetValue(currentKey, out var existingFolder))
                {
                    current = existingFolder;
                    continue;
                }

                var folder = new UnoSolutionFolder(this, segment)
                {
                    ParentFolder = current
                };
                if (ReferenceEquals(current, this))
                {
                    _items.Add(folder);
                }
                else
                {
                    current.Items.Add(folder);
                }

                _foldersByKey[currentKey] = folder;
                current = folder;
            }

            return current;
        }

        private void RemoveItemFromFolder(ISolutionFolder folder, ISolutionItem item)
        {
            if (ReferenceEquals(folder, this))
            {
                _items.Remove(item);
                return;
            }

            folder.Items.Remove(item);
        }

        public Microsoft.Build.Evaluation.ProjectCollection MSBuildProjectCollection { get; }

        public FileName FileName { get; private set; }

        public event EventHandler? FileNameChanged;

        public DirectoryName Directory { get; }

        private IProject _startupProject = null!;

        public IProject StartupProject
        {
            get => _startupProject;
            set
            {
                if (ReferenceEquals(_startupProject, value))
                {
                    return;
                }

                _startupProject = value;
                StartupProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? StartupProjectChanged;

        public IModelCollection<IProject> Projects => _projects;

        public IEnumerable<ISolutionItem> AllItems => _items.CreateSnapshot();

        public IMutableModelCollection<SolutionSection> GlobalSections => _globalSections;

        public ISolutionItem GetItemByGuid(Guid guid)
        {
            return _items.CreateSnapshot().FirstOrDefault(item => item.IdGuid == guid)!;
        }

        public Properties Preferences => _preferences;

        public Properties SDSettings => _sdSettings;

        public event EventHandler? PreferencesSaving;

        public bool IsReadOnly => false;

        public bool IsDirty => false;

        public event EventHandler? IsDirtyChanged;

        public void SavePreferences()
        {
            PreferencesSaving?.Invoke(this, EventArgs.Empty);
        }

        public void Save()
        {
        }

        public string Name { get; set; }

        public bool IsAncestorOf(ISolutionItem item)
        {
            if (item is null)
            {
                return false;
            }

            var current = item.ParentFolder;
            while (current is not null)
            {
                if (ReferenceEquals(current, this))
                {
                    return true;
                }

                current = current.ParentFolder;
            }

            return false;
        }

        public IMutableModelCollection<ISolutionItem> Items => _items;

        public IProject AddExistingProject(FileName fileName)
        {
            return AddProjectPath(fileName.ToString());
        }

        public ISolutionFileItem AddFile(FileName fileName)
        {
            var item = new UnoSolutionFileItem(this, fileName);
            _items.Add(item);
            return item;
        }

        public ISolutionFolder CreateFolder(string name)
        {
            var folder = new UnoSolutionFolder(this, name);
            _items.Add(folder);
            _foldersByKey[name] = folder;
            return folder;
        }

        public IConfigurationOrPlatformNameCollection ConfigurationNames => _configurations;

        public IConfigurationOrPlatformNameCollection PlatformNames => _platforms;

        public ConfigurationAndPlatform ActiveConfiguration
        {
            get => _activeConfiguration;
            set
            {
                _activeConfiguration = value;
                ActiveConfigurationChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? ActiveConfigurationChanged;

        public ISolutionFolder ParentFolder
        {
            get => null!;
            set
            {
            }
        }

        public ISolution ParentSolution => this;

        public Guid IdGuid { get; set; } = Guid.NewGuid();

        public Guid TypeGuid { get; } = Guid.Empty;

        public void Dispose()
        {
            MSBuildProjectCollection.Dispose();
        }

        private static bool _msbuildEnvironmentInitialized;

        private static void InitializeMSBuildEnvironment()
        {
            if (_msbuildEnvironmentInitialized) return;
            _msbuildEnvironmentInitialized = true;

            var dotnetRoot = GetDotnetRoot();
            if (dotnetRoot == null) return;

            var sdksDir = Path.Combine(dotnetRoot, "sdk");
            if (!System.IO.Directory.Exists(sdksDir)) return;

            var latestSdk = System.IO.Directory.GetDirectories(sdksDir)
                .Where(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out _))
                .OrderByDescending(d =>
                {
                    Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v);
                    return v;
                })
                .FirstOrDefault();

            if (latestSdk == null) return;

            // Point MSBuild at the .NET SDK so the NuGet SDK resolver can find
            // NuGet-distributed SDKs (e.g. Uno.Sdk) via the global package cache.
            // Do NOT set MSBUILD_EXE_PATH: it triggers an allowlist type check that
            // expects Microsoft.Build.Utilities.Core Version=15.1.0.0 and causes
            // MSB0001 InternalErrorException when the in-process version differs.
            Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(latestSdk, "Sdks"));
            Environment.SetEnvironmentVariable("MSBuildExtensionsPath", latestSdk);

            // In-process MSBuild uses Assembly.LoadFile with a hardcoded path equal to
            // its own assembly directory (our app bin dir) when loading NuGet runtime
            // helpers needed by intrinsic functions like GetTargetFrameworkIdentifier.
            // AssemblyResolve cannot intercept LoadFile calls, so we copy the files once
            // from the SDK into the bin dir where MSBuild expects to find them.
            var binDir = Path.GetDirectoryName(typeof(UnoSolutionModel).Assembly.Location)!;
            foreach (var dep in new[] { "NuGet.Frameworks.dll" })
            {
                var dst = Path.Combine(binDir, dep);
                var src = Path.Combine(latestSdk, dep);
                if (!File.Exists(dst) && File.Exists(src))
                    File.Copy(src, dst);
            }

            // In-process MSBuild (loaded from a NuGet package) looks for its runtime
            // dependencies (e.g. NuGet.Frameworks.dll) next to the MSBuild assembly,
            // which is the app bin directory. The real copies live in the .NET SDK.
            // Redirect assembly resolution so MSBuild intrinsic functions work without
            // copying DLLs or adding SharpDevelop-level dependencies.
        }

        private static string? GetDotnetRoot()
        {
            var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
                return Path.GetDirectoryName(hostPath);

            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(dotnetRoot) && System.IO.Directory.Exists(dotnetRoot))
                return dotnetRoot;

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidate = Path.Combine(programFiles, "dotnet");
            if (System.IO.Directory.Exists(candidate))
                return candidate;

            return null;
        }

    }

    internal sealed class UnoProjectModel : MSBuildBasedProject, IBuildable
    {
        // Watches this project's .csproj for edits made outside the IDE (e.g. `dotnet add package`
        // in a terminal, or hand-editing the file) and reloads the in-memory project model + posts
        // a targeted Solution Explorer refresh in response. See docs/project-system.md (Slice 50).
        private readonly UnoProjectChangeWatcher _changeWatcher;

        public UnoProjectModel(ProjectLoadInformation info)
            : base(info)
        {
            _changeWatcher = new UnoProjectChangeWatcher(FileName.ToString());
            _changeWatcher.ChangedExternally += OnChangedExternally;
        }

        public UnoProjectModel(string projectFilePath, ISolution solution)
            : this(new ProjectLoadInformation(solution, FileName.Create(projectFilePath)!, Path.GetFileNameWithoutExtension(projectFilePath)))
        {
        }

        private void OnChangedExternally(object? sender, FileRenameEventArgs e)
        {
            try
            {
                ReloadFromDisk();
            }
            catch (Exception ex)
            {
                // A reload can legitimately fail transiently (e.g. the file is mid-write by another
                // process) or fail hard (invalid XML) — either way, keep the previously-loaded state
                // rather than leaving the project half-updated, and let the user notice via a normal
                // reload/rebuild if the file is genuinely broken.
                LoggingService.Warn($"UnoProjectModel.ReloadFromDisk failed for {FileName}: {ex.Message}");
                return;
            }

            // Reuses the exact same targeted-refresh path as a normal item add/remove (slice 49) —
            // MainPage.OnProjectItemCollectionChanged already knows how to rebuild just this
            // project's Dependencies node from a ProjectItemEventArgs. ProjectItem is unused by that
            // handler for this purpose, so null is fine here.
            if (ServiceSingleton.ServiceProvider.GetService(typeof(IProjectService)) is UnoProjectService service)
            {
                service.RaiseProjectItemAdded(new ProjectItemEventArgs(this, null!));
            }
        }

        public override void Dispose()
        {
            _changeWatcher.ChangedExternally -= OnChangedExternally;
            _changeWatcher.Dispose();
            base.Dispose();
        }

        public string ProjectFilePath => FileName.ToString();

        public string ProjectDirectory => Directory.ToString();

        public string ProjectName => Name;

        public override string AssemblyName
        {
            get => GetEvaluatedProperty("AssemblyName") ?? Name;
            set => SetProperty("AssemblyName", value);
        }

        public override string RootNamespace
        {
            get => GetEvaluatedProperty("RootNamespace") ?? Name;
            set => SetProperty("RootNamespace", value);
        }

        public override FileName OutputAssemblyFullPath =>
            FileName.Create(Path.Combine(ProjectDirectory, "bin", ActiveConfiguration.Configuration, $"{AssemblyName}.dll"))!;

        public override string Language => "C#";

        public override ItemType GetDefaultItemType(string fileName) => GetProjectItemType(fileName);

        public override string GetDefaultNamespace(string fileName) => RootNamespace;

        public override ProjectItem CreateProjectItem(IProjectItemBackendStore item)
        {
            return new FileProjectItem(this, item);
        }

        public override IEnumerable<ReferenceProjectItem> ResolveAssemblyReferences(CancellationToken cancellationToken)
        {
            return Enumerable.Empty<ReferenceProjectItem>();
        }

        public new IEnumerable<IBuildable> GetBuildDependencies(ProjectBuildOptions buildOptions)
        {
            return base.GetBuildDependencies(buildOptions);
        }

        public override async Task<bool> BuildAsync(ProjectBuildOptions options, IBuildFeedbackSink feedbackSink, IProgressMonitor progressMonitor)
        {
            var targetPath = ProjectFilePath;
            var target = options?.Target ?? BuildTarget.Build;
            var verb = "build";
            if (target == BuildTarget.Rebuild)
            {
                verb = "rebuild";
            }
            else if (target == BuildTarget.Clean)
            {
                verb = "clean";
            }

            feedbackSink.ReportMessage(new ICSharpCode.AvalonEdit.Highlighting.RichText($"------ {verb} started: {Path.GetFileName(targetPath)} ------"));

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"{verb} \"{targetPath}\" --nologo",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = ProjectDirectory
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    feedbackSink.ReportError(new BuildError(targetPath, "dotnet process failed to start."));
                    return false;
                }

                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        feedbackSink.ReportMessage(new ICSharpCode.AvalonEdit.Highlighting.RichText(e.Data));
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        feedbackSink.ReportMessage(new ICSharpCode.AvalonEdit.Highlighting.RichText(e.Data));
                    }
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cancellationRegistration = progressMonitor.CancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }
                });

                await process.WaitForExitAsync(progressMonitor.CancellationToken).ConfigureAwait(false);

                if (process.ExitCode == 0)
                {
                    feedbackSink.ReportMessage(new ICSharpCode.AvalonEdit.Highlighting.RichText($"------ {verb} succeeded: {Path.GetFileName(targetPath)} ------"));
                    return true;
                }

                feedbackSink.ReportError(new BuildError(targetPath, $"dotnet {verb} exited with code {process.ExitCode}."));
                feedbackSink.ReportMessage(new ICSharpCode.AvalonEdit.Highlighting.RichText($"------ {verb} failed: {Path.GetFileName(targetPath)} ------"));
                return false;
            }
            catch (OperationCanceledException)
            {
                feedbackSink.ReportMessage(new ICSharpCode.AvalonEdit.Highlighting.RichText($"------ {verb} canceled: {Path.GetFileName(targetPath)} ------"));
                return false;
            }
            catch (Exception ex)
            {
                feedbackSink.ReportError(new BuildError(targetPath, ex.Message));
                return false;
            }
        }

        public override ProjectBuildOptions CreateProjectBuildOptions(BuildOptions options, bool isRootBuildable)
        {
            var target = isRootBuildable ? options.ProjectTarget : options.TargetForDependencies;
            var projectOptions = new ProjectBuildOptions(target)
            {
                BuildOutputVerbosity = options.BuildOutputVerbosity
            };

            if (ParentSolution is not null)
            {
                projectOptions.Configuration = options.SolutionConfiguration ?? ParentSolution.ActiveConfiguration.Configuration;
                projectOptions.Platform = options.SolutionPlatform ?? ParentSolution.ActiveConfiguration.Platform;
            }

            foreach (var pair in options.GlobalAdditionalProperties)
            {
                projectOptions.Properties[pair.Key] = pair.Value;
            }

            if (isRootBuildable)
            {
                foreach (var pair in options.ProjectAdditionalProperties)
                {
                    projectOptions.Properties[pair.Key] = pair.Value;
                }
            }

            return projectOptions;
        }
    }

    private sealed class UnoSolutionFolder : ISolutionFolder
    {
        private readonly SimpleModelCollection<ISolutionItem> _items = new();

        public UnoSolutionFolder(ISolution solution, string name)
        {
            ParentSolution = solution;
            ParentFolder = solution;
            Name = name;
        }

        public string Name { get; set; }

        public bool IsAncestorOf(ISolutionItem item)
        {
            if (item is null)
            {
                return false;
            }

            var current = item.ParentFolder;
            while (current is not null)
            {
                if (ReferenceEquals(current, this))
                {
                    return true;
                }

                current = current.ParentFolder;
            }

            return false;
        }

        public IMutableModelCollection<ISolutionItem> Items => _items;

        public IProject AddExistingProject(FileName fileName)
        {
            if (ParentSolution is UnoSolutionModel model)
            {
                var project = new UnoProjectModel(fileName.ToString(), ParentSolution)
                {
                    ParentFolder = this
                };
                model.RegisterProject(project);
                _items.Add(project);
                return project;
            }

            var fallback = new UnoProjectModel(fileName.ToString(), ParentSolution)
            {
                ParentFolder = this
            };
            _items.Add(fallback);
            return fallback;
        }

        public ISolutionFileItem AddFile(FileName fileName)
        {
            var fileItem = new UnoSolutionFileItem(ParentSolution, fileName);
            fileItem.ParentFolder = this;
            _items.Add(fileItem);
            return fileItem;
        }

        public ISolutionFolder CreateFolder(string name)
        {
            var folder = new UnoSolutionFolder(ParentSolution, name);
            folder.ParentFolder = this;
            _items.Add(folder);
            return folder;
        }

        public ISolutionFolder ParentFolder { get; set; }

        public ISolution ParentSolution { get; }

        public Guid IdGuid { get; set; } = Guid.NewGuid();

        public Guid TypeGuid { get; } = Guid.Empty;
    }

    private sealed class UnoSolutionFileItem : ISolutionFileItem
    {
        public UnoSolutionFileItem(ISolution solution, FileName fileName)
        {
            ParentSolution = solution;
            ParentFolder = solution;
            FileName = fileName;
        }

        public FileName FileName { get; set; }

        public ISolutionFolder ParentFolder { get; set; }

        public ISolution ParentSolution { get; }

        public Guid IdGuid { get; set; } = Guid.NewGuid();

        public Guid TypeGuid { get; } = Guid.Empty;
    }

    private sealed class SimpleConfigurationOrPlatformNameCollection : IConfigurationOrPlatformNameCollection
    {
        private readonly SimpleModelCollection<string> _entries = new();

        public SimpleConfigurationOrPlatformNameCollection(string initialValue)
        {
            _entries.Add(initialValue);
        }

        public event ModelCollectionChangedEventHandler<string>? CollectionChanged
        {
            add => _entries.CollectionChanged += value;
            remove => _entries.CollectionChanged -= value;
        }

        public int Count => _entries.Count;

        public string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null!;
            }

            return name.Trim();
        }

        public void Add(string newName, string copyFrom = null)
        {
            var normalized = ValidateName(newName);
            if (string.IsNullOrEmpty(normalized) || _entries.Contains(normalized))
            {
                return;
            }

            _entries.Add(normalized);
        }

        public void Remove(string name)
        {
            _entries.Remove(name);
        }

        public void Rename(string oldName, string newName)
        {
            if (!_entries.Contains(oldName))
            {
                return;
            }

            var normalized = ValidateName(newName);
            _entries.Remove(oldName);
            _entries.Add(normalized);
        }

        public bool Contains(string item)
        {
            return _entries.Contains(item);
        }

        public IReadOnlyCollection<string> CreateSnapshot()
        {
            return _entries.CreateSnapshot();
        }

        public IEnumerator<string> GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
