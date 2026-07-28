#nullable enable

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SearchAndReplace.Portable;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Search;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.ApplicationModel.DataTransfer;

namespace UnoDevelop.AddIns.Misc.SearchAndReplace;

public sealed class SearchAndReplaceViewContent : IViewContent
{
    private readonly ObservableCollection<PortableSearchResult> _results = new();
    private readonly ObservableCollection<SearchResultDisplayRow> _displayRows = new();
    private readonly PortableSearchService _searchService = new();
    private readonly PortableSearchResultGrouper _resultGrouper = new();
    private readonly Grid _control;
    private readonly TextBox _findText;
    private readonly TextBox _replaceText;
    private readonly ComboBox _scopeSelector;
    private readonly TextBox _lookInText;
    private readonly TextBox _fileTypesText;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _useRegex;
    private readonly CheckBox _includeSubdirectories;
    private readonly ComboBox _groupingSelector;
    private readonly Button _findButton;
    private readonly Button _cancelButton;
    private readonly TextBlock _status;
    private CancellationTokenSource? _searchCancellation;

    public SearchAndReplaceViewContent()
    {
        TabPageText = "Search and Replace";
        TitleName = TabPageText;
        InfoTip = "Search and replace files in the current workspace";

        _findText = new TextBox { PlaceholderText = "Find", MinWidth = 260, Text = GetActiveSelectedText() };
        _replaceText = new TextBox { PlaceholderText = "Replace", MinWidth = 260 };
        _scopeSelector = new ComboBox
        {
            MinWidth = 180,
            ItemsSource = SearchScopeItem.All,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(SearchScopeItem.Label)
        };
        _lookInText = new TextBox { Text = GetDefaultSearchRoot(), MinWidth = 360 };
        _fileTypesText = new TextBox { Text = PortableSearchDefaults.FileTypes, MinWidth = 260 };
        _matchCase = new CheckBox { Content = "Match case" };
        _useRegex = new CheckBox { Content = "Regex" };
        _includeSubdirectories = new CheckBox { Content = "Include subdirectories", IsChecked = true };
        _groupingSelector = new ComboBox
        {
            MinWidth = 160,
            ItemsSource = SearchGroupingItem.All,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(SearchGroupingItem.Label)
        };
        _groupingSelector.SelectionChanged += (_, _) => RefreshGroupedResults();
        _status = new TextBlock { Text = "Ready", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        _findButton = new Button { Content = "Find All", MinWidth = 96 };
        _findButton.Click += async (_, _) => await FindAllAsync();
        _cancelButton = new Button { Content = "Cancel", MinWidth = 80, IsEnabled = false };
        _cancelButton.Click += (_, _) => _searchCancellation?.Cancel();

        var replaceButton = new Button { Content = "Replace Listed", MinWidth = 112 };
        replaceButton.Click += (_, _) => ReplaceListed();

        var form = CreateForm(replaceButton);
        var resultList = new ListView
        {
            ItemsSource = _displayRows,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateDisplayRowTemplate(),
            Margin = new Thickness(8)
        };
        resultList.DoubleTapped += (_, _) => JumpToSelectedResult(resultList);

        var menu = new MenuFlyout();
        var jump = new MenuFlyoutItem { Text = "Go to location" };
        jump.Click += (_, _) => JumpToSelectedResult(resultList);
        menu.Items.Add(jump);
        var copy = new MenuFlyoutItem { Text = "Copy location" };
        copy.Click += (_, _) => CopySelectedResult(resultList);
        menu.Items.Add(copy);
        resultList.ContextFlyout = menu;

        _control = new Grid();
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(form, 0);
        Grid.SetRow(resultList, 1);
        _control.Children.Add(form);
        _control.Children.Add(resultList);
    }

    public object? Control => _control;
    public object? InitiallyFocusedControl => _findText;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }
    public event EventHandler? TabPageTextChanged;
    public string TabPageText { get; }
    public string TitleName { get; }
    public event EventHandler? TitleNameChanged;
    public string InfoTip { get; }
    public event EventHandler? InfoTipChanged;
    public IList<OpenedFile> Files => Array.Empty<OpenedFile>();
    public OpenedFile? PrimaryFile => null;
    public FileName? PrimaryFileName => null;
    public bool IsDisposed { get; private set; }
    public event EventHandler? Disposed;
    public bool IsReadOnly => true;
    public bool IsViewOnly => true;
    public bool CloseWithSolution => false;
    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();
    public bool IsDirty => false;
    public event EventHandler? IsDirtyChanged;

    public void Save(OpenedFile file, Stream stream)
    {
    }

    public void Load(OpenedFile file, Stream stream)
    {
    }

