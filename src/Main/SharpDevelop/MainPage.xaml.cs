using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AvalonDock.Core;
using AvalonDock.Layout;
using AvalonDock.Serializer.Xml;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using ICSharpCode.AvalonEdit.Folding;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using LeXtudio.OpenDevelop.ResourceFiles;
using UnoEdit.Skia.Desktop.Controls;
using UnoDevelop.Controls;
using UnoDevelop.Debugger;
using UnoDevelop.OptionPanels;
using UnoDevelop.Services;
using ICSharpCode.UnitTesting.Simple;
using UnoDevelop.UnitTesting;
using UnoDevelop.Workbench;

namespace UnoDevelop;

public partial class MainPage : Page, IUnoSolutionExplorerHost
{
    /// <summary>Gets the active MainPage instance, or null.</summary>
    public static MainPage? Current { get; private set; }

    private readonly UnoWorkbenchService? _workbench;
    private readonly IProjectService? _projectService;
    private readonly IUnoSolutionExplorerController? _explorerController;
    private readonly IUnoAddInContextMenuBuilder? _contextMenuBuilder;
    private readonly UnoFileService? _fileService;
    private readonly ITestService? _testService;
    private readonly LanguageServiceRegistry? _languageServiceRegistry;
    private readonly RunService _runService = new();
    private readonly DebugService _debugService = new();
    // True while the debuggee is paused (break mode); drives the "Paused" toolbar condition.
    private bool _debugPaused;
    internal DebugService DebugService => _debugService;
    private LocalsPad? _localsPad;
    private CallStackPad? _callStackPad;
    private WatchPad? _watchPad;
    private ImmediatePad? _immediatePad;
    private ThreadsPad? _threadsPad;
    private ModulesPad? _modulesPad;
    private SolutionExplorerPad? _solutionExplorerPad;
    private TestResultsPad? _testResultsPad;
    private UnoDevelop.Workbench.ErrorListPad? _errorListPad;
    private OutputPad? _outputPad;
    private PropertiesPad? _propertiesPad;
    private WpfButton? RunToolbarButton;
    private WpfButton? StopToolbarButton;
    private WpfButton? DebugToolbarButton;
    private WpfButton? ContinueToolbarButton;
    private WpfButton? StepOverToolbarButton;
    private WpfButton? StepInToolbarButton;
    private WpfButton? StepOutToolbarButton;
    private readonly Dictionary<IViewContent, LayoutDocument> _documents = new();
    private readonly Dictionary<string, IViewContent> _openFileViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LayoutAnchorable> _padWindows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _diagnosticsCancellations = new(StringComparer.OrdinalIgnoreCase);
    private CompletionWindow? _completionWindow;
    private bool _isRefreshingSolutionTree;
    private HashSet<string> _expandedNodeKeys = new(StringComparer.OrdinalIgnoreCase);
    private SolutionExplorerNodeContext? _selectedTreeItem;

    public MainPage()
    {
        Current = this;
        InitializeComponent();

        _workbench = ServiceSingleton.ServiceProvider.GetService(typeof(IWorkbench)) as UnoWorkbenchService;
        _projectService = ServiceSingleton.ServiceProvider.GetService(typeof(IProjectService)) as IProjectService;
        _explorerController = ServiceSingleton.ServiceProvider.GetService(typeof(IUnoSolutionExplorerController)) as IUnoSolutionExplorerController;
        _contextMenuBuilder = ServiceSingleton.ServiceProvider.GetService(typeof(IUnoAddInContextMenuBuilder)) as IUnoAddInContextMenuBuilder;
        _fileService = ServiceSingleton.ServiceProvider.GetService(typeof(IFileService)) as UnoFileService;
        _testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
        _languageServiceRegistry = ServiceSingleton.ServiceProvider.GetService(typeof(LanguageServiceRegistry)) as LanguageServiceRegistry;
        _explorerController?.BindHost(this);
        _workbench?.BindUiHost(OpenOrActivateView, CloseAllWorkbenchViews);
        _fileService?.BindWorkbenchFileOperations(OpenFileInWorkbench, JumpToFilePositionInWorkbench);
        (ServiceSingleton.ServiceProvider as System.ComponentModel.Design.IServiceContainer)
            ?.AddService(typeof(UnoDevelop.Debugger.IDebuggerService), _debugService);
        HookProjectServiceEvents();
        HookRunServiceEvents();
        HookWorkbenchPadEvents();
        LoadAddInPads();
        // OnPadAdded runs on an enqueued turn per pad (see HookWorkbenchPadEvents), so enqueue the
        // startup layout restore too - it lands after all of LoadAddInPads' pad-add callbacks, once
        // _padWindows is fully populated and LayoutSerializationCallback can resolve every ContentId.
        DispatcherQueue.TryEnqueue(() => UnoDevelop.Workbench.ChooseLayoutComboBox.LoadCurrentLayout());
        PopulateAddInToolbar();
        PopulateMainMenus();
        PopulateViewMenu();
        HookDebugServiceEvents();
        HookTestServiceEvents();
        UnoDevelop.Debugger.DebuggerAddin.Initialize(_debugService);
        UpdateShellChrome();
        OpenOnStartIfRequested();
    }

    private void OpenOnStartIfRequested()
    {
        // Integration-test / command-line override takes priority.
        var path = Environment.GetEnvironmentVariable("UNODEVELOP_OPEN_ON_START");
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            _projectService?.OpenSolutionOrProject(ICSharpCode.Core.FileName.Create(path)!);
            return;
        }

