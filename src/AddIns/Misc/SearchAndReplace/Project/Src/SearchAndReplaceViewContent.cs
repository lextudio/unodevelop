using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Search;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.ApplicationModel.DataTransfer;

namespace UnoDevelop.AddIns.Misc.SearchAndReplace;

public sealed class SearchAndReplaceViewContent : IViewContent
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "artifacts"
    };

    private readonly ObservableCollection<SearchResultItem> _results = new();
    private readonly Grid _control;
    private readonly TextBox _findText;
    private readonly TextBox _replaceText;
    private readonly TextBox _lookInText;
    private readonly TextBox _fileTypesText;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _useRegex;
    private readonly CheckBox _includeSubdirectories;
    private readonly TextBlock _status;

    public SearchAndReplaceViewContent()
    {
        TabPageText = "Search and Replace";
        TitleName = TabPageText;
        InfoTip = "Search and replace files in the current workspace";

        _findText = new TextBox { PlaceholderText = "Find", MinWidth = 260, Text = GetActiveSelectedText() };
        _replaceText = new TextBox { PlaceholderText = "Replace", MinWidth = 260 };
        _lookInText = new TextBox { Text = GetDefaultSearchRoot(), MinWidth = 360 };
        _fileTypesText = new TextBox { Text = "*.cs;*.xaml;*.xml;*.resx;*.settings;*.txt;*.md", MinWidth = 260 };
        _matchCase = new CheckBox { Content = "Match case" };
        _useRegex = new CheckBox { Content = "Regex" };
        _includeSubdirectories = new CheckBox { Content = "Include subdirectories", IsChecked = true };
        _status = new TextBlock { Text = "Ready", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        var findButton = new Button { Content = "Find All", MinWidth = 96 };
        findButton.Click += (_, _) => FindAll();

        var replaceButton = new Button { Content = "Replace Listed", MinWidth = 112 };
        replaceButton.Click += (_, _) => ReplaceListed();

        var form = CreateForm(findButton, replaceButton);
        var resultList = new ListView
        {
            ItemsSource = _results,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateResultTemplate(),
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

    private Grid CreateForm(Button findButton, Button replaceButton)
    {
        var form = new Grid { Margin = new Thickness(8), ColumnSpacing = 8, RowSpacing = 8 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 5; i++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddLabel(form, "Find:", 0);
        AddControl(form, _findText, 0);
        AddLabel(form, "Replace:", 1);
        AddControl(form, _replaceText, 1);
        AddLabel(form, "Look in:", 2);
        AddControl(form, _lookInText, 2);
        AddLabel(form, "File types:", 3);
        AddControl(form, _fileTypesText, 3);

        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        options.Children.Add(_matchCase);
        options.Children.Add(_useRegex);
        options.Children.Add(_includeSubdirectories);
        Grid.SetColumn(options, 1);
        Grid.SetRow(options, 4);
        form.Children.Add(options);

        Grid.SetColumn(findButton, 2);
        Grid.SetRow(findButton, 0);
        form.Children.Add(findButton);
        Grid.SetColumn(replaceButton, 3);
        Grid.SetRow(replaceButton, 0);
        form.Children.Add(replaceButton);

        Grid.SetColumn(_status, 2);
        Grid.SetColumnSpan(_status, 2);
        Grid.SetRow(_status, 4);
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

    private void FindAll()
    {
        _results.Clear();
        var pattern = _findText.Text;
        if (string.IsNullOrEmpty(pattern))
        {
            _status.Text = "Enter a search pattern.";
            return;
        }

        var root = _lookInText.Text;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            _status.Text = "Search directory does not exist.";
            return;
        }

        var matcher = CreateMatcher(pattern);
        if (matcher is null)
        {
            return;
        }

        var fileCount = 0;
        foreach (var file in EnumerateFiles(root))
        {
            fileCount++;
            AddMatches(file, matcher);
        }

        _status.Text = $"{_results.Count} result(s) in {fileCount} file(s).";
        PublishSearchResults(pattern);
    }

    private void AddMatches(string file, Func<string, IEnumerable<MatchRange>> matcher)
    {
        string text;
        try
        {
            var info = new FileInfo(file);
            if (info.Length > 4 * 1024 * 1024)
            {
                return;
            }

            text = File.ReadAllText(file);
        }
        catch
        {
            return;
        }

        var lineStarts = GetLineStarts(text);
        foreach (var match in matcher(text))
        {
            var lineIndex = FindLineIndex(lineStarts, match.Index);
            var lineStart = lineStarts[lineIndex];
            var lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            _results.Add(new SearchResultItem(
                file,
                lineIndex + 1,
                match.Index - lineStart + 1,
                match.Index,
                match.Length,
                text[lineStart..lineEnd].Trim()));
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

        var replacement = _replaceText.Text ?? string.Empty;
        var replace = CreateReplacer(pattern, replacement);
        if (replace is null)
        {
            return;
        }

        var changed = 0;
        foreach (var file in _results.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            try
            {
                var original = File.ReadAllText(file);
                var updated = replace(original);
                if (!string.Equals(original, updated, StringComparison.Ordinal))
                {
                    File.WriteAllText(file, updated);
                    changed++;
                }
            }
            catch
            {
                // Keep replacing other listed files when one file is locked or unreadable.
            }
        }

        _status.Text = $"Updated {changed} file(s).";
        FindAll();
    }

    private Func<string, IEnumerable<MatchRange>>? CreateMatcher(string pattern)
    {
        if (_useRegex.IsChecked == true)
        {
            try
            {
                var regex = new Regex(pattern, GetRegexOptions());
                return text => regex.Matches(text).Select(match => new MatchRange(match.Index, match.Length));
            }
            catch (ArgumentException ex)
            {
                _status.Text = ex.Message;
                return null;
            }
        }

        var comparison = _matchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return text => FindLiteralMatches(text, pattern, comparison);
    }

    private Func<string, string>? CreateReplacer(string pattern, string replacement)
    {
        if (_useRegex.IsChecked == true)
        {
            try
            {
                var regex = new Regex(pattern, GetRegexOptions());
                return text => regex.Replace(text, replacement);
            }
            catch (ArgumentException ex)
            {
                _status.Text = ex.Message;
                return null;
            }
        }

        var comparison = _matchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return text => ReplaceLiteral(text, pattern, replacement, comparison);
    }

    private RegexOptions GetRegexOptions()
    {
        var options = RegexOptions.Multiline;
        if (_matchCase.IsChecked != true)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return options;
    }

    private IEnumerable<string> EnumerateFiles(string root)
    {
        var patterns = _fileTypesText.Text
            .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DefaultIfEmpty("*.*")
            .ToArray();

        var searchOption = _includeSubdirectories.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var pattern in patterns)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, pattern, searchOption);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!IsInExcludedDirectory(root, file))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsInExcludedDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => ExcludedDirectories.Contains(part));
    }

    private static IEnumerable<MatchRange> FindLiteralMatches(string text, string pattern, StringComparison comparison)
    {
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(pattern, index, comparison);
            if (index < 0)
            {
                yield break;
            }

            yield return new MatchRange(index, pattern.Length);
            index += Math.Max(pattern.Length, 1);
        }
    }

    private static string ReplaceLiteral(string text, string pattern, string replacement, StringComparison comparison)
    {
        var result = new System.Text.StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var match = text.IndexOf(pattern, index, comparison);
            if (match < 0)
            {
                result.Append(text, index, text.Length - index);
                break;
            }

            result.Append(text, index, match - index);
            result.Append(replacement);
            index = match + pattern.Length;
        }

        return result.ToString();
    }

    private static int[] GetLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' && i + 1 < text.Length)
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    private static int FindLineIndex(int[] lineStarts, int index)
    {
        var position = Array.BinarySearch(lineStarts, index);
        return position >= 0 ? position : Math.Max(0, ~position - 1);
    }

    private static DataTemplate CreateResultTemplate()
    {
        var template = new DataTemplate(() =>
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };
            var location = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            location.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(SearchResultItem.Location)) });
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
            preview.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(SearchResultItem.Preview)) });
            panel.Children.Add(location);
            panel.Children.Add(preview);
            return panel;
        });
        return template;
    }

    private static void CopySelectedResult(ListView resultList)
    {
        if (resultList.SelectedItem is not SearchResultItem item)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(item.Location);
        Clipboard.SetContent(package);
    }

    private static void JumpToSelectedResult(ListView resultList)
    {
        if (resultList.SelectedItem is not SearchResultItem item)
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

    private sealed record MatchRange(int Index, int Length);

    private sealed record SearchResultItem(string FilePath, int Line, int Column, int Offset, int Length, string Preview)
    {
        public string Location => $"{FilePath}:{Line}:{Column}";
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