    public INavigationPoint BuildNavPoint() => new SearchAndReplaceNavigationPoint();
    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;
    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;
    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }
    public object? GetService(Type serviceType) => null;

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    private Grid CreateForm(Button replaceButton)
    {
        var form = new Grid { Margin = new Thickness(8), ColumnSpacing = 8, RowSpacing = 8 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 6; i++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddLabel(form, "Find:", 0);
        AddControl(form, _findText, 0);
        AddLabel(form, "Replace:", 1);
        AddControl(form, _replaceText, 1);
        AddLabel(form, "Scope:", 2);
        AddControl(form, _scopeSelector, 2);
        AddLabel(form, "Look in:", 3);
        AddControl(form, _lookInText, 3);
        AddLabel(form, "File types:", 4);
        AddControl(form, _fileTypesText, 4);

        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        options.Children.Add(_matchCase);
        options.Children.Add(_useRegex);
        options.Children.Add(_includeSubdirectories);
        options.Children.Add(new TextBlock { Text = "Group:", VerticalAlignment = VerticalAlignment.Center });
        options.Children.Add(_groupingSelector);
        Grid.SetColumn(options, 1);
        Grid.SetRow(options, 5);
        form.Children.Add(options);

        Grid.SetColumn(_findButton, 2);
        Grid.SetRow(_findButton, 0);
        form.Children.Add(_findButton);
        Grid.SetColumn(_cancelButton, 3);
        Grid.SetRow(_cancelButton, 0);
        form.Children.Add(_cancelButton);
        Grid.SetColumn(replaceButton, 4);
        Grid.SetRow(replaceButton, 0);
        form.Children.Add(replaceButton);

        Grid.SetColumn(_status, 2);
        Grid.SetColumnSpan(_status, 2);
        Grid.SetRow(_status, 5);
        form.Children.Add(_status);

        return form;
    }

    private static void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(label, 0);
        Grid.SetRow(label, row);
        grid.Children.Add(label);
    }

    private static void AddControl(Grid grid, Control control, int row)
    {
        Grid.SetColumn(control, 1);
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private async Task FindAllAsync()
    {
        _results.Clear();
        _displayRows.Clear();
        var pattern = _findText.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            _status.Text = "Enter a search pattern.";
            return;
        }

        var scope = CreateScope();
        if (scope.IsDirectory && (string.IsNullOrWhiteSpace(scope.Directory) || !Directory.Exists(scope.Directory)))
        {
            _status.Text = "Search directory does not exist.";
            return;
        }
        if (!scope.IsDirectory && scope.FilePaths.Count == 0)
        {
            _status.Text = "No files available in the selected scope.";
            return;
        }

        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        _findButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        try
        {
            var options = CreateOptions(pattern, _replaceText.Text ?? string.Empty, scope.Directory ?? GetDefaultSearchRoot());
            var progress = new Progress<int>(count => _status.Text = $"Searched {count} file(s)...");
            var run = await Task.Run(() => _searchService.FindAll(options, scope, cancellationToken, progress), cancellationToken);
            foreach (var result in run.Results)
            {
                _results.Add(result);
            }
            RefreshGroupedResults();

            _status.Text = run.FormatStatus();
            PublishSearchResults(pattern);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Search cancelled.";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _findButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
        }
    }

    private void ReplaceListed()
    {
        if (_results.Count == 0)
        {
            _status.Text = "Run Find All before replacing.";
            return;
        }

        var pattern = _findText.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            _status.Text = "Enter a search pattern.";
            return;
        }

        try
        {
            var scope = CreateScope();
            var options = CreateOptions(pattern, _replaceText.Text ?? string.Empty, scope.Directory ?? GetDefaultSearchRoot());
            var plan = _searchService.CreateReplacePlan(_results, options, scope);
            _status.Text = plan.FormatStatus();
            if (!plan.HasChanges)
            {
                return;
            }

            if (!MessageService.AskQuestion(
                $"Replace {plan.MatchCount} occurrence(s) in {plan.ChangedFileCount} file(s)?",
                "Search and Replace"))
            {
                _status.Text = "Replace cancelled.";
                return;
            }

            var run = _searchService.ApplyReplacePlan(plan);
            _status.Text = run.FormatStatus();
            var openStatus = OpenChangedFiles(run);
            if (!string.IsNullOrEmpty(openStatus))
            {
                _status.Text = run.FormatStatus() + " " + openStatus;
            }
            _ = FindAllAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            _status.Text = ex.Message;
        }
    }

    private PortableSearchOptions CreateOptions(string pattern, string replacement, string root) =>
        new(pattern, replacement, root, _fileTypesText.Text, _matchCase.IsChecked == true, _useRegex.IsChecked == true, _includeSubdirectories.IsChecked == true);

    private PortableSearchScope CreateScope()
    {
        var selected = _scopeSelector.SelectedItem as SearchScopeItem ?? SearchScopeItem.All[0];
        return selected.Kind switch
        {
            PortableSearchScopeKind.CurrentDocument => PortableSearchScope.ForFiles(selected.Kind, GetCurrentDocumentFiles()),
            PortableSearchScopeKind.AllOpenFiles => PortableSearchScope.ForFiles(selected.Kind, GetOpenDocumentFiles()),
            PortableSearchScopeKind.WholeProject => PortableSearchScope.ForFiles(selected.Kind, GetCurrentProjectFiles()),
            PortableSearchScopeKind.WholeSolution => PortableSearchScope.ForFiles(selected.Kind, GetSolutionFiles()),
            _ => PortableSearchScope.ForDirectory(_lookInText.Text)
        };
    }

    private void RefreshGroupedResults()
    {
        _displayRows.Clear();
        if (_results.Count == 0)
        {
            return;
        }

        var selected = _groupingSelector.SelectedItem as SearchGroupingItem ?? SearchGroupingItem.All[0];
        var groups = _resultGrouper.Group(_results, selected.Kind, GetProjectNameForFile);
        foreach (var group in groups)
        {
            AddGroupRows(group, level: 0);
        }
    }

    private void AddGroupRows(PortableSearchResultGroup group, int level)
    {
        var title = group.Title;
        if (group.Results.Count > 0 || group.Children.Count > 0)
        {
            _displayRows.Add(SearchResultDisplayRow.ForGroup(title, group.OccurrenceCount, level));
        }

        foreach (var result in group.Results)
        {
            _displayRows.Add(SearchResultDisplayRow.ForResult(result, level + 1));
        }

        foreach (var child in group.Children)
        {
            AddGroupRows(child, level + 1);
        }
    }

    private static DataTemplate CreateDisplayRowTemplate()
    {
        var template = new DataTemplate(() =>
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };
            var location = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            location.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(SearchResultDisplayRow.Title)) });
            location.SetBinding(FrameworkElement.MarginProperty, new Binding { Path = new PropertyPath(nameof(SearchResultDisplayRow.Margin)) });
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
            preview.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(SearchResultDisplayRow.Preview)) });
            preview.SetBinding(FrameworkElement.MarginProperty, new Binding { Path = new PropertyPath(nameof(SearchResultDisplayRow.Margin)) });
            panel.Children.Add(location);
            panel.Children.Add(preview);
            return panel;
        });
        return template;
    }

    private static void CopySelectedResult(ListView resultList)
    {
        if (resultList.SelectedItem is not SearchResultDisplayRow { Result: { } item })
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(item.Location);
        Clipboard.SetContent(package);
    }

    private static void JumpToSelectedResult(ListView resultList)
    {
        if (resultList.SelectedItem is not SearchResultDisplayRow { Result: { } item })
        {
            return;
        }

        var fileName = FileName.Create(item.FilePath);
        if (fileName is null)
        {
            return;
        }

        SD.FileService.JumpToFilePosition(fileName, item.Line, item.Column);
    }

    private static string OpenChangedFiles(PortableReplaceRunResult run)
    {
        if (run.ChangedFilePaths is not { Count: > 0 })
        {
            return string.Empty;
        }

        if (run.ChangedFilePaths.Count > PortableSearchDefaults.MaxAutoOpenChangedFiles)
        {
            return $"{run.ChangedFilePaths.Count} files changed; not auto-opening more than {PortableSearchDefaults.MaxAutoOpenChangedFiles} files.";
        }

        for (var i = 0; i < run.ChangedFilePaths.Count; i++)
        {
            var fileName = FileName.Create(run.ChangedFilePaths[i]);
            if (fileName is null)
            {
                continue;
            }

            SD.FileService.OpenFile(fileName, switchToOpenedView: i == run.ChangedFilePaths.Count - 1);
        }

        return $"Opened {run.ChangedFilePaths.Count} changed file(s).";
    }

    private void PublishSearchResults(string pattern)
    {
        var service = ServiceSingleton.GetRequiredService<UnoSearchResultsService>();
        service.ShowSearchResults(
            $"Occurrences of '{pattern}'",
            _results.Select(item => (Item: item, FileName: FileName.Create(item.FilePath)))
                .Where(item => item.FileName is not null)
                .Select(item => new SearchResultEntry(
                    item.FileName,
                    item.Item.Line,
                    item.Item.Column,
                    item.Item.Offset,
                    item.Item.Length,
                    item.Item.Preview)));

        var padType = Type.GetType("UnoDevelop.Workbench.SearchResultsPad, UnoDevelop");
        var pad = padType is null ? null : SD.Workbench.GetPad(padType);
        if (pad is not null)
        {
            SD.Workbench.ActivatePad(pad);
        }
    }

    private static string GetActiveSelectedText()
    {
        if (SD.Workbench.ActiveViewContent?.GetService(typeof(ITextEditor)) is ITextEditor editor
            && editor.SelectionLength > 0)
        {
            return editor.SelectedText;
        }

        return string.Empty;
    }

    private static string GetDefaultSearchRoot()
    {
        var solutionFile = SD.ProjectService.CurrentSolution?.FileName?.ToString();
        if (!string.IsNullOrEmpty(solutionFile))
        {
            var directory = Path.GetDirectoryName(solutionFile);
            if (!string.IsNullOrEmpty(directory))
            {
                return directory;
            }
        }

        return Environment.CurrentDirectory;
    }

    private static IEnumerable<string> GetCurrentDocumentFiles()
    {
        if (SD.Workbench.ActiveViewContent?.GetService(typeof(ITextEditor)) is ITextEditor editor
            && editor.FileName is not null)
        {
            yield return editor.FileName;
        }
    }

    private static IEnumerable<string> GetOpenDocumentFiles()
    {
        foreach (var view in SD.Workbench.ViewContentCollection)
        {
            if (view.GetService(typeof(ITextEditor)) is ITextEditor editor
                && editor.FileName is not null)
            {
                yield return editor.FileName;
            }
        }
    }

    private static IEnumerable<string> GetCurrentProjectFiles()
    {
        var project = ProjectService.CurrentProject;
        if (project is null)
            yield break;

        foreach (var item in project.Items.OfType<FileProjectItem>())
        {
            yield return item.FileName;
        }
    }

    private static IEnumerable<string> GetSolutionFiles()
    {
        var solution = ProjectService.OpenSolution;
        if (solution is null)
            yield break;

        foreach (var item in solution.AllItems.OfType<ISolutionFileItem>())
        {
            yield return item.FileName;
        }

        foreach (var item in solution.Projects.SelectMany(project => project.Items).OfType<FileProjectItem>())
        {
            yield return item.FileName;
        }
    }

    private static string? GetProjectNameForFile(string filePath)
    {
        var fileName = FileName.Create(filePath);
        if (fileName is null)
            return null;

        return SD.ProjectService.FindProjectContainingFile(fileName)?.Name;
    }

    private sealed record SearchScopeItem(string Label, PortableSearchScopeKind Kind)
    {
        public static IReadOnlyList<SearchScopeItem> All { get; } =
        [
            new("Directory", PortableSearchScopeKind.Directory),
            new("Current document", PortableSearchScopeKind.CurrentDocument),
            new("All open files", PortableSearchScopeKind.AllOpenFiles),
            new("Current project", PortableSearchScopeKind.WholeProject),
            new("Whole solution", PortableSearchScopeKind.WholeSolution)
        ];
    }

    private sealed record SearchGroupingItem(string Label, PortableSearchResultGroupingKind Kind)
    {
        public static IReadOnlyList<SearchGroupingItem> All { get; } =
        [
            new("Flat", PortableSearchResultGroupingKind.Flat),
            new("File", PortableSearchResultGroupingKind.PerFile),
            new("Project", PortableSearchResultGroupingKind.PerProject),
            new("Project/File", PortableSearchResultGroupingKind.PerProjectAndFile)
        ];
    }

    private sealed record SearchResultDisplayRow(string Title, string Preview, PortableSearchResult? Result, Thickness Margin)
    {
        public static SearchResultDisplayRow ForGroup(string title, int occurrenceCount, int level) =>
            new($"{title} ({occurrenceCount})", string.Empty, null, new Thickness(level * 16, 0, 0, 0));

        public static SearchResultDisplayRow ForResult(PortableSearchResult result, int level) =>
            new(result.Location, result.Preview, result, new Thickness(level * 16, 0, 0, 0));
    }

    private sealed class SearchAndReplaceNavigationPoint : INavigationPoint
    {
        public string FileName { get; private set; } = string.Empty;
        public string Description => "Search and Replace";
        public string FullDescription => Description;
        public string ToolTip => Description;
        public object NavigationData => Description;
        public int Index => 0;
        public void JumpTo()
        {
        }

        public void FileNameChanged(string newName) => FileName = newName;
        public void ContentChanging(object sender, EventArgs e)
        {
        }

        public int CompareTo(object? obj) => obj is SearchAndReplaceNavigationPoint ? 0 : -1;
    }
}