        // "Load previous project on startup" — mirrors SharpDevelop.LoadPrevProjectOnStartup (default false).
        if (SD.PropertyService.Get("SharpDevelop.LoadPrevProjectOnStartup", false))
        {
            var recentOpen = ServiceSingleton.ServiceProvider.GetService(typeof(IRecentOpen)) as IRecentOpen;
            if (recentOpen?.RecentProjects.Count > 0)
            {
                _projectService?.OpenSolutionOrProject(recentOpen.RecentProjects[0]);
                return;
            }
        }
    }

    private void ClearOutputPad()
    {
        _outputPad?.Clear();
    }

    private UnoOutputPadService? GetOutputPadService()
    {
        return ServiceSingleton.ServiceProvider.GetService(typeof(ICSharpCode.SharpDevelop.Workbench.IOutputPad))
            as UnoOutputPadService;
    }

    private MessageViewCategory? PrepareExecutionOutputCategory(string name)
    {
        var outputPad = GetOutputPadService();
        var category = outputPad?.GetOrCreateMessageViewCategory(name);
        if (category is null)
            return null;

        category.ClearText();
        outputPad.SelectCategory(category);
        ShowOutputPad();
        return category;
    }

    // Surface a run/debug failure both in the Output pad (so the reason is visible
    // at a glance) and in the status bar.
    private void ReportExecutionIssue(IOutputCategory? category, string message)
    {
        category?.AppendLine("ERROR: " + message);
        ShowOutputPad();
        SetExplorerStatus(message);
    }

    private void ShowOutputPad()
    {
        var pad = _workbench?.GetPad(typeof(UnoDevelop.Workbench.OutputPad));
        if (pad is not null)
            ShowPad(pad);
    }

    private void ShowTestsPad()
    {
        var pad = _workbench?.GetPad(typeof(TestResultsPad));
        if (pad is not null)
            ShowPad(pad);
    }

    private void ShowErrorsPad()
    {
        var pad = _workbench?.GetPad(typeof(UnoDevelop.Workbench.ErrorListPad));
        if (pad is not null)
            ShowPad(pad);
    }

    private void ShowSolutionExplorerPad()
    {
        var pad = _workbench?.GetPad(typeof(UnoDevelop.Workbench.SolutionExplorerPad));
        if (pad is not null)
            ShowPad(pad);
    }

    private void PopulateAddInToolbar()
    {
        var toolbarBuilder = ServiceSingleton.ServiceProvider.GetService(typeof(IUnoAddInToolbarBuilder))
            as IUnoAddInToolbarBuilder;
        toolbarBuilder?.PopulateToolbar(MainToolbar, this, "/UnoDevelop/Workbench/ToolBar/Standard");

        RunToolbarButton = FindToolbarButton("RunWithoutDebugging");
        StopToolbarButton = FindToolbarButton("Stop");
        DebugToolbarButton = FindToolbarButton("Debug");
        ContinueToolbarButton = FindToolbarButton("Continue");
        StepOverToolbarButton = FindToolbarButton("StepOver");
        StepInToolbarButton = FindToolbarButton("StepInto");
        StepOutToolbarButton = FindToolbarButton("StepOut");

        SetToolbarButtonEnabled(StopToolbarButton, false);
        SetStepButtonsEnabled(false);
        UpdateExecutionButtonsEnabled();
    }

    // Re-evaluates the declarative <Condition>s on the main toolbar (SolutionOpen / ExecutionActive
    // / Debugging / Paused) and updates each button's enabled state — the SharpDevelop
    // ToolBarService.UpdateStatus equivalent. Call whenever the underlying state changes.
    private void UpdateExecutionButtonsEnabled()
    {
        (ServiceSingleton.ServiceProvider.GetService(typeof(IUnoAddInToolbarBuilder)) as IUnoAddInToolbarBuilder)
            ?.UpdateStatus(MainToolbar);
        // The Tests pad toolbar's Run/Stop also gate on SolutionOpen, so refresh it on the same
        // state transitions (solution open/close, execution start/stop).
        UpdateTestsPadButtonsEnabled();
    }

    private WpfButton? FindToolbarButton(string id)
    {
        return MainToolbar.Items
            .OfType<WpfButton>()
            .FirstOrDefault(button => string.Equals(button.Tag as string, id, StringComparison.Ordinal));
    }

    private void SetExplorerStatus(string text) => ShellStatusText.Text = text;

    internal void SetStatusBarMessage(string message, bool highlighted)
    {
        if (highlighted)
        {
            ShellStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            ShellStatusText.Background = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        }
        else
        {
            ShellStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            ShellStatusText.Background = null;
        }
        ShellStatusText.Text = message;
    }

    internal void UpdateStatusBarProgress(string? taskName, double progress, ICSharpCode.SharpDevelop.OperationStatus status)
    {
        if (double.IsNaN(progress))
        {
            ShellProgressBar.IsIndeterminate = true;
            ShellProgressBar.Visibility = Visibility.Visible;
        }
        else if (progress >= 0)
        {
            ShellProgressBar.IsIndeterminate = false;
            ShellProgressBar.Value = progress;
            ShellProgressBar.Visibility = progress >= 1 ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            ShellProgressBar.Visibility = Visibility.Collapsed;
        }

        if (status == ICSharpCode.SharpDevelop.OperationStatus.Error)
            ShellProgressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
        else if (status == ICSharpCode.SharpDevelop.OperationStatus.Warning)
            ShellProgressBar.Foreground = new SolidColorBrush(Microsoft.UI.Colors.YellowGreen);
        else
            ShellProgressBar.ClearValue(ProgressBar.ForegroundProperty);

        ShellTaskNameText.Text = taskName ?? "";
    }

    private void PopulateTestsPadChrome()
    {
        if (_testResultsPad is null) return;
        var toolbarBuilder = ServiceSingleton.ServiceProvider.GetService(typeof(IUnoAddInToolbarBuilder))
            as IUnoAddInToolbarBuilder;
        toolbarBuilder?.PopulateToolbar(_testResultsPad.Toolbar, _testResultsPad,
            "/SharpDevelop/Pads/TestsPad/Toolbar/Standard");
    }

    // Re-evaluates the declarative <Condition>s on the Tests pad toolbar (SolutionOpen / TestsRunning)
    // so Run/Stop enable/disable as a run starts and completes.
    private void UpdateTestsPadButtonsEnabled()
    {
        if (_testResultsPad is null) return;
        (ServiceSingleton.ServiceProvider.GetService(typeof(IUnoAddInToolbarBuilder)) as IUnoAddInToolbarBuilder)
            ?.UpdateStatus(_testResultsPad.Toolbar);
    }

    private static void SetToolbarButtonEnabled(WpfButton? button, bool enabled)
    {
        if (button is not null)
            button.IsEnabled = enabled;
    }

    private static void PopulateAddInMenu(MenuBarItem menu, string path, object? owner = null)
    {
        menu.Items.Clear();
        foreach (var item in AddInTree.BuildItems<MenuItemDescriptor>(path, owner, false))
        {
            var flyoutItem = CreateMenuFlyoutItem(item);
            if (flyoutItem is not null)
                menu.Items.Add(flyoutItem);
        }
    }

    private static MenuFlyoutItemBase? CreateMenuFlyoutItem(MenuItemDescriptor descriptor)
    {
        var type = descriptor.Codon.Properties.Contains("type") ? descriptor.Codon.Properties["type"] : "Command";
        if (type == "Separator")
            return new MenuFlyoutSeparator();

        var command = CommandWrapper.CreateLazyCommand(descriptor.Codon, descriptor.Conditions);
        var item = new MenuFlyoutItem
        {
            Text = descriptor.Codon.Properties["label"],
            IsEnabled = command.CanExecute(descriptor.Parameter)
        };
        item.Click += (_, _) =>
        {
            if (command.CanExecute(descriptor.Parameter))
                command.Execute(descriptor.Parameter);
        };
        return item;
    }

    private void HookProjectServiceEvents()
    {
        if (_projectService is null)
        {
            return;
        }

        _projectService.CurrentSolutionChanged += OnCurrentSolutionChanged;
        _projectService.SolutionOpened += OnSolutionOpened;
        _projectService.SolutionClosed += OnSolutionClosed;
        _projectService.ProjectItemAdded += OnProjectItemAdded;
        _projectService.ProjectItemRemoved += OnProjectItemRemoved;
        SD.BuildService.BuildFinished += OnBuildFinished;
        Unloaded += OnMainPageUnloaded;
    }

    private void OnMainPageUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_projectService is null)
        {
            return;
        }

        _projectService.CurrentSolutionChanged -= OnCurrentSolutionChanged;
        _projectService.SolutionOpened -= OnSolutionOpened;
        _projectService.SolutionClosed -= OnSolutionClosed;
        _projectService.ProjectItemAdded -= OnProjectItemAdded;
        _projectService.ProjectItemRemoved -= OnProjectItemRemoved;
        SD.BuildService.BuildFinished -= OnBuildFinished;
        Unloaded -= OnMainPageUnloaded;
    }

    private void OnBuildFinished(object? sender, BuildEventArgs e)
    {
        var taskService = ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService;
        if ((taskService?.TaskCount ?? 0) > 0)
        {
            ShowErrorsPad();
        }
    }

    private async void OnCurrentSolutionChanged(object sender, PropertyChangedEventArgs<ISolution> e)
    {
        (ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService)?.ClearExceptCommentTasks();
        await LoadLanguageServiceProjectsAsync(e.NewValue);
        await RefreshSolutionTreeAsync();
        UpdateShellChrome();
        UpdateExecutionButtonsEnabled();
    }

    private async void OnSolutionOpened(object? sender, SolutionEventArgs e)
    {
        (ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService)?.ClearExceptCommentTasks();
        ICSharpCode.Core.PropertyService.Save();
        await LoadLanguageServiceProjectsAsync(e.Solution);
        await RefreshSolutionTreeAsync();
        ShowSolutionExplorerPad();
        UpdateShellChrome();
        UpdateExecutionButtonsEnabled();
    }

    private async void OnSolutionClosed(object? sender, SolutionEventArgs e)
    {
        (ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService)?.Clear();
        _workbench?.CloseAllSolutionViews(false);
        // Reclaim this solution's per-project dataflow sessions (slice 47) up front — the refresh
        // below may fall back to a plain directory listing with no resolvable solution file, which
        // never runs the prune-after-rebuild reconciliation in SolutionExplorerTreeBuilder.
        await Microsoft.VisualStudio.ProjectSystem.Tree.Dependencies.SharpDevelopDependenciesSnapshotFactory.ClearAllAsync();
        await RefreshSolutionTreeAsync();
        UpdateShellChrome();
        UpdateExecutionButtonsEnabled();
    }

    private async Task LoadLanguageServiceProjectsAsync(ISolution? solution)
    {
        if (_languageServiceRegistry is null || solution is null)
        {
            return;
        }

        var projectsByService = solution.Projects
            .Where(project => project is not null)
            .GroupBy(project =>
            {
                var languageService = _languageServiceRegistry.GetService(project.FileName.ToString());
                return ReferenceEquals(languageService, _languageServiceRegistry.FallbackService) ? null : languageService;
            })
            .Where(group => group.Key is not null);

        foreach (var group in projectsByService)
        {
            try
            {
                if (group.Key is ICSharpCode.SharpDevelop.LanguageServices.Roslyn.CSharpVBLanguageService roslynService)
                {
                    var snapshots = group
                        .SelectMany(LanguageServiceProjectSnapshot.FromProjectAllTargetFrameworks)
                        .ToArray();
                    await roslynService.LoadProjectsAsync(snapshots, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"Language service project load failed: {ex.Message}");
            }
        }
    }

    private async void OnProjectItemAdded(object? sender, ProjectItemEventArgs e)
    {
        await UpdateLanguageServiceForProjectItemAddedAsync(e);
        await OnProjectItemCollectionChangedAsync(e);
    }

    private async void OnProjectItemRemoved(object? sender, ProjectItemEventArgs e)
    {
        UpdateLanguageServiceForProjectItemRemoved(e);
        await OnProjectItemCollectionChangedAsync(e);
    }

    private async Task OnProjectItemCollectionChangedAsync(ProjectItemEventArgs e)
    {
        // Slice 49: an item add/remove within one project only needs that project's node rebuilt,
        // not a full Solution Explorer refresh (state capture/restore + every project's tree).
        // Falls back to the full refresh if the project's node can't be found (e.g. before the
        // tree has been built once, or the item belongs to a project not yet in the live tree).
        var refreshedInPlace = _solutionExplorerPad is not null
            && e.Project?.FileName.ToString() is { } projectPath
            && await UnoDevelop.Services.SolutionExplorerTreeBuilder.RefreshProjectNodeAsync(SolutionTree, projectPath, e.Project);

        if (!refreshedInPlace)
        {
            await RefreshSolutionTreeAsync();
        }

        UpdateExecutionButtonsEnabled();
    }

    /// <summary>
    /// Slice 3 incremental update (docs/language-services.md §2.1): a single new <c>Compile</c>
    /// item only needs a targeted document add, not a whole-project re-snapshot. Any other item
    /// type (References, ProjectReferences, ...) can change project-level compilation inputs, so
    /// those still fall back to a full project reload.
    /// </summary>
    private async Task UpdateLanguageServiceForProjectItemAddedAsync(ProjectItemEventArgs e)
    {
        if (_languageServiceRegistry is null || e.Project is null || e.ProjectItem is null)
        {
            return;
        }

        if (e.ProjectItem.ItemType != ItemType.Compile)
        {
            await LoadLanguageServiceProjectAsync(e.Project);
            return;
        }

        var languageService = _languageServiceRegistry.GetService(e.Project.FileName.ToString());
        if (languageService is not ICSharpCode.SharpDevelop.LanguageServices.Roslyn.CSharpVBLanguageService roslynService)
        {
            return;
        }

        var fileName = e.ProjectItem.FileName.ToString();
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        try
        {
            await roslynService.AddCompileDocumentAsync(e.Project.FileName.ToString(), fileName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Language service incremental document add failed for {fileName}: {ex.Message}");
        }
    }

    private void UpdateLanguageServiceForProjectItemRemoved(ProjectItemEventArgs e)
    {
        if (_languageServiceRegistry is null || e.Project is null || e.ProjectItem is null)
        {
            return;
        }

        if (e.ProjectItem.ItemType != ItemType.Compile)
        {
            _ = LoadLanguageServiceProjectAsync(e.Project);
            return;
        }

        if (_languageServiceRegistry.GetService(e.Project.FileName.ToString())
            is not ICSharpCode.SharpDevelop.LanguageServices.Roslyn.CSharpVBLanguageService roslynService)
        {
            return;
        }

        var fileName = e.ProjectItem.FileName.ToString();
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        try
        {
            roslynService.RemoveDocument(fileName);
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Language service incremental document remove failed for {fileName}: {ex.Message}");
        }
    }

    private async Task LoadLanguageServiceProjectAsync(IProject? project)
    {
        if (_languageServiceRegistry is null || project is null)
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(project.FileName.ToString());
        if (languageService is ICSharpCode.SharpDevelop.LanguageServices.Roslyn.CSharpVBLanguageService roslynService)
        {
            try
            {
                var snapshots = LanguageServiceProjectSnapshot.FromProjectAllTargetFrameworks(project);
                await roslynService.LoadProjectsAsync(snapshots, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"Language service project reload failed for {project.FileName}: {ex.Message}");
            }
        }
    }

    private async Task RefreshSolutionTreeAsync()
    {
        if (_solutionExplorerPad is null)
        {
            return;
        }

        if (_isRefreshingSolutionTree)
        {
            return;
        }

        _isRefreshingSolutionTree = true;
        try
        {
            CaptureSolutionTreeState();
            await LoadSolutionTreeAsync();
            RestoreSolutionTreeState();
            PopulateExplorerChrome();
            SetExplorerStatus("Solution Explorer refreshed.");
        }
        finally
        {
            _isRefreshingSolutionTree = false;
        }
    }

    private void SeedInitialDocument()
    {
        if (_workbench is null)
        {
            return;
        }

        const string bootstrapCode = "// UnoDevelop editor bootstrap\nusing System;\n\nnamespace UnoDevelop.App;\n\npublic static class EntryPoint\n{\n    public static void Run()\n    {\n        Console.WriteLine(\"UnoDock + UnoEdit integrated.\");\n    }\n}";
        var initialView = new EditorViewContent("Program.cs", bootstrapCode, null);
        _workbench.ShowView(initialView, true);
    }

    private async void OnBuildSolutionClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await BuildSolutionAsync();

    internal async Task BuildSolutionAsync()
    {
        var solution = _projectService?.CurrentSolution;
        if (solution is null || (solution.Projects?.Count ?? 0) == 0)
        {
            SetExplorerStatus("No solution loaded to build.");
            return;
        }

        SetExplorerStatus("Building...");
        PrepareExecutionOutputCategory("Build");

        try
        {
            var buildService = SD.BuildService;
            var results = await buildService.BuildAsync(solution, new ICSharpCode.SharpDevelop.Project.BuildOptions(ICSharpCode.SharpDevelop.Project.BuildTarget.Build));
            var errorCount = results.ErrorCount;
            SetExplorerStatus(errorCount == 0 ? "Build succeeded." : $"Build failed: {errorCount} error(s).");
        }
        catch (Exception ex)
        {
            SetExplorerStatus("Build error: " + ex.Message);
        }
    }

    private void OnCancelBuildClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => CancelBuild();

    internal void CancelBuild()
    {
        SD.BuildService.CancelBuild();
        SetExplorerStatus("Build canceled.");
    }

    private void HookRunServiceEvents()
    {
        // Expose live execution state to the AddIn-tree condition evaluators (SolutionOpen /
        // ExecutionActive / Debugging / Paused) so toolbar buttons enable/disable declaratively.
        ExecutionState.IsRunning = () => _runService.IsRunning;
        ExecutionState.IsDebugging = () => _debugService.IsDebugging;
        ExecutionState.IsPaused = () => _debugPaused;
        ExecutionState.IsTestsRunning = () => _testService?.IsRunning ?? false;

        _runService.RunStarted += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateExecutionButtonsEnabled();
            SetExplorerStatus("Running...");
        });
        _runService.RunStopped += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateExecutionButtonsEnabled();
            SetExplorerStatus("Stopped.");
        });
    }

    private string? ResolveStartupProjectPath()
    {
        var project = _projectService?.CurrentProject
            ?? _projectService?.CurrentSolution?.Projects?.FirstOrDefault();
        var path = project?.FileName?.ToString();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private async void OnRunClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await RunWithoutDebuggingAsync();

    internal async Task RunWithoutDebuggingAsync()
    {
        var category = PrepareExecutionOutputCategory("Run");

        if (_runService.IsRunning)
        {
            ReportExecutionIssue(category, "A run is already in progress. Stop it before starting a new one.");
            return;
        }

        // Only one execution session at a time — tear down any active debug session
        // so it does not hold a lock on the build output.
        if (_debugService.IsDebugging)
        {
            category?.AppendLine("> Stopping active debug session first...");
            _debugService.Stop();
        }

        var projectPath = ResolveStartupProjectPath();
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            ReportExecutionIssue(category, "No startup project to run.");
            return;
        }

        if (category is null)
        {
            SetExplorerStatus("Cannot run: output pad unavailable.");
            return;
        }

        SetExplorerStatus("Starting...");
        await _runService.StartAsync(projectPath, category);
    }

    private void OnStopRunClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => StopRunOrDebug();

    internal void StopRunOrDebug()
    {
        if (_debugService.IsDebugging)
            _debugService.Stop();
        else
            _runService.Stop();
    }

    private void HookDebugServiceEvents()
    {
        if (_callStackPad is not null)
        {
            _callStackPad.FrameActivated += (filePath, line) =>
                DispatcherQueue.TryEnqueue(() => NavigateToSource(filePath, line));
        }

        UnoDevelop.Debugger.LocalsPad.GetVisualizerActions = v =>
        {
            var descriptors = UnoDevelop.Debugger.Visualizers.VisualizerDescriptors.GetAll();
            var result = new List<(string, Action)>();
            foreach (var d in descriptors)
            {
                if (!d.IsVisualizerAvailable(v.Type)) continue;
                VariableInfo? ReEval()
                {
                    var task = Task.Run(() =>
                        _debugService.EvaluateAsync(v.EvaluateName ?? v.Name));
                    return task.GetAwaiter().GetResult();
                }
                var cmd = d.CreateVisualizerCommand(v, ReEval);
                result.Add((d.GetType().Name.Replace("VisualizerDescriptor", ""), () => cmd.Execute()));
            }
            return result;
        };
        _debugService.DebugStarted += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _debugPaused = false;
            UpdateExecutionButtonsEnabled();
            SetExplorerStatus("Debugging...");
        });
        _debugService.DebugStopped += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _debugPaused = false;
            UpdateExecutionButtonsEnabled();
            ClearExecutionPosition();
            SetExplorerStatus("Debug session ended.");
        });
        _debugService.Stopped += (threadId, reason) => DispatcherQueue.TryEnqueue(() =>
        {
            _debugPaused = true;
            UpdateExecutionButtonsEnabled();
            SetExplorerStatus($"Paused ({reason}).");
        });
        _debugService.Continued += () => DispatcherQueue.TryEnqueue(() =>
        {
            _debugPaused = false;
            UpdateExecutionButtonsEnabled();
            ClearExecutionPosition();
            SetExplorerStatus("Debugging...");
        });
        _debugService.ExecutionPositionChanged += (filePath, line) =>
            DispatcherQueue.TryEnqueue(() => ShowExecutionPosition(filePath, line));
    }

    private void HookTestServiceEvents()
    {
        if (_testService is null)
            return;

        _testService.TestRunStarted += () => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTestsPadButtonsEnabled();
            SetExplorerStatus("Tests running...");
        });
        _testService.TestRunCompleted += () => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTestsPadButtonsEnabled();
            SetExplorerStatus("Test run completed.");
        });
    }

    internal void RefreshTests()
        => _ = RefreshTestsAsync();

    internal async Task RefreshTestsAsync()
    {
        _testService?.RefreshTests();
        ShowTestsPad();
        if (_testResultsPad is not null)
            await _testResultsPad.RefreshTestsAsync();
    }

    internal async Task RunAllTestsAsync()
    {
        if (_testService is null || _testService.IsRunning)
            return;

        await _testService.RunAllTestsAsync();
    }

    internal void StopTests()
    {
        _testService?.Stop();
    }

    internal void RunSelectedTest()
    {
        _testResultsPad?.RunSelectedTest();
        ShowTestsPad();
    }

    internal void ExpandAllTests()
    {
        _testResultsPad?.ExpandAll();
    }

    internal void CollapseAllTests()
    {
        _testResultsPad?.CollapseAll();
    }

    private void HookWorkbenchPadEvents()
    {
        if (_workbench is null) return;
        _workbench.PadAdded += (_, pad) => DispatcherQueue.TryEnqueue(() => OnPadAdded(pad));
    }

    /// <summary>Serializes the current docking arrangement (pane sizes/positions, visible pads) to disk.</summary>
    internal void SaveCurrentLayout(string fileName)
    {
        try
        {
            var dir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            new XmlLayoutSerializer(DockManager).Serialize(fileName);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("Failed to save layout to " + fileName + ": " + ex);
        }
    }

    /// <summary>Test/tooling hook: toggles a pad (by PadDescriptor.ClassName) between docked and auto-hidden.</summary>
    internal bool ToggleAutoHideForTesting(string contentId)
    {
        if (_padWindows.TryGetValue(contentId, out var anchorable))
        {
            anchorable.ToggleSingleAutoHide();
            return true;
        }
        return false;
    }

    /// <summary>Test/tooling hook: hides a pad (by PadDescriptor.ClassName) so its layout can be authored.</summary>
    internal bool HidePadForTesting(string contentId)
    {
        if (_padWindows.TryGetValue(contentId, out var anchorable))
        {
            anchorable.Hide();
            return true;
        }
        return false;
    }

    /// <summary>Test/tooling hook: re-shows a pad previously hidden via HidePadForTesting.</summary>
    internal bool ShowPadForTesting(string contentId)
    {
        if (_padWindows.TryGetValue(contentId, out var anchorable))
        {
            anchorable.Show();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Deserializes a saved docking arrangement. Only pads (LayoutAnchorable, matched to their live
    /// control by ContentId == PadDescriptor.ClassName) are restored - open documents are not part
    /// of a layout switch, so any LayoutDocument entries are skipped, matching OpenDevelop's
    /// DockWorkspace.LayoutSerializationCallback.
    /// </summary>
    internal void RestoreLayout(string fileName)
    {
        if (!File.Exists(fileName))
            return;
        try
        {
            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.LayoutSerializationCallback += OnLayoutSerializationCallback;
            try
            {
                serializer.Deserialize(fileName);
            }
            finally
            {
                serializer.LayoutSerializationCallback -= OnLayoutSerializationCallback;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn("Failed to restore layout from " + fileName + ": " + ex);
            return;
        }

        // Deserialize replaces DockManager.Layout wholesale with a brand-new tree, so the
        // LeftPane/RightPane/BottomPane/DocumentPane fields captured at InitializeComponent time
        // now point at orphaned panes. Re-resolve them from the new tree (anchorable panes carry
        // their model-level Name through serialization; there's always exactly one document pane)
        // so later pad additions (OnPadAdded's targetPane routing) keep landing in the visible tree.
        LeftPane = FindAnchorablePaneByName("LeftPane") ?? LeftPane;
        RightPane = FindAnchorablePaneByName("RightPane") ?? RightPane;
        BottomPane = FindAnchorablePaneByName("BottomPane") ?? BottomPane;
        DocumentPane = DockManager.Layout?.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault() ?? DocumentPane;
    }

    /// <summary>
    /// Test hook: reports each dock pane's live child ContentIds (post-reassignment if a
    /// RestoreLayout swapped the panes), to verify layout save/restore round-trips correctly and
    /// that pad routing (OnPadAdded's targetPane) still lands in the visible tree afterward.
    /// </summary>
    internal object GetDockPaneDiagForTesting()
    {
        var liveDescendents = DockManager.Layout?.Descendents().ToHashSet() ?? new HashSet<AvalonDock.Layout.ILayoutElement>();
        return new
        {
            leftPane = DescribePaneChildren(LeftPane),
            rightPane = DescribePaneChildren(RightPane),
            bottomPane = DescribePaneChildren(BottomPane),
            documentPane = DocumentPane?.Children.Select(c => new { contentId = (c as LayoutDocument)?.ContentId, title = (c as LayoutDocument)?.Title }).ToArray() ?? [],
            leftPaneIsLive = LeftPane is not null && liveDescendents.Contains(LeftPane),
            rightPaneIsLive = RightPane is not null && liveDescendents.Contains(RightPane),
            bottomPaneIsLive = BottomPane is not null && liveDescendents.Contains(BottomPane),
            documentPaneIsLive = DocumentPane is not null && liveDescendents.Contains(DocumentPane),
        };
    }

    // RightPane (and, in principle, any empty anchorable pane) can be legitimately garbage-collected
    // by AvalonDock even without a layout restore - see the RightPane comment in
    // LayoutConfigurationTests.SaveAndRestoreLayout_RoundTripsPadsAndKeepsPanesLive.
    private static object[] DescribePaneChildren(LayoutAnchorablePane? pane)
        => pane?.Children.Select(c => new { contentId = (c as LayoutAnchorable)?.ContentId, title = (c as LayoutAnchorable)?.Title }).ToArray() ?? [];

    private LayoutAnchorablePane? FindAnchorablePaneByName(string name)
        => DockManager.Layout?.Descendents().OfType<LayoutAnchorablePane>()
            .FirstOrDefault(p => p.Name == name);

    private void OnLayoutSerializationCallback(object? sender, LayoutSerializationCallbackEventArgs e)
    {
        if (e.Model is LayoutDocument)
        {
            e.Cancel = true;
            return;
        }

        if (e.Model is LayoutAnchorable anchorable
            && _padWindows.TryGetValue(anchorable.ContentId, out var existing)
            && existing.Content is not null)
        {
            e.Content = existing.Content;
            return;
        }

        e.Cancel = true;
    }

    private void LoadAddInPads()
    {
        if (_workbench is null)
            return;

        var pads = AddInTree.BuildItems<PadDescriptor>("/SharpDevelop/Workbench/Pads", this, false);
        foreach (var pad in pads)
        {
            _workbench.ActivatePad(pad);
        }
    }

    private void OnPadAdded(PadDescriptor pad)
    {
        pad.CreatePad();
        if (pad.PadContent?.Control is not FrameworkElement padControl) return;
        AttachPadServices(padControl);

        if (_padWindows.TryGetValue(pad.ClassName, out var existing))
        {
            existing.IsSelected = true;
            existing.IsActive = true;
            return;
        }

        var anchorable = new LayoutAnchorable
        {
            Title = pad.Title,
            ContentId = pad.ClassName,
            Content = padControl,
        };

        // Route by default position; fall back to bottom
        var position = pad.DefaultPosition;
        LayoutAnchorablePane targetPane;
        if (position.HasFlag(DefaultPadPositions.Left))
            targetPane = LeftPane;
        else if (position.HasFlag(DefaultPadPositions.Right))
            targetPane = RightPane;
        else
            targetPane = BottomPane;

        targetPane.Children.Add(anchorable);
        _padWindows[pad.ClassName] = anchorable;
        anchorable.IsSelected = true;
    }

    private void PopulateViewMenu()
    {
        if (_workbench is null)
            return;

        ViewMenu.Items.Clear();
        var items = AddInTree.BuildItems<object>("/SharpDevelop/Workbench/ViewMenu", this, false);
        foreach (var item in items)
        {
            switch (item)
            {
                case PadMenuDescriptor padMenu:
                    AddPadMenuItems(padMenu.Category);
                    break;
                case MenuItemDescriptor descriptor:
                    var menuItem = CreateViewMenuItem(descriptor);
                    if (menuItem is not null)
                        ViewMenu.Items.Add(menuItem);
                    break;
            }
        }
    }

    private void AddPadMenuItems(string category)
    {
        if (_workbench is null)
            return;

        var pads = _workbench.PadContentCollection
            .Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase);
        foreach (var pad in pads)
        {
            var item = new MenuFlyoutItem
            {
                Text = pad.Title
            };
            item.Click += (_, _) => ShowPad(pad);
            ViewMenu.Items.Add(item);
        }
    }

    private static MenuFlyoutItemBase? CreateViewMenuItem(MenuItemDescriptor descriptor)
    {
        var type = descriptor.Codon.Properties.Contains("type") ? descriptor.Codon.Properties["type"] : "Command";
        return type == "Separator"
            ? new MenuFlyoutSeparator()
            : null;
    }

    private void ShowPad(PadDescriptor pad)
    {
        if (_padWindows.TryGetValue(pad.ClassName, out var anchorable))
        {
            anchorable.IsSelected = true;
            anchorable.IsActive = true;
            return;
        }

        _workbench?.ActivatePad(pad);
    }

    // Shows the Properties pad (UnoPropertyGrid) and binds it to the selected node.
    internal void ShowPropertiesForNode(UnoDevelop.Services.SolutionExplorerNodeContext? node)
    {
        if (node is null)
            return;

        var pad = _workbench?.GetPad(typeof(PropertiesPad));
        if (pad is not null)
            ShowPad(pad);

        void Apply() => _propertiesPad?.SetSelectedObject(
            new UnoDevelop.Services.SolutionExplorerNodeProperties(node));

        // The pad control is attached on an enqueued OnPadAdded turn; if it isn't
        // ready yet, apply after that runs (same FIFO dispatcher queue).
        if (_propertiesPad is not null)
            Apply();
        else
            DispatcherQueue.TryEnqueue(Apply);
    }

    private void AttachPadServices(FrameworkElement padControl)
    {
        switch (padControl)
        {
            case LocalsPad localsPad:
                _localsPad = localsPad;
                localsPad.Attach(_debugService);
                break;
            case CallStackPad callStackPad:
                _callStackPad = callStackPad;
                callStackPad.Attach(_debugService);
                break;
            case WatchPad watchPad:
                _watchPad = watchPad;
                watchPad.Attach(_debugService);
                break;
            case ImmediatePad immediatePad:
                _immediatePad = immediatePad;
                immediatePad.Attach(_debugService);
                break;
            case ThreadsPad threadsPad:
                _threadsPad = threadsPad;
                threadsPad.Attach(_debugService);
                break;
            case ModulesPad modulesPad:
                _modulesPad = modulesPad;
                modulesPad.Attach(_debugService);
                break;
            case SolutionExplorerPad solutionExplorerPad:
                _solutionExplorerPad = solutionExplorerPad;
                solutionExplorerPad.Attach(this);
                _ = RefreshSolutionTreeAsync();
                PopulateExplorerChrome();
                if ((_projectService?.CurrentSolution?.Projects?.Count ?? 0) == 0)
                {
                    foreach (System.Windows.Input.ICommand command in ICSharpCode.Core.AddInTree.BuildItems<System.Windows.Input.ICommand>(
                        "/SharpDevelop/Workbench/AutostartNothingLoaded", null, false))
                    {
                        command.Execute(null);
                    }
                }
                break;
            case TestResultsPad testResultsPad:
                _testResultsPad = testResultsPad;
                if (_testService is not null)
                    testResultsPad.Attach(_testService);
                PopulateTestsPadChrome();
                break;
            case UnoDevelop.Workbench.ErrorListPad errorListPad:
                _errorListPad = errorListPad;
                errorListPad.ItemActivated += (file, line, column) =>
                    DispatcherQueue.TryEnqueue(() => NavigateToSource(file, line, column));
                break;
            case OutputPad outputPad:
                _outputPad = outputPad;
                outputPad.LinkActivated += (file, line, _) =>
                    DispatcherQueue.TryEnqueue(() => NavigateToSource(file, line));
                break;
            case PropertiesPad propertiesPad:
                _propertiesPad = propertiesPad;
                break;
        }
    }

    private void SetStepButtonsEnabled(bool enabled)
    {
        SetToolbarButtonEnabled(ContinueToolbarButton, enabled);
        SetToolbarButtonEnabled(StepOverToolbarButton, enabled);
        SetToolbarButtonEnabled(StepInToolbarButton, enabled);
        SetToolbarButtonEnabled(StepOutToolbarButton, enabled);
    }

    private async void OnContinueClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await ContinueDebugAsync();

    internal Task ContinueDebugAsync()
        => _debugService.ContinueAsync();

    private async void OnStepOverClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await StepOverDebugAsync();

    internal Task StepOverDebugAsync()
        => _debugService.StepOverAsync();

    private async void OnStepInClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await StepIntoDebugAsync();

    internal Task StepIntoDebugAsync()
        => _debugService.StepInAsync();

    private async void OnStepOutClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await StepOutDebugAsync();

    internal Task StepOutDebugAsync()
        => _debugService.StepOutAsync();

    private void OnBreakpointsChanged(string filePath, IReadOnlyList<int> lines)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var fn = ICSharpCode.Core.FileName.Create(filePath);
        SD.BookmarkManager.RemoveAll(b => b.FileName == fn);
        foreach (var line in lines)
        {
            var bm = new ICSharpCode.SharpDevelop.Editor.Bookmarks.Bookmark();
            bm.FileName = fn;
            bm.Location = new ICSharpCode.AvalonEdit.Document.TextLocation(line, 1);
            SD.BookmarkManager.AddMark(bm);
        }

        if (_debugService.IsDebugging)
            _ = _debugService.SetBreakpointsAsync(filePath, lines);
    }

    private void ShowExecutionPosition(string filePath, int line)
    {
        // Clear arrow on all open editors
        ClearExecutionPosition();
        if (string.IsNullOrEmpty(filePath) || line <= 0) return;

        // Open the file if not already open
        if (!_openFileViews.ContainsKey(filePath) && File.Exists(filePath))
            OpenFileInWorkbench(filePath);

        if (!_openFileViews.TryGetValue(filePath, out var view)) return;
        if (view is not EditorViewContent editorView) return;

        // Show arrow in gutter
        editorView.Editor.TextArea?.TextView?.SetCurrentExecutionLine(line);

        // Yellow line background via TextView.AddBackgroundHighlight
        var tv = editorView.Editor.TextArea?.TextView;
        if (tv is not null)
        {
            tv.ClearBackgroundHighlights("exec-line");
            try
            {
                var docLine = editorView.Editor.Document?.GetLineByNumber(line);
                if (docLine is not null)
                {
                    var fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(80, 255, 220, 0));
                    tv.AddBackgroundHighlight("exec-line", docLine, fill, null);
                }
            }
            catch { }
        }

        // Scroll to the line and activate the document
        editorView.Editor.ScrollToLine(line);
        if (_documents.TryGetValue(editorView, out var doc))
        {
            doc.IsSelected = true;
            doc.IsActive = true;
        }
    }

    /// Open a file and scroll to a line WITHOUT moving the execution arrow.
    /// Used when the user clicks a Call Stack frame to inspect its source.
    // column defaults to 1 (not the diagnostic's real column) for callers that only have a line
    // number to navigate to (e.g. breakpoints) — see ErrorListPad.ItemActivated for the case
    // that does have a real column and must pass it through.
    private void NavigateToSource(string filePath, int line, int column = 1)
    {
        if (string.IsNullOrEmpty(filePath) || line <= 0) return;

        if (!_openFileViews.ContainsKey(filePath) && File.Exists(filePath))
            OpenFileInWorkbench(filePath);

        if (!_openFileViews.TryGetValue(filePath, out var view)) return;
        if (view is not EditorViewContent editorView) return;

        // Previously only scrolled the viewport to the line (ScrollToLine) without moving the
        // caret at all, so double-clicking an error opened the right file/line but left the
        // caret wherever it already was. MoveEditorToPosition is the same "set caret, zero-length
        // select, scroll into view, activate the tab" helper Go To Definition/rename/nav-bar
        // clicks already use.
        MoveEditorToPosition(editorView, new TextPosition(line, Math.Max(column, 1)));
    }

    private void ClearExecutionPosition()
    {
        foreach (var view in _openFileViews.Values)
        {
            if (view is not EditorViewContent ev) continue;
            ev.Editor.TextArea?.TextView?.SetCurrentExecutionLine(0);
            ev.Editor.TextArea?.TextView?.ClearBackgroundHighlights("exec-line");
        }
    }

    private async void OnDebugClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await StartDebugAsync();

    internal async Task StartDebugAsync()
    {
        var category = PrepareExecutionOutputCategory("Debug");

        if (_debugService.IsDebugging)
        {
            ReportExecutionIssue(category, "A debug session is already running. Stop it before starting a new one.");
            return;
        }

        // A still-running "Run without debugging" process holds a file lock on the
        // build output, which makes the debug build silently fail. Stop it first so
        // debugging can start after a failed/lingering run.
        if (_runService.IsRunning)
        {
            category?.AppendLine("> Stopping the running process first (it locks the build output)...");
            _runService.Stop();
            SetToolbarButtonEnabled(RunToolbarButton, true);
            SetToolbarButtonEnabled(StopToolbarButton, false);
        }

        var projectPath = ResolveStartupProjectPath();
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            ReportExecutionIssue(category, "No startup project to debug.");
            return;
        }

        if (category is null)
        {
            SetExplorerStatus("Cannot debug: output pad unavailable.");
            return;
        }

        SetExplorerStatus("Starting debugger...");
        await _debugService.StartAsync(projectPath, category);
    }

    private async Task SyncAllBreakpointsToDapAsync()
    {
        if (!_debugService.IsDebugging) return;
        var byFile = SD.BookmarkManager.Bookmarks
            .Where(b => b.FileName != null)
            .GroupBy(b => b.FileName.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (var group in byFile)
        {
            var lines = group.Select(b => b.LineNumber).OrderBy(x => x).ToList();
            await _debugService.SetBreakpointsAsync(group.Key, lines);
        }
    }

    internal void CollapseAllSolutionTree()
    {
        if (_solutionExplorerPad is null)
            return;

        foreach (var node in SolutionTree.RootNodes)
            node.IsExpanded = false;
    }

    private static readonly string _dbgLog = "/tmp/unodevelop-debug.log";
    private static void Dbg(string msg)
    {
        try { System.IO.File.AppendAllText(_dbgLog, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    private void OpenSelectedSolutionOrProject(string path)
    {
        Dbg($"OpenSelectedSolutionOrProject: path={path}");
        if (_projectService is null)
        {
            Dbg("FAIL: _projectService is null");
            return;
        }

        var normalizedPath = NormalizeInputPath(path);
        Dbg($"normalizedPath={normalizedPath}, exists={System.IO.File.Exists(normalizedPath)}");
        var fileName = FileName.Create(normalizedPath);
        Dbg($"fileName={fileName}");
        if (fileName is null)
        {
            Dbg("FAIL: FileName.Create returned null");
            ServiceSingleton.GetRequiredService<IMessageService>().ShowError("Failed to open: " + normalizedPath);
            return;
        }

        var opened = _projectService.OpenSolutionOrProject(fileName);
        Dbg($"OpenSolutionOrProject returned {opened}");
        if (!opened)
        {
            ServiceSingleton.GetRequiredService<IMessageService>().ShowError("Failed to open: " + normalizedPath);
            return;
        }

        var dir = System.IO.Path.GetDirectoryName(normalizedPath);
        if (dir != null)
        {
            SD.PropertyService.Set("UnoDevelop.LastOpenDirectory", dir);
            ICSharpCode.Core.PropertyService.Save();
        }

        SetExplorerStatus("Opened: " + normalizedPath);
    }

    private static string NormalizeInputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Trim('"');
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), trimmed));
    }

    private IViewContent? OpenFileInWorkbench(string filePath, bool switchToOpenedView = true)
    {
        if (_workbench is null)
        {
            return null;
        }

        if (_openFileViews.TryGetValue(filePath, out var existingView))
        {
            if (switchToOpenedView)
            {
                _workbench.ActivateView(existingView);
            }

            return existingView;
        }

        if (IconCursorFileReader.CanRead(filePath))
        {
            try
            {
                var iconView = new IconCursorViewerViewContent(filePath);
                _openFileViews[filePath] = iconView;
                _workbench.ShowView(iconView, switchToOpenedView);
                _fileService?.NotifyFileOpened(filePath);
                return iconView;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                SetExplorerStatus("Could not open icon/cursor file: " + ex.Message);
            }
        }

        if (ResourceFileReader.CanRead(filePath))
        {
            try
            {
                var resourceView = new ResourceViewerViewContent(filePath);
                _openFileViews[filePath] = resourceView;
                _workbench.ShowView(resourceView, switchToOpenedView);
                _fileService?.NotifyFileOpened(filePath);
                return resourceView;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or System.Xml.XmlException)
            {
                SetExplorerStatus("Could not open resource file: " + ex.Message);
            }
        }

        var displayBindingFileName = FileName.Create(filePath);
        var displayBinding = displayBindingFileName is null
            ? null
            : ServiceSingleton.GetRequiredService<IDisplayBindingService>().GetBindingPerFileName(displayBindingFileName);
        if (displayBinding is not null && displayBindingFileName is not null && _fileService is not null)
        {
            try
            {
                var openedFile = _fileService.GetOrCreateOpenedFile(displayBindingFileName);
                var displayBindingView = displayBinding.CreateContentForFile(openedFile);
                _openFileViews[filePath] = displayBindingView;
                AttachSecondaryDisplayBindings(displayBindingView);
                _workbench.ShowView(displayBindingView, switchToOpenedView);
                _fileService?.NotifyFileOpened(filePath);
                return displayBindingView;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException or FormatException)
            {
                SetExplorerStatus("Could not open file with display binding: " + ex.Message);
            }
        }

        var text = File.ReadAllText(filePath);
        var view = new EditorViewContent(Path.GetFileName(filePath), text, filePath);
        SyncLanguageServiceDocument(view);
        view.BreakpointsChanged += OnBreakpointsChanged;

        var fn = ICSharpCode.Core.FileName.Create(filePath);
        var saved = SD.BookmarkManager.GetBookmarks(fn).ToList();
        if (saved.Count > 0)
        {
            var lines = saved.Select(b => b.LineNumber).OrderBy(x => x).ToList();
            view.Editor.TextArea?.TextView?.SetBreakpoints(lines);
        }

        _openFileViews[filePath] = view;
        AttachSecondaryDisplayBindings(view);
        _workbench.ShowView(view, switchToOpenedView);
        _fileService?.NotifyFileOpened(filePath);
        ScheduleDiagnosticsRefresh(view);
        return view;
    }

    /// <summary>
    /// Attaches AddIn-registered secondary display bindings (e.g. XamlDesigner's design-surface
    /// preview) to a freshly opened primary view content, mirroring upstream SharpDevelop's
    /// DisplayBindingService.AttachSubWindows call site. Errors are swallowed (logged) so a
    /// misbehaving secondary binding never blocks opening the primary file.
    /// </summary>
    private static void AttachSecondaryDisplayBindings(IViewContent viewContent)
    {
        try
        {
            ServiceSingleton.GetRequiredService<IDisplayBindingService>().AttachSubWindows(viewContent, false);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("Failed to attach secondary display bindings: " + ex.Message);
        }
    }

    private IViewContent? JumpToFilePositionInWorkbench(string filePath, int line, int column)
    {
        var view = OpenFileInWorkbench(filePath, true);
        if (view?.GetService(typeof(ITextEditor)) is ITextEditor editor)
        {
            editor.JumpTo(Math.Max(1, line), Math.Max(1, column));
        }

        return view;
    }

    private void OpenOrActivateView(IViewContent viewContent, bool activate)
    {
        if (!_documents.TryGetValue(viewContent, out var document))
        {
            UIElement? docControl;
            if (viewContent is EditorViewContent editorContent)
            {
                var editor = editorContent.Control as TextEditor ?? new TextEditor();
                editor.Theme = TextEditorTheme.Light;
                ConfigureCodeEditor(editor);
                editor.AllowDrop = true;
                editor.DragOver += OnXamlEditorDragOver;
                editor.Drop += (_, args) => OnXamlEditorDrop(editorContent, args);
                editor.TextChanged += (_, _) => OnEditorTextChanged(editorContent);
                editor.KeyDown += (_, eventArgs) => OnEditorKeyDown(editorContent, eventArgs);
                if (editor.TextArea is not null)
                {
                    editor.TextArea.TextEntered += (_, eventArgs) => OnEditorTextEntered(editorContent, eventArgs);
                    editor.TextArea.TextEntering += (_, eventArgs) => OnEditorTextEntering(eventArgs);
                    if (editor.TextArea.Caret is not null)
                    {
                        editor.TextArea.Caret.PositionChanged += (_, _) => SyncNavigationBarSelectionToCaret(editorContent);
                    }
                }

                editorContent.TypeComboBox.SelectionChanged += (_, _) => OnOutlineTypeSelectionChanged(editorContent);
                editorContent.MemberComboBox.SelectionChanged += (_, _) => OnOutlineMemberSelectionChanged(editorContent);
                editorContent.TargetFrameworkComboBox.SelectionChanged += (_, _) => OnTargetFrameworkSelectionChanged(editorContent);

                var navigationBar = BuildNavigationBar(editorContent);
                var container = new Grid();
                container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(navigationBar, 0);
                Grid.SetRow(editor, 1);
                container.Children.Add(navigationBar);
                container.Children.Add(editor);
                docControl = container;

                _ = RefreshNavigationBarAsync(editorContent);
            }
            else
            {
                docControl = viewContent.Control as UIElement;
            }

            var viewContents = new List<IViewContent> { viewContent };
            viewContents.AddRange(viewContent.SecondaryViewContents);

            ContentControl? viewHost = null;
            List<ToggleButton>? viewButtons = null;
            List<UIElement?>? viewControls = null;
            if (viewContents.Count > 1)
            {
                viewControls = new List<UIElement?> { docControl };
                viewControls.AddRange(viewContent.SecondaryViewContents.Select(secondary => secondary.Control as UIElement));
                viewHost = new ContentControl { Content = viewControls[0] };
                viewButtons = new List<ToggleButton>();
                var switcher = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 2,
                    Padding = new Thickness(8, 4, 8, 4)
                };
                for (var index = 0; index < viewContents.Count; index++)
                {
                    var button = new ToggleButton
                    {
                        Content = index == 0 ? "Code" : viewContents[index].TabPageText,
                        MinWidth = 84,
                        IsChecked = index == 0,
                        Tag = index
                    };
                    viewButtons.Add(button);
                    switcher.Children.Add(button);
                }
                var viewGrid = new Grid();
                viewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                viewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var switcherBorder = new Border
                {
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                    Child = switcher
                };
                Grid.SetRow(viewHost, 0);
                Grid.SetRow(switcherBorder, 1);
                viewGrid.Children.Add(viewHost);
                viewGrid.Children.Add(switcherBorder);
                docControl = viewGrid;
            }

            document = new LayoutDocument
            {
                Title = viewContent.TabPageText,
                ContentId = BuildDocumentContentId(viewContent),
                Content = docControl
            };

            viewContent.IsDirtyChanged += (_, _) => UpdateDocumentTitle(viewContent);
            document.Closing += (_, closingArgs) =>
            {
                if (!TryCloseViewContent(viewContent))
                {
                    closingArgs.Cancel = true;
                }
            };
            UpdateDocumentTitle(viewContent);

            document.Closed += (_, _) =>
            {
                _documents.Remove(viewContent);
                if (viewContent.PrimaryFileName is { } primaryFileName)
                {
                    var primaryPath = primaryFileName.ToString();
                    _openFileViews.Remove(primaryPath);
                    _fileService?.NotifyFileClosed(primaryPath);
                }

                if (viewContent is EditorViewContent editorContent && !string.IsNullOrEmpty(editorContent.FilePath))
                {
                    CancelDiagnosticsRefresh(editorContent.FilePath);
                    (ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService)
                        ?.ClearLanguageDiagnostics(editorContent.FilePath);
                }

                viewContent.Dispose();

                if (_workbench is not null)
                {
                    _workbench.ViewContentCollection.Remove(viewContent);
                }
            };

            DocumentPane.Children.Add(document);
            _documents[viewContent] = document;

            var workbenchWindow = new UnoWorkbenchWindow(document, viewContents, viewHost, viewButtons, viewControls);
            foreach (var content in viewContents)
                content.WorkbenchWindow = workbenchWindow;
        }

        if (activate)
        {
            document.IsSelected = true;
            document.IsActive = true;
        }
    }

    private static void ConfigureCodeEditor(TextEditor editor)
    {
        UnoCodeEditorOptions.Instance.ApplyTo(editor);
        if (!UnoCodeEditorOptions.Instance.EnableFolding)
            return;

        if (editor.TextArea is null)
            return;

        if (_foldingStates.TryGetValue(editor, out var existingState))
        {
            existingState.Refresh();
            return;
        }

        var foldingManager = FoldingManager.Install(editor.TextArea);
        var foldingState = new FoldingState(editor, foldingManager);
        _foldingStates.Add(editor, foldingState);
        editor.TextChanged += foldingState.OnTextChanged;
        foldingState.Refresh();
    }

    private static void OnXamlEditorDragOver(object sender, DragEventArgs args)
    {
        if (sender is TextEditor editor
            && string.Equals(Path.GetExtension(editor.Tag as string), ".xaml", StringComparison.OrdinalIgnoreCase)
            && args.DataView.Contains(StandardDataFormats.Text))
        {
            args.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private static async void OnXamlEditorDrop(EditorViewContent editorContent, DragEventArgs args)
    {
        if (!string.Equals(Path.GetExtension(editorContent.FilePath), ".xaml", StringComparison.OrdinalIgnoreCase)
            || !args.DataView.Contains(StandardDataFormats.Text))
            return;

        var payload = await args.DataView.GetTextAsync();
        const string prefix = "UnoDevelop.XamlToolbox:";
        if (!payload.StartsWith(prefix, StringComparison.Ordinal))
            return;

        InsertXamlToolboxSnippet(editorContent, payload.Substring(prefix.Length));
    }

    internal static bool InsertXamlToolboxSnippet(IViewContent viewContent, string snippet)
    {
        if (!string.Equals(Path.GetExtension(viewContent.PrimaryFileName?.ToString()), ".xaml", StringComparison.OrdinalIgnoreCase)
            || viewContent.GetService(typeof(ITextEditor)) is not ITextEditor editor)
            return false;
        editor.Document.Insert(editor.Caret.Offset, snippet);
        if (viewContent is EditorViewContent editorContent)
            editorContent.MarkDirty();
        return true;
    }

    private void CloseAllWorkbenchViews()
    {
        CancelAllDiagnosticsRefreshes();
        _openFileViews.Clear();
        _documents.Clear();
        DocumentPane.Children.Clear();
    }

    private void OnEditorTextChanged(EditorViewContent editorContent)
    {
        SyncLanguageServiceDocument(editorContent);
        ScheduleDiagnosticsRefresh(editorContent);

        if (editorContent.IsDirty)
        {
            return;
        }

        editorContent.MarkDirty();
        UpdateDocumentTitle(editorContent);
    }

    private void ScheduleDiagnosticsRefresh(EditorViewContent editorContent)
    {
        if (string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        CancelDiagnosticsRefresh(editorContent.FilePath);

        var cancellation = new CancellationTokenSource();
        _diagnosticsCancellations[editorContent.FilePath] = cancellation;
        var cancellationToken = cancellation.Token;
        _ = RefreshDiagnosticsAfterDelayAsync(editorContent, cancellationToken);
    }

    private async Task RefreshDiagnosticsAfterDelayAsync(EditorViewContent editorContent, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(600, cancellationToken);
            await RefreshDiagnosticsAsync(editorContent, cancellationToken);
            await RefreshNavigationBarAsync(editorContent);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Language service diagnostics refresh failed for {editorContent.FilePath}: {ex.Message}");
        }
        finally
        {
            if (!string.IsNullOrEmpty(editorContent.FilePath)
                && _diagnosticsCancellations.TryGetValue(editorContent.FilePath, out var cancellation)
                && cancellation.Token == cancellationToken)
            {
                _diagnosticsCancellations.Remove(editorContent.FilePath);
                cancellation.Dispose();
            }
        }
    }

    private void CancelAllDiagnosticsRefreshes()
    {
        foreach (var cancellation in _diagnosticsCancellations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _diagnosticsCancellations.Clear();
    }

    private void CancelDiagnosticsRefresh(string filePath)
    {
        if (_diagnosticsCancellations.Remove(filePath, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
    }

    private async Task RefreshDiagnosticsAsync(EditorViewContent editorContent, CancellationToken cancellationToken)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
        var diagnostics = await languageService.GetDiagnosticsAsync(new DocumentId(editorContent.FilePath), cancellationToken);
        var taskService = ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService;
        taskService?.ReplaceLanguageDiagnostics(editorContent.FilePath, diagnostics);

        if (diagnostics.Count > 0)
        {
            ShowErrorsPad();
            SetStatusBarMessage($"{diagnostics.Count} diagnostic(s).", highlighted: true);
        }
        else
        {
            SetStatusBarMessage("No diagnostics.", highlighted: false);
        }
    }

    /// <summary>
    /// Lays out the editor's navigation bar: type dropdown, member dropdown, and (right-aligned)
    /// the "active target framework" selector for multi-targeted projects.
    /// </summary>
    private static Grid BuildNavigationBar(EditorViewContent editorContent)
    {
        var bar = new Grid();
        // Type/member dropdowns split the available width between them (VS's classic nav bar
        // ratio) instead of an Auto width that shrinks each to its own content and leaves the
        // remainder as a dead gap before the right-aligned framework selector.
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        editorContent.TypeComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        editorContent.MemberComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;

        Grid.SetColumn(editorContent.TypeComboBox, 0);
        Grid.SetColumn(editorContent.MemberComboBox, 1);
        Grid.SetColumn(editorContent.TargetFrameworkComboBox, 2);
        bar.Children.Add(editorContent.TypeComboBox);
        bar.Children.Add(editorContent.MemberComboBox);
        bar.Children.Add(editorContent.TargetFrameworkComboBox);
        return bar;
    }

    /// <summary>
    /// Repopulates the navigation bar for this editor: the target-framework selector (only
    /// shown for multi-targeted projects) and the type/member outline (docs/language-services.md
    /// §4 slices 1-3/5-6, <see cref="ILanguageService.GetDocumentOutlineAsync"/>).
    /// </summary>
    private async Task RefreshNavigationBarAsync(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            editorContent.TypeComboBox.Visibility = Visibility.Collapsed;
            editorContent.MemberComboBox.Visibility = Visibility.Collapsed;
            editorContent.TargetFrameworkComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        RefreshTargetFrameworkSelector(editorContent, languageService);

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
        var outline = await languageService.GetDocumentOutlineAsync(new DocumentId(editorContent.FilePath), CancellationToken.None);
        if (outline.Count == 0)
        {
            editorContent.TypeComboBox.Visibility = Visibility.Collapsed;
            editorContent.MemberComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        var previouslySelectedName = (editorContent.TypeComboBox.SelectedItem as EditorViewContent.OutlineComboItem)?.Name;
        var items = outline.Select(EditorViewContent.OutlineComboItem.Create).ToArray();
        editorContent.TypeComboBox.ItemsSource = items;
        editorContent.TypeComboBox.SelectedItem = items.FirstOrDefault(item => item.Name == previouslySelectedName) ?? items[0];
        editorContent.TypeComboBox.Visibility = Visibility.Visible;
    }

    private static void RefreshTargetFrameworkSelector(EditorViewContent editorContent, ICSharpCode.SharpDevelop.LanguageServices.ILanguageService languageService)
    {
        if (languageService is not ICSharpCode.SharpDevelop.LanguageServices.Roslyn.CSharpVBLanguageService roslynService
            || editorContent.FilePath is null)
        {
            editorContent.TargetFrameworkComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        var project = SD.ProjectService.FindProjectContainingFile(ICSharpCode.Core.FileName.Create(editorContent.FilePath));
        var projectFileName = project?.FileName.ToString();
        var targetFrameworks = projectFileName is not null ? roslynService.GetTargetFrameworks(projectFileName) : Array.Empty<string>();
        if (targetFrameworks.Count <= 1)
        {
            editorContent.TargetFrameworkComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        editorContent.TargetFrameworkComboBox.ItemsSource = targetFrameworks;
        editorContent.TargetFrameworkComboBox.SelectedItem = roslynService.GetActiveTargetFramework(projectFileName!) ?? targetFrameworks[0];
        editorContent.TargetFrameworkComboBox.Visibility = Visibility.Visible;
    }

    private void OnTargetFrameworkSelectionChanged(EditorViewContent editorContent)
    {
        if (editorContent.TargetFrameworkComboBox.SelectedItem is not string targetFramework || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        if (_languageServiceRegistry?.GetService(editorContent.FilePath) is not ICSharpCode.SharpDevelop.LanguageServices.Roslyn.CSharpVBLanguageService roslynService)
        {
            return;
        }

        var project = SD.ProjectService.FindProjectContainingFile(ICSharpCode.Core.FileName.Create(editorContent.FilePath));
        if (project is null)
        {
            return;
        }

        roslynService.SetActiveTargetFramework(project.FileName.ToString(), targetFramework);
        ScheduleDiagnosticsRefresh(editorContent);
    }

    private static void OnOutlineTypeSelectionChanged(EditorViewContent editorContent)
    {
        if (editorContent.TypeComboBox.SelectedItem is not EditorViewContent.OutlineComboItem type)
        {
            editorContent.MemberComboBox.Visibility = Visibility.Collapsed;
            return;
        }

        PopulateMemberComboBox(editorContent, type.Node);

        if (!editorContent.IsSyncingOutlineSelectionFromCaret)
        {
            NavigateToOutlineNode(editorContent, type.Node);
        }
    }

    /// <summary>Fills MemberComboBox with <paramref name="typeNode"/>'s children (or hides it if there are none).</summary>
    private static void PopulateMemberComboBox(EditorViewContent editorContent, DocumentOutlineNode typeNode)
    {
        if (typeNode.Children.Count == 0)
        {
            editorContent.MemberComboBox.ItemsSource = null;
            editorContent.MemberComboBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            editorContent.MemberComboBox.ItemsSource = typeNode.Children.Select(EditorViewContent.OutlineComboItem.Create).ToArray();
            editorContent.MemberComboBox.SelectedIndex = -1;
            editorContent.MemberComboBox.Visibility = Visibility.Visible;
        }
    }

    private static void OnOutlineMemberSelectionChanged(EditorViewContent editorContent)
    {
        if (editorContent.MemberComboBox.SelectedItem is EditorViewContent.OutlineComboItem member
            && !editorContent.IsSyncingOutlineSelectionFromCaret)
        {
            NavigateToOutlineNode(editorContent, member.Node);
        }
    }

    /// <summary>
    /// Auto-selects the type/member the caret currently sits inside (VS-style nav bar behavior),
    /// without moving the caret itself — the reverse of clicking a dropdown entry.
    /// </summary>
    private static void SyncNavigationBarSelectionToCaret(EditorViewContent editorContent)
    {
        if (editorContent.TypeComboBox.ItemsSource is not IEnumerable<EditorViewContent.OutlineComboItem> types)
        {
            return;
        }

        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        var offset = Math.Clamp(editorContent.Editor.CurrentOffset, 0, document.TextLength);
        var location = document.GetLocation(offset);

        editorContent.IsSyncingOutlineSelectionFromCaret = true;
        try
        {
            var containingType = FindContainingOutlineItem(types, location.Line, location.Column);
            if (containingType is null)
            {
                return;
            }

            if (!ReferenceEquals(editorContent.TypeComboBox.SelectedItem, containingType))
            {
                editorContent.TypeComboBox.SelectedItem = containingType;

                // Repopulate MemberComboBox directly instead of relying on the SelectedItem
                // change above to raise SelectionChanged -> OnOutlineTypeSelectionChanged first:
                // that dispatch isn't guaranteed to run synchronously on every Uno target, and
                // the member lookup right below needs the *new* type's children immediately, not
                // on the next message-loop turn - otherwise, moving the caret from one type into
                // another (e.g. scrolling past a class boundary) would highlight a member of the
                // *previous* type for one caret move.
                PopulateMemberComboBox(editorContent, containingType.Node);
            }

            if (editorContent.MemberComboBox.ItemsSource is IEnumerable<EditorViewContent.OutlineComboItem> members)
            {
                var containingMember = FindContainingOutlineItem(members, location.Line, location.Column);
                if (containingMember is not null && !ReferenceEquals(editorContent.MemberComboBox.SelectedItem, containingMember))
                {
                    editorContent.MemberComboBox.SelectedItem = containingMember;
                }
            }
        }
        finally
        {
            editorContent.IsSyncingOutlineSelectionFromCaret = false;
        }
    }

    /// <summary>
    /// Finds the innermost item whose <see cref="DocumentOutlineNode.ExtentSpan"/> contains
    /// (line, column), i.e. the smallest containing span rather than "the last one in source
    /// order that contains it" — the latter only happens to work for non-overlapping siblings and
    /// picks arbitrarily when two items' extents touch at a shared boundary or (for a language
    /// service that ever reports nested spans at the same combo level) one extent nests inside
    /// another.
    /// </summary>
    internal static EditorViewContent.OutlineComboItem? FindContainingOutlineItem(
        IEnumerable<EditorViewContent.OutlineComboItem> items, int line, int column)
    {
        EditorViewContent.OutlineComboItem? best = null;
        var bestSize = (int.MaxValue, int.MaxValue);
        foreach (var item in items)
        {
            var extent = item.Node.ExtentSpan;
            if (!IsWithinExtent(extent, line, column))
            {
                continue;
            }

            var size = ExtentSize(extent);
            if (size.CompareTo(bestSize) < 0)
            {
                best = item;
                bestSize = size;
            }
        }

        return best;
    }

    // (lineSpan, columnSpanOnLastLine) - compared lexicographically, so a single-line extent
    // (lineSpan 0) always beats a multi-line one regardless of column width, and among
    // same-lineSpan extents the narrower one wins. A tuple comparison is enough to pick the
    // innermost of two nested/overlapping spans without needing an exact character count.
    private static (int, int) ExtentSize(TextSpan extent)
    {
        var lineSpan = extent.End.Line - extent.Start.Line;
        var columnSpan = lineSpan == 0 ? extent.End.Column - extent.Start.Column : extent.End.Column;
        return (lineSpan, columnSpan);
    }

    internal static bool IsWithinExtent(TextSpan extent, int line, int column)
    {
        var afterStart = line > extent.Start.Line || (line == extent.Start.Line && column >= extent.Start.Column);
        var beforeEnd = line < extent.End.Line || (line == extent.End.Line && column <= extent.End.Column);
        return afterStart && beforeEnd;
    }

    private static void NavigateToOutlineNode(EditorViewContent editorContent, DocumentOutlineNode node)
    {
        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        var offset = OffsetFromLineColumn(document.Text, node.Span.Start.Line, node.Span.Start.Column);
        editorContent.Editor.CurrentOffset = Math.Clamp(offset, 0, document.TextLength);
    }

    private static int OffsetFromLineColumn(string text, int line, int column)
    {
        var currentLine = 1;
        var currentColumn = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (currentLine == line && currentColumn == column)
            {
                return i;
            }

            if (text[i] == '\n')
            {
                currentLine++;
                currentColumn = 1;
            }
            else
            {
                currentColumn++;
            }
        }

        return text.Length;
    }

    private void SyncLanguageServiceDocument(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        _ = SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
    }

    private static async Task SyncLanguageServiceDocumentAsync(
        ICSharpCode.SharpDevelop.LanguageServices.ILanguageService languageService,
        string filePath,
        string text)
    {
        try
        {
            if (languageService is ICSharpCode.SharpDevelop.LanguageServices.Roslyn.CSharpVBLanguageService roslynService)
            {
                await roslynService.UpsertDocumentAsync(new DocumentId(filePath), text, CancellationToken.None);
            }
            else if (languageService is ICSharpCode.SharpDevelop.LanguageServices.Lsp.LspLanguageService lspService)
            {
                await lspService.UpsertDocumentAsync(new DocumentId(filePath), text, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Language service document sync failed for {filePath}: {ex.Message}");
        }
    }

    private void OnEditorKeyDown(EditorViewContent editorContent, KeyRoutedEventArgs args)
    {
        if (_completionWindow is { IsOpen: true })
        {
            if (args.Key == VirtualKey.Escape)
            {
                _completionWindow.Close();
                _completionWindow = null;
                args.Handled = true;
                return;
            }

            _completionWindow.CompletionList.HandleKey(args);
            if (args.Handled)
            {
                return;
            }

            if (args.Key == VirtualKey.Back)
            {
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateCompletionFilter(editorContent));
            }
        }

        if (args.Key == VirtualKey.Space && IsControlPressed())
        {
            _ = ShowCompletionAsync(editorContent);
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.F12)
        {
            _ = GoToDefinitionAsync(editorContent);
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.F2)
        {
            _ = RenameSymbolAsync(editorContent);
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Enter && IsControlPressed())
        {
            // Ctrl+. (VS/Rider's usual "show code actions" binding) isn't available: this Uno
            // version's VirtualKey enum has no OEM/punctuation key members at all (only the
            // letter/number/function-key/navigation set), so Ctrl+Enter is used instead.
            _ = ShowCodeActionsAsync(editorContent);
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.K && IsControlPressed())
        {
            _ = FormatDocumentAsync(editorContent);
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.I && IsControlPressed())
        {
            _ = ShowQuickInfoAsync(editorContent);
            args.Handled = true;
            return;
        }

        if (args.Key != VirtualKey.S || !IsControlPressed())
        {
            return;
        }

        SaveEditorDocument(editorContent, notifyOnSuccess: true);
        args.Handled = true;
    }

    private void OnEditorTextEntered(EditorViewContent editorContent, TextCompositionEventArgs args)
    {
        if (args.Text == ".")
        {
            _ = ShowCompletionAsync(editorContent);
            return;
        }

        if (_completionWindow is { IsOpen: true } && args.Text.Length > 0 && (char.IsLetterOrDigit(args.Text[0]) || args.Text[0] == '_'))
        {
            UpdateCompletionFilter(editorContent);
        }
    }

    private void OnEditorTextEntering(TextCompositionEventArgs args)
    {
        if (_completionWindow is null || args.Text.Length == 0)
        {
            return;
        }

        if (!char.IsLetterOrDigit(args.Text[0]) && args.Text[0] != '_')
        {
            _completionWindow.Close();
            _completionWindow = null;
        }
    }

    private void UpdateCompletionFilter(EditorViewContent editorContent)
    {
        if (_completionWindow is not { IsOpen: true } || editorContent.Editor.Document is null)
        {
            return;
        }

        var start = Math.Clamp(_completionWindow.StartOffset, 0, editorContent.Editor.Document.TextLength);
        var end = Math.Clamp(editorContent.Editor.CurrentOffset, start, editorContent.Editor.Document.TextLength);
        _completionWindow.CompletionList.SelectItem(editorContent.Editor.Document.Text.Substring(start, end - start));
    }

    private async Task ShowCompletionAsync(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var textArea = editorContent.Editor.TextArea;
        var document = editorContent.Editor.Document;
        if (textArea is null || document is null)
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
        var offset = Math.Clamp(editorContent.Editor.CurrentOffset, 0, document.TextLength);
        var completions = await languageService.GetCompletionsAsync(new DocumentId(editorContent.FilePath), offset, CancellationToken.None);
        if (completions.Items.Count == 0)
        {
            return;
        }

        var startOffset = GetCompletionStartOffset(document.Text, offset);
        _completionWindow?.Close();
        var completionWindow = new CompletionWindow(textArea)
        {
            StartOffset = startOffset,
            EndOffset = offset
        };
        _completionWindow = completionWindow;

        foreach (var item in completions.Items.OrderBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase).Take(200))
        {
            completionWindow.CompletionList.CompletionData.Add(new LanguageServiceCompletionData(item));
        }

        UpdateCompletionFilter(editorContent);
        completionWindow.CompletionList.InsertionRequested += (_, eventArgs) =>
        {
            var selected = completionWindow.CompletionList.SelectedItem;
            if (selected is null)
            {
                return;
            }

            var completionOffset = Math.Clamp(completionWindow.StartOffset, 0, document.TextLength);
            var completionEnd = Math.Clamp(editorContent.Editor.CurrentOffset, completionOffset, document.TextLength);
            selected.Complete(textArea, new CompletionSegment(completionOffset, completionEnd - completionOffset), eventArgs);
            editorContent.Editor.CurrentOffset = completionOffset + selected.Text.Length;
            SyncLanguageServiceDocument(editorContent);
            completionWindow.Close();
        };
        completionWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_completionWindow, completionWindow))
            {
                _completionWindow = null;
            }
        };
        completionWindow.Show();
    }

    private async Task GoToDefinitionAsync(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
        var offset = Math.Clamp(editorContent.Editor.CurrentOffset, 0, document.TextLength);
        var targets = await languageService.GoToDefinitionAsync(new DocumentId(editorContent.FilePath), offset, CancellationToken.None);
        var target = targets.FirstOrDefault(target => File.Exists(target.FileName));
        if (target is null)
        {
            return;
        }

        OpenFileInWorkbench(target.FileName);
        if (!_openFileViews.TryGetValue(target.FileName, out var view) || view is not EditorViewContent targetView)
        {
            return;
        }

        MoveEditorToPosition(targetView, target.Position);
    }

    private async Task FormatDocumentAsync(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
        var edits = await languageService.FormatAsync(new DocumentId(editorContent.FilePath), null, CancellationToken.None);
        if (edits.Count == 0)
        {
            return;
        }

        ApplyTextEdits(document, edits);
        SyncLanguageServiceDocument(editorContent);
    }

    /// <summary>
    /// F2 rename (docs/language-services.md §2.3/§3.3 "deliberately out of scope" note,
    /// implemented here): prompts for a new name, asks the language service to compute edits
    /// across every file that references the symbol, then applies them — to open editors via
    /// their live <see cref="TextDocument"/>, and directly to disk for files that aren't open.
    /// </summary>
    private async Task RenameSymbolAsync(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
        var offset = Math.Clamp(editorContent.Editor.CurrentOffset, 0, document.TextLength);
        var currentName = GetIdentifierAtOffset(document.Text, offset);

        var newName = await PromptForRenameAsync(currentName);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentName, StringComparison.Ordinal))
        {
            return;
        }

        IReadOnlyDictionary<string, IReadOnlyList<TextEdit>> editsByFile;
        try
        {
            editsByFile = await languageService.RenameSymbolAsync(new DocumentId(editorContent.FilePath), offset, newName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Rename failed for {editorContent.FilePath}: {ex.Message}");
            SetStatusBarMessage("Rename failed.", highlighted: true);
            return;
        }

        if (editsByFile.Count == 0)
        {
            SetStatusBarMessage("Nothing to rename at the caret.", highlighted: false);
            return;
        }

        var changedFileCount = await ApplyEditsAcrossFilesAsync(editsByFile, "Rename");
        SetStatusBarMessage($"Renamed '{currentName}' to '{newName}' in {changedFileCount} file(s).", highlighted: true);
    }

    /// <summary>
    /// Applies a per-file edit map (as returned by <see cref="ILanguageService.RenameSymbolAsync"/>
    /// or <see cref="ILanguageService.ApplyCodeActionAsync"/>, docs/language-services.md §8.4) —
    /// to open editors via their live <see cref="TextDocument"/>, and directly to disk for files
    /// that aren't open. Returns the number of files actually changed.
    /// </summary>
    private async Task<int> ApplyEditsAcrossFilesAsync(IReadOnlyDictionary<string, IReadOnlyList<TextEdit>> editsByFile, string operationName)
    {
        var changedFileCount = 0;
        foreach (var (filePath, edits) in editsByFile)
        {
            if (edits.Count == 0)
            {
                continue;
            }

            if (_openFileViews.TryGetValue(filePath, out var openViewContent) && openViewContent is EditorViewContent openView)
            {
                var openDocument = openView.Editor.Document;
                if (openDocument is null)
                {
                    continue;
                }

                ApplyTextEdits(openDocument, edits);
                SyncLanguageServiceDocument(openView);
                if (!openView.IsDirty)
                {
                    openView.MarkDirty();
                    UpdateDocumentTitle(openView);
                }

                ScheduleDiagnosticsRefresh(openView);
            }
            else
            {
                try
                {
                    var text = await File.ReadAllTextAsync(filePath);
                    await File.WriteAllTextAsync(filePath, ApplyTextEditsToText(text, edits));
                }
                catch (Exception ex)
                {
                    LoggingService.Warn($"{operationName} failed to update {filePath}: {ex.Message}");
                    continue;
                }
            }

            changedFileCount++;
        }

        return changedFileCount;
    }

    /// <summary>
    /// Ctrl+. code actions menu (docs/language-services.md §8.4): lists the actions applicable
    /// at the caret (or over the current selection, if any) and applies whichever one is picked
    /// through the same multi-file edit path F2 rename uses.
    /// </summary>
    private async Task ShowCodeActionsAsync(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);

        var selectionStart = editorContent.Editor.SelectionStart;
        var selectionLength = editorContent.Editor.SelectionLength;
        var startOffset = Math.Clamp(selectionLength > 0 ? selectionStart : editorContent.Editor.CurrentOffset, 0, document.TextLength);
        var endOffset = Math.Clamp(selectionLength > 0 ? selectionStart + selectionLength : startOffset, startOffset, document.TextLength);
        var startLocation = document.GetLocation(startOffset);
        var endLocation = document.GetLocation(endOffset);
        var span = new TextSpan(new TextPosition(startLocation.Line, startLocation.Column), new TextPosition(endLocation.Line, endLocation.Column));

        var documentId = new DocumentId(editorContent.FilePath);
        IReadOnlyList<CodeActionInfo> actions;
        try
        {
            actions = await languageService.GetCodeActionsAsync(documentId, span, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Code actions failed for {editorContent.FilePath}: {ex.Message}");
            SetStatusBarMessage("Code actions failed.", highlighted: true);
            return;
        }

        if (actions.Count == 0)
        {
            SetStatusBarMessage("No code actions available.", highlighted: false);
            return;
        }

        var menu = new MenuFlyout();
        foreach (var action in actions)
        {
            var item = new MenuFlyoutItem { Text = action.Title };
            item.Click += (_, _) => _ = ApplyCodeActionAsync(editorContent, languageService, documentId, action);
            menu.Items.Add(item);
        }

        menu.ShowAt(editorContent.Editor);
    }

    private async Task ApplyCodeActionAsync(
        EditorViewContent editorContent, ICSharpCode.SharpDevelop.LanguageServices.ILanguageService languageService, DocumentId documentId, CodeActionInfo action)
    {
        IReadOnlyDictionary<string, IReadOnlyList<TextEdit>> editsByFile;
        try
        {
            editsByFile = await languageService.ApplyCodeActionAsync(documentId, action.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Applying code action '{action.Title}' failed for {editorContent.FilePath}: {ex.Message}");
            SetStatusBarMessage("Code action failed.", highlighted: true);
            return;
        }

        if (editsByFile.Count == 0)
        {
            SetStatusBarMessage("Code action produced no changes.", highlighted: false);
            return;
        }

        var changedFileCount = await ApplyEditsAcrossFilesAsync(editsByFile, "Code action");
        SetStatusBarMessage($"Applied '{action.Title}' to {changedFileCount} file(s).", highlighted: true);
    }

    private static async Task<string?> PromptForRenameAsync(string? currentName)
    {
        var textBox = new TextBox { Text = currentName ?? string.Empty };
        textBox.SelectAll();

        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = textBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = MainPage.Current?.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private static string? GetIdentifierAtOffset(string text, int offset)
    {
        if (text.Length == 0)
        {
            return null;
        }

        var position = Math.Clamp(offset, 0, text.Length - 1);
        // If the caret sits right after the identifier ("Foo|"), step back onto it.
        if (position > 0 && !IsIdentifierChar(text[position]) && IsIdentifierChar(text[position - 1]))
        {
            position--;
        }

        if (!IsIdentifierChar(text[position]))
        {
            return null;
        }

        var start = position;
        while (start > 0 && IsIdentifierChar(text[start - 1]))
        {
            start--;
        }

        var end = position;
        while (end < text.Length - 1 && IsIdentifierChar(text[end + 1]))
        {
            end++;
        }

        return text.Substring(start, end - start + 1);
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string ApplyTextEditsToText(string text, IReadOnlyList<TextEdit> edits)
    {
        var builder = new System.Text.StringBuilder(text);
        foreach (var edit in edits
            .Select(edit => new
            {
                Edit = edit,
                StartOffset = OffsetFromLineColumn(text, edit.Span.Start.Line, edit.Span.Start.Column),
                EndOffset = OffsetFromLineColumn(text, edit.Span.End.Line, edit.Span.End.Column)
            })
            .OrderByDescending(item => item.StartOffset))
        {
            var startOffset = Math.Clamp(edit.StartOffset, 0, builder.Length);
            var endOffset = Math.Clamp(edit.EndOffset, startOffset, builder.Length);
            builder.Remove(startOffset, endOffset - startOffset);
            builder.Insert(startOffset, edit.Edit.NewText);
        }

        return builder.ToString();
    }

    private async Task ShowQuickInfoAsync(EditorViewContent editorContent)
    {
        if (_languageServiceRegistry is null || string.IsNullOrEmpty(editorContent.FilePath))
        {
            return;
        }

        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        var languageService = _languageServiceRegistry.GetService(editorContent.FilePath);
        if (ReferenceEquals(languageService, _languageServiceRegistry.FallbackService))
        {
            return;
        }

        await SyncLanguageServiceDocumentAsync(languageService, editorContent.FilePath, editorContent.Editor.Text);
        var offset = Math.Clamp(editorContent.Editor.CurrentOffset, 0, document.TextLength);
        var quickInfo = await languageService.GetQuickInfoAsync(new DocumentId(editorContent.FilePath), offset, CancellationToken.None);
        if (quickInfo is null || string.IsNullOrWhiteSpace(quickInfo.Text))
        {
            return;
        }

        SetStatusBarMessage(quickInfo.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? quickInfo.Text, highlighted: true);
    }

    private void MoveEditorToPosition(EditorViewContent editorContent, TextPosition position)
    {
        var document = editorContent.Editor.Document;
        if (document is null)
        {
            return;
        }

        try
        {
            var line = Math.Clamp(position.Line, 1, document.LineCount);
            var documentLine = document.GetLineByNumber(line);
            var column = Math.Clamp(position.Column, 1, documentLine.Length + 1);
            var offset = documentLine.Offset + column - 1;
            editorContent.Editor.CurrentOffset = offset;
            editorContent.Editor.Select(offset, 0);
            editorContent.Editor.ScrollToLine(line);
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Navigation failed for {editorContent.FilePath}: {ex.Message}");
        }

        if (_documents.TryGetValue(editorContent, out var layoutDocument))
        {
            layoutDocument.IsSelected = true;
            layoutDocument.IsActive = true;
        }
    }

    private static void ApplyTextEdits(TextDocument document, IReadOnlyList<TextEdit> edits)
    {
        foreach (var edit in edits
            .Select(edit => new { Edit = edit, Offset = GetOffset(document, edit.Span.Start) })
            .OrderByDescending(item => item.Offset))
        {
            var startOffset = Math.Clamp(edit.Offset, 0, document.TextLength);
            var endOffset = Math.Clamp(GetOffset(document, edit.Edit.Span.End), startOffset, document.TextLength);
            document.Replace(startOffset, endOffset - startOffset, edit.Edit.NewText);
        }
    }

    private static int GetOffset(TextDocument document, TextPosition position)
    {
        var line = Math.Clamp(position.Line, 1, document.LineCount);
        var documentLine = document.GetLineByNumber(line);
        var column = Math.Clamp(position.Column, 1, documentLine.Length + 1);
        return documentLine.Offset + column - 1;
    }

    private static int GetCompletionStartOffset(string text, int offset)
    {
        var start = Math.Clamp(offset, 0, text.Length);
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        return start;
    }

    private sealed class CompletionSegment : ISegment
    {
        public CompletionSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }
        public int Length { get; }
        public int EndOffset => Offset + Length;
    }

    private sealed class LanguageServiceCompletionData : ICompletionData
    {
        private readonly CompletionItem _item;

        public LanguageServiceCompletionData(CompletionItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
        }

        public System.Windows.Media.ImageSource? Image => null;
        public string Text => _item.InsertionText;
        public object Content => _item.DisplayText;
        public object Description => _item.Description ?? _item.DisplayText;
        public double Priority => 0;

        public void Complete(object textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            if (textArea is not TextArea area || area.Document is null)
            {
                return;
            }

            var offset = Math.Clamp(completionSegment.Offset, 0, area.Document.TextLength);
            var length = Math.Clamp(completionSegment.Length, 0, area.Document.TextLength - offset);
            area.Document.Replace(offset, length, _item.InsertionText);
        }
    }

    private bool TryCloseEditorDocument(EditorViewContent editorContent)
    {
        if (!editorContent.IsDirty)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(editorContent.FilePath))
        {
            return SaveEditorDocument(editorContent, notifyOnSuccess: false);
        }

        ServiceSingleton.GetRequiredService<IMessageService>().ShowWarning("Closing an unsaved temporary document.");
        return true;
    }

    private bool TryCloseViewContent(IViewContent viewContent)
    {
        if (!viewContent.IsDirty)
        {
            return true;
        }

        if (viewContent is EditorViewContent editorContent)
        {
            return TryCloseEditorDocument(editorContent);
        }

        return SaveViewContent(viewContent, notifyOnSuccess: false);
    }

    private bool SaveEditorDocument(EditorViewContent editorContent, bool notifyOnSuccess)
    {
        if (string.IsNullOrEmpty(editorContent.FilePath))
        {
            ServiceSingleton.GetRequiredService<IMessageService>().ShowWarning("This document does not have a file path yet.");
            return false;
        }

        try
        {
            editorContent.Editor.Save(editorContent.FilePath);
            editorContent.MarkClean();
            UpdateDocumentTitle(editorContent);
            if (notifyOnSuccess)
            {
                ServiceSingleton.GetRequiredService<IMessageService>().ShowMessage($"Saved: {editorContent.FilePath}", "UnoDevelop");
            }

            return true;
        }
        catch (Exception ex)
        {
            ServiceSingleton.GetRequiredService<IMessageService>().ShowException(ex, $"Failed to save {editorContent.FilePath}");
            return false;
        }
    }

    private bool SaveViewContent(IViewContent viewContent, bool notifyOnSuccess)
    {
        var fileName = viewContent.PrimaryFileName?.ToString();
        if (string.IsNullOrEmpty(fileName) || viewContent.PrimaryFile is null)
        {
            ServiceSingleton.GetRequiredService<IMessageService>().ShowWarning("This document does not have a file path yet.");
            return false;
        }

        try
        {
            using var stream = File.Create(fileName);
            viewContent.Save(viewContent.PrimaryFile, stream);
            UpdateDocumentTitle(viewContent);
            if (notifyOnSuccess)
            {
                ServiceSingleton.GetRequiredService<IMessageService>().ShowMessage($"Saved: {fileName}", "UnoDevelop");
            }

            return true;
        }
        catch (Exception ex)
        {
            ServiceSingleton.GetRequiredService<IMessageService>().ShowException(ex, $"Failed to save {fileName}");
            return false;
        }
    }

    private void UpdateDocumentTitle(IViewContent editorContent)
    {
        if (_documents.TryGetValue(editorContent, out var document))
        {
            document.Title = editorContent.IsDirty ? editorContent.TabPageText + "*" : editorContent.TabPageText;
        }
    }

    private static bool IsControlPressed()
    {
        var window = CoreWindow.GetForCurrentThread();
        if (window is null)
        {
            return false;
        }

        return window.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)
            || window.GetKeyState(VirtualKey.LeftControl).HasFlag(CoreVirtualKeyStates.Down);
    }

    private static string BuildDocumentContentId(IViewContent viewContent)
    {
        var primaryFile = viewContent.PrimaryFileName;
        if (primaryFile is not null)
        {
            return "doc-" + primaryFile.ToString();
        }

        return "doc-" + viewContent.TitleName;
    }

    // internal (not private) so UnoDevelop.Core.Tests can construct OutlineComboItem/
    // DocumentOutlineNode fixtures to unit-test FindContainingOutlineItem/IsWithinExtent
    // directly, without needing a live editor/window.
    internal sealed class EditorViewContent : IViewContent
    {
        private readonly List<OpenedFile> _files = new();
        private readonly AvalonEditTextEditorAdapter _textEditorAdapter;
        private bool _isDirty;

        public EditorViewContent(string title, string text, string? filePath)
        {
            TabPageText = title;
            TitleName = title;
            InfoTip = title;
            FilePath = filePath;

            Editor = new TextEditor
            {
                Theme = TextEditorTheme.Light,
                Text = text,
                Tag = filePath
            };
            ApplySyntaxHighlighting(filePath ?? title);
            _textEditorAdapter = new AvalonEditTextEditorAdapter(Editor);
            ConfigureCodeEditor(Editor);

            if (Editor.TextArea?.TextView is { } tv)
            {
                tv.BreakpointsChanged += OnTvBreakpointsChanged;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                _files.Add(new DocumentOpenedFile(filePath));
            }

            TypeComboBox = new ComboBox
            {
                ItemTemplate = OutlineComboItemTemplate,
                MinWidth = 140,
                Margin = new Thickness(4, 2, 0, 2),
                Visibility = Visibility.Collapsed
            };
            MemberComboBox = new ComboBox
            {
                ItemTemplate = OutlineComboItemTemplate,
                MinWidth = 140,
                Margin = new Thickness(4, 2, 0, 2),
                Visibility = Visibility.Collapsed
            };
            TargetFrameworkComboBox = new ComboBox
            {
                MinWidth = 100,
                Margin = new Thickness(4, 2, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                Visibility = Visibility.Collapsed
            };
        }

        public string? FilePath { get; private set; }

        /// <summary>Left dropdown of the editor's navigation bar: types declared in this document.</summary>
        public ComboBox TypeComboBox { get; }

        /// <summary>Second dropdown of the navigation bar: members of the selected type.</summary>
        public ComboBox MemberComboBox { get; }

        /// <summary>
        /// Right-aligned "active target framework" selector, shown only when the owning project
        /// is multi-targeted (docs/language-services.md §4 slice 4).
        /// </summary>
        public ComboBox TargetFrameworkComboBox { get; }

        /// <summary>
        /// True while the nav bar's Type/Member selection is being updated to follow the caret
        /// (<see cref="MainPage.SyncNavigationBarSelectionToCaret"/>) — suppresses the selection
        /// handlers' normal "navigate the caret to this node" behavior, which would otherwise
        /// fight the caret move that triggered the sync in the first place.
        /// </summary>
        public bool IsSyncingOutlineSelectionFromCaret { get; set; }

        static readonly DataTemplate OutlineComboItemTemplate = new(() =>
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var icon = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            icon.SetBinding(Image.SourceProperty, new Binding { Path = new PropertyPath("IconUri") });
            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath("Name") });
            stack.Children.Add(icon);
            stack.Children.Add(text);
            return stack;
        });

        /// <summary>
        /// Wraps a <see cref="DocumentOutlineNode"/> with the icon URI for its <see cref="DocumentOutlineNode.Kind"/>
        /// so the navigation bar's dropdowns (icon + text per row, matching SharpDevelop's
        /// ClassBrowserIcons-style class/member glyphs) can bind against it directly.
        /// </summary>
        public sealed record OutlineComboItem(DocumentOutlineNode Node, string IconUri)
        {
            public string Name => Node.Name;

            public static OutlineComboItem Create(DocumentOutlineNode node) => new(node, ResolveIconUri(node.Kind, node.Accessibility));

            /// <summary>
            /// Base icon name per symbol kind (matches the VS-style SVGs in
            /// src/Main/SharpDevelop/Icons/, e.g. "Class" -> Class_16x.svg / ClassPrivate_16x.svg).
            /// </summary>
            static string? ResolveIconBaseName(string kind) => kind switch
            {
                "Class" => "Class",
                "Struct" => "Structure",
                "Interface" => "Interface",
                "Enum" => "Enum",
                "Method" or "Constructor" or "Function" => "Method",
                "Property" or "Indexer" => "Property",
                "Field" or "Variable" or "Constant" => "Field",
                "Event" => "Event",
                _ => null
            };

            static string ResolveIconUri(string kind, string? accessibility)
            {
                var baseName = ResolveIconBaseName(kind) ?? "Class";

                // Public (or unknown/LSP-reported-nothing) uses the plain glyph; Roslyn-reported
                // non-public accessibility gets the matching modifier-badged variant. Enum's own
                // "no modifier" glyph is named "Enumerator", not "Enum" (VS Image Library quirk —
                // the "Enum" folder only ships modifier-badged variants).
                var suffix = accessibility switch
                {
                    "Private" => "Private",
                    "Protected" => "Protected",
                    "Internal" => "Internal",
                    _ => baseName == "Enum" ? "erator" : string.Empty
                };

                return $"ms-appx:///Icons/{baseName}{suffix}_16x.svg";
            }
        }

        /// <summary>Raised when breakpoints change for this view's document.</summary>
        public event Action<string, IReadOnlyList<int>>? BreakpointsChanged;

        /// <summary>Notifies the outer MainPage when the TextView's breakpoints change.</summary>
        private void OnTvBreakpointsChanged(IReadOnlyList<int> lines)
        {
            BreakpointsChanged?.Invoke(FilePath ?? string.Empty, lines);
        }

        public void Retarget(string newPath)
        {
            FilePath = newPath;
            TabPageText = Path.GetFileName(newPath);
            TitleName = TabPageText;
            Editor.Tag = newPath;
            ApplySyntaxHighlighting(newPath);
            TabPageTextChanged?.Invoke(this, EventArgs.Empty);
            TitleNameChanged?.Invoke(this, EventArgs.Empty);

            if (PrimaryFile is DocumentOpenedFile openedFile)
            {
                openedFile.FileName = ICSharpCode.Core.FileName.Create(newPath);
            }
        }

        public TextEditor Editor { get; }

        void ApplySyntaxHighlighting(string? fileName)
        {
            var definition = ResolveSyntaxHighlighting(fileName);
            Editor.SyntaxHighlighting = definition;
            Editor.HighlightedLineSource?.Dispose();
            Editor.HighlightedLineSource = definition is null
                ? null
                : new XshdHighlightedLineSource(definition);
        }

        private static IHighlightingDefinition? ResolveSyntaxHighlighting(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var manager = HighlightingManager.Instance;
            var extension = Path.GetExtension(fileName);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                var byExtension = manager.GetDefinitionByExtension(extension);
                if (byExtension is not null)
                {
                    return byExtension;
                }
            }

            return Path.GetFileName(fileName).ToLowerInvariant() switch
            {
                "directory.build.props" or "directory.build.targets" or "nuget.config" or "app.config" or "web.config" => manager.GetDefinition("XML"),
                "global.json" or "appsettings.json" or "launchsettings.json" => manager.GetDefinition("Json"),
                _ => null
            };
        }

        public object? Control => Editor;

        public object? InitiallyFocusedControl => Editor;

        public IWorkbenchWindow? WorkbenchWindow { get; set; }

        public event EventHandler? TabPageTextChanged;

        public string TabPageText { get; private set; }

        public string TitleName { get; private set; }

        public event EventHandler? TitleNameChanged;

        public string InfoTip { get; }

        public event EventHandler? InfoTipChanged;

        public void Save(OpenedFile file, Stream stream)
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(Editor.Text);
        }

        public void Load(OpenedFile file, Stream stream)
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            Editor.Text = reader.ReadToEnd();
        }

        public IList<OpenedFile> Files => _files;

        public OpenedFile? PrimaryFile => _files.Count > 0 ? _files[0] : null;

        public FileName? PrimaryFileName => PrimaryFile?.FileName;

        public INavigationPoint BuildNavPoint()
        {
            return new NullNavigationPoint(PrimaryFileName?.ToString() ?? TitleName);
        }

        public bool IsDisposed { get; private set; }

        public event EventHandler? Disposed;

        public bool IsReadOnly => false;

        public bool IsViewOnly => false;

        public bool CloseWithSolution => true;

        public ICollection<IViewContent> SecondaryViewContents { get; } = new List<IViewContent>();

        public bool IsDirty => _isDirty;

        public event EventHandler? IsDirtyChanged;

        public void MarkDirty()
        {
            if (_isDirty)
            {
                return;
            }

            _isDirty = true;
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MarkClean()
        {
            if (!_isDirty)
            {
                return;
            }

            _isDirty = false;
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;

        public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;

        public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView)
        {
        }

        public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView)
        {
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ITextEditor))
            {
                return _textEditorAdapter;
            }
            if (serviceType == typeof(TextEditor))
            {
                return Editor;
            }

            return null;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            if (PrimaryFile is DocumentOpenedFile openedFile)
            {
                openedFile.NotifyClosed();
            }

            Disposed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static readonly ConditionalWeakTable<TextEditor, FoldingState> _foldingStates = new();

    internal static (string Strategy, int Count) GetFoldingSnapshot(TextEditor editor)
    {
        if (!_foldingStates.TryGetValue(editor, out var state))
            return ("None", 0);
        state.Refresh();
        return (state.StrategyName, state.Count);
    }

    private sealed class FoldingState
    {
        private readonly TextEditor _editor;
        private readonly FoldingManager _foldingManager;
        private readonly BraceFoldingStrategy _braceStrategy = new();
        private readonly XmlFoldingStrategy _xmlStrategy = new();
        private readonly VisualBasicFoldingStrategy _visualBasicStrategy = new();

        public FoldingState(TextEditor editor, FoldingManager foldingManager)
        {
            _editor = editor;
            _foldingManager = foldingManager;
        }

        public void OnTextChanged(object? sender, EventArgs e)
            => Refresh();

        public string StrategyName => Path.GetExtension(_editor.Tag as string).ToLowerInvariant() switch
        {
            ".xml" or ".xaml" => nameof(XmlFoldingStrategy),
            ".vb" => nameof(VisualBasicFoldingStrategy),
            _ => nameof(BraceFoldingStrategy)
        };

        public int Count => _foldingManager.AllFoldings.Count();

        public void Refresh()
        {
            if (_editor.Document is null)
                return;

            switch (Path.GetExtension(_editor.Tag as string).ToLowerInvariant())
            {
                case ".xml":
                case ".xaml":
                    _xmlStrategy.UpdateFoldings(_foldingManager, _editor.Document);
                    break;
                case ".vb":
                    _visualBasicStrategy.UpdateFoldings(_foldingManager, _editor.Document);
                    break;
                default:
                    _braceStrategy.UpdateFoldings(_foldingManager, _editor.Document);
                    break;
            }
        }
    }

    private sealed class DocumentOpenedFile : OpenedFile
    {
        public DocumentOpenedFile(string filePath)
        {
            FileName = ICSharpCode.Core.FileName.Create(filePath);
        }

        public override event EventHandler? FileClosed;

        public void NotifyClosed()
        {
            FileClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class UnoWorkbenchWindow : IWorkbenchWindow
    {
        private readonly LayoutDocument _document;
        private readonly ContentControl? _viewHost;
        private readonly IList<ToggleButton> _viewButtons;
        private readonly IList<UIElement?> _viewControls;

        public UnoWorkbenchWindow(
            LayoutDocument document,
            IList<IViewContent> viewContents,
            ContentControl? viewHost,
            IList<ToggleButton>? viewButtons,
            IList<UIElement?>? viewControls)
        {
            _document = document;
            _viewHost = viewHost;
            _viewButtons = viewButtons ?? Array.Empty<ToggleButton>();
            _viewControls = viewControls ?? Array.Empty<UIElement?>();
            ViewContents = viewContents;
            ActiveViewContent = viewContents[0];
            _document.Title = ActiveViewContent.TabPageText;
            foreach (var button in _viewButtons)
            {
                button.Click += (_, _) =>
                {
                    if (button.Tag is int selectedIndex)
                        SwitchView(selectedIndex);
                };
            }
        }

        public string Title => _document.Title;

        public bool IsDisposed => false;

        public IViewContent ActiveViewContent { get; set; }

        public object? Icon { get; set; }

        public event EventHandler? ActiveViewContentChanged;

        public IList<IViewContent> ViewContents { get; }

        public void SwitchView(int viewNumber)
        {
            if (viewNumber < 0 || viewNumber >= ViewContents.Count)
            {
                return;
            }

            if (_viewHost is not null && viewNumber < _viewControls.Count)
                _viewHost.Content = _viewControls[viewNumber];
            for (var index = 0; index < _viewButtons.Count; index++)
                _viewButtons[index].IsChecked = index == viewNumber;
            SetActiveView(viewNumber);
        }

        private void SetActiveView(int viewNumber)
        {
            if (ReferenceEquals(ActiveViewContent, ViewContents[viewNumber]))
                return;
            ActiveViewContent = ViewContents[viewNumber];
            ActiveViewContentChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool CloseWindow(bool force)
        {
            _document.Close();
            return true;
        }

        public void SelectWindow()
        {
            _document.IsSelected = true;
            _document.IsActive = true;
        }

        public event EventHandler? TitleChanged;
    }

    private sealed class NullNavigationPoint : INavigationPoint
    {
        public NullNavigationPoint(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; private set; }

        public string Description => FileName;

        public string FullDescription => FileName;

        public string ToolTip => FileName;

        public object NavigationData => FileName;

        public int Index => 0;

        public void JumpTo()
        {
        }

        public void FileNameChanged(string newName)
        {
            FileName = newName;
        }

        public void ContentChanging(object sender, EventArgs e)
        {
        }

        public int CompareTo(object? obj)
        {
            if (obj is NullNavigationPoint other)
            {
                return string.Compare(FileName, other.FileName, StringComparison.OrdinalIgnoreCase);
            }

            return 0;
        }
    }
}
