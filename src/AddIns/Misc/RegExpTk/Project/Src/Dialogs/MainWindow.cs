using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace UnoDevelop.AddIns.Misc.RegExpTk;

public sealed class RegularExpressionToolkitViewContent : IViewContent
{
    private readonly ObservableCollection<RegexMatchItem> _matches = new();
    private readonly ObservableCollection<RegexGroupItem> _groups = new();
    private readonly Grid _control;
    private readonly TextBox _patternBox;
    private readonly TextBox _inputBox;
    private readonly TextBox _replacementBox;
    private readonly TextBox _replacementResultBox;
    private readonly CheckBox _ignoreCase;
    private readonly CheckBox _multiline;
    private readonly CheckBox _singleline;
    private readonly TextBlock _status;

    public RegularExpressionToolkitViewContent()
    {
        TabPageText = "Regular Expressions";
        TitleName = "Regular Expressions Toolkit";
        InfoTip = "Test regular expressions";

        _patternBox = new TextBox { PlaceholderText = "Regular expression", Margin = new Thickness(8, 8, 4, 4) };
        _inputBox = new TextBox
        {
            PlaceholderText = "Input",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 4, 4, 8),
            MinHeight = 180
        };
        _replacementBox = new TextBox { PlaceholderText = "Replacement", Margin = new Thickness(4, 8, 8, 4) };
        _replacementResultBox = new TextBox
        {
            PlaceholderText = "Replacement result",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            Margin = new Thickness(4, 4, 8, 8),
            MinHeight = 180
        };
        _ignoreCase = new CheckBox { Content = "Ignore case", Margin = new Thickness(8, 0, 8, 0) };
        _multiline = new CheckBox { Content = "Multiline", Margin = new Thickness(8, 0, 8, 0) };
        _singleline = new CheckBox { Content = "Singleline", Margin = new Thickness(8, 0, 8, 0) };
        _status = new TextBlock { Text = "No matches.", Margin = new Thickness(8, 4, 8, 8) };

        var quickInsert = new ComboBox
        {
            Header = "Insert",
            ItemsSource = QuickInsert.Items,
            DisplayMemberPath = nameof(QuickInsert.Name),
            Margin = new Thickness(8, 0, 8, 0),
            MinWidth = 150
        };
        quickInsert.SelectionChanged += (_, _) =>
        {
            if (quickInsert.SelectedItem is QuickInsert item)
            {
                _patternBox.SelectedText = item.Text;
                quickInsert.SelectedIndex = -1;
            }
        };

        var run = new Button { Content = "Run", Margin = new Thickness(8, 0, 4, 0) };
        run.Click += (_, _) => Evaluate();

        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 4) };
        options.Children.Add(run);
        options.Children.Add(_ignoreCase);
        options.Children.Add(_multiline);
        options.Children.Add(_singleline);
        options.Children.Add(quickInsert);

        var inputPanel = new Grid();
        inputPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inputPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inputPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_patternBox, 0);
        Grid.SetRow(options, 1);
        Grid.SetRow(_inputBox, 2);
        inputPanel.Children.Add(_patternBox);
        inputPanel.Children.Add(options);
        inputPanel.Children.Add(_inputBox);

        var replacePanel = new Grid();
        replacePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        replacePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_replacementBox, 0);
        Grid.SetRow(_replacementResultBox, 1);
        replacePanel.Children.Add(_replacementBox);
        replacePanel.Children.Add(_replacementResultBox);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(inputPanel, 0);
        Grid.SetColumn(replacePanel, 1);
        top.Children.Add(inputPanel);
        top.Children.Add(replacePanel);

        var matchList = new ListView
        {
            ItemsSource = _matches,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateMatchTemplate(),
            Margin = new Thickness(8, 0, 4, 8)
        };
        matchList.SelectionChanged += (_, _) => ShowGroups(matchList.SelectedItem as RegexMatchItem);

        var groupList = new ListView
        {
            ItemsSource = _groups,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateGroupTemplate(),
            Margin = new Thickness(4, 0, 8, 8)
        };

        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(matchList, 0);
        Grid.SetColumn(groupList, 1);
        bottom.Children.Add(matchList);
        bottom.Children.Add(groupList);

        _control = new Grid();
        _control.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(top, 0);
        Grid.SetRow(_status, 1);
        Grid.SetRow(bottom, 2);
        _control.Children.Add(top);
        _control.Children.Add(_status);
        _control.Children.Add(bottom);
    }

    public object? Control => _control;
    public object? InitiallyFocusedControl => _patternBox;
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

    public void Save(OpenedFile file, System.IO.Stream stream) { }
    public void Load(OpenedFile file, System.IO.Stream stream) { }
    public INavigationPoint BuildNavPoint() => new RegexToolkitNavigationPoint();
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

    private void Evaluate()
    {
        _matches.Clear();
        _groups.Clear();
        _replacementResultBox.Text = string.Empty;

        try
        {
            var regex = new Regex(_patternBox.Text, GetOptions());
            var matches = regex.Matches(_inputBox.Text);
            foreach (Match match in matches)
            {
                _matches.Add(new RegexMatchItem(match));
            }

            _replacementResultBox.Text = regex.Replace(_inputBox.Text, _replacementBox.Text);
            _status.Text = $"{matches.Count} match(es).";
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            _status.Text = ex.Message;
        }
    }

    private RegexOptions GetOptions()
    {
        var options = RegexOptions.None;
        if (_ignoreCase.IsChecked == true)
        {
            options |= RegexOptions.IgnoreCase;
        }

        if (_multiline.IsChecked == true)
        {
            options |= RegexOptions.Multiline;
        }

        if (_singleline.IsChecked == true)
        {
            options |= RegexOptions.Singleline;
        }

        return options;
    }

    private void ShowGroups(RegexMatchItem? item)
    {
        _groups.Clear();
        if (item is null)
        {
            return;
        }

        for (var i = 0; i < item.Match.Groups.Count; i++)
        {
            _groups.Add(new RegexGroupItem(i, item.Match.Groups[i]));
        }
    }

    private static DataTemplate CreateMatchTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 5, 8, 5), ColumnSpacing = 10 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            root.Children.Add(BoundCell("Value", 0, true));
            root.Children.Add(BoundCell("Index", 1, false));
            root.Children.Add(BoundCell("End", 2, false));
            root.Children.Add(BoundCell("Length", 3, false));
            return root;
        });
    }

    private static DataTemplate CreateGroupTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 5, 8, 5), ColumnSpacing = 10 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            root.Children.Add(BoundCell("GroupIndex", 0, true));
            root.Children.Add(BoundCell("Value", 1, false));
            root.Children.Add(BoundCell("Index", 2, false));
            root.Children.Add(BoundCell("Length", 3, false));
            return root;
        });
    }

    private static TextBlock BoundCell(string path, int column, bool strong)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };
        text.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(path) });
        Grid.SetColumn(text, column);
        return text;
    }

    private sealed record RegexMatchItem(Match Match)
    {
        public string Value => Match.Value;
        public int Index => Match.Index;
        public int End => Match.Index + Match.Length;
        public int Length => Match.Length;
    }

    private sealed record RegexGroupItem(int GroupIndex, Group Group)
    {
        public string Value => Group.Success ? Group.Value : string.Empty;
        public int Index => Group.Success ? Group.Index : -1;
        public int Length => Group.Success ? Group.Length : 0;
    }

    private sealed record QuickInsert(string Name, string Text)
    {
        public static IReadOnlyList<QuickInsert> Items { get; } = new[]
        {
            new QuickInsert("Ungreedy star", "*?"),
            new QuickInsert("Word character", "\\w"),
            new QuickInsert("Non-word character", "\\W"),
            new QuickInsert("Whitespace", "\\s"),
            new QuickInsert("Non-whitespace", "\\S"),
            new QuickInsert("Digit", "\\d"),
            new QuickInsert("Non-digit", "\\D"),
            new QuickInsert("Word boundary", "\\b")
        };
    }

    private sealed class RegexToolkitNavigationPoint : INavigationPoint
    {
        public string FileName { get; private set; } = string.Empty;
        public string Description => "Regular Expressions Toolkit";
        public string FullDescription => Description;
        public string ToolTip => Description;
        public object NavigationData => Description;
        public int Index => 0;
        public void JumpTo() { }
        public void FileNameChanged(string newName) => FileName = newName;
        public void ContentChanging(object sender, EventArgs e) { }
        public int CompareTo(object? obj) => obj is RegexToolkitNavigationPoint ? 0 : -1;
    }
}
