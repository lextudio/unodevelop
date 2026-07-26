using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor.Bookmarks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Debugger;

public sealed class BreakpointsPad : UserControl
{
    private readonly ObservableCollection<BreakpointItem> _breakpoints = new();
    private readonly ListView _listView;
    private bool _eventsAttached;

    public BreakpointsPad()
    {
        AttachBookmarkEvents();

        var toolbar = new CommandBar
        {
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Right,
            IsOpen = false,
        };
        var refresh = new AppBarButton { Label = "Refresh", Icon = new SymbolIcon(Symbol.Refresh) };
        refresh.Click += (_, _) => Refresh();
        toolbar.PrimaryCommands.Add(refresh);

        var remove = new AppBarButton { Label = "Remove", Icon = new SymbolIcon(Symbol.Delete) };
        remove.Click += (_, _) => RemoveSelected();
        toolbar.PrimaryCommands.Add(remove);

        var clear = new AppBarButton { Label = "Clear All", Icon = new SymbolIcon(Symbol.Clear) };
        clear.Click += (_, _) => ClearAll();
        toolbar.SecondaryCommands.Add(clear);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(70) },
                new ColumnDefinition { Width = new GridLength(90) },
            },
            Padding = new Thickness(8, 4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        header.Children.Add(new TextBlock { Text = "File", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Line", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 1);
        header.Children.Add(new TextBlock { Text = "Enabled", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 2);

        _listView = new ListView
        {
            ItemsSource = _breakpoints,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = BuildItemTemplate(),
            Margin = new Thickness(4),
        };
        AutomationProperties.SetAutomationId(_listView, "breakpoints-list");

        var stack = new StackPanel();
        stack.Children.Add(toolbar);
        stack.Children.Add(header);
        stack.Children.Add(_listView);
        Content = stack;

        Refresh();
    }

    private static DataTemplate BuildItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(70) },
                    new ColumnDefinition { Width = new GridLength(90) },
                },
                Padding = new Thickness(4, 2),
            };

            var file = new TextBlock { TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis };
            file.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("File") });
            grid.Children.Add(file);

            var line = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace") };
            line.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Line") });
            Grid.SetColumn(line, 1);
            grid.Children.Add(line);

            var enabled = new CheckBox { IsEnabled = false };
            enabled.SetBinding(CheckBox.IsCheckedProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Enabled") });
            Grid.SetColumn(enabled, 2);
            grid.Children.Add(enabled);

            return grid;
        });
    }

    private void AttachBookmarkEvents()
    {
        if (_eventsAttached)
            return;
        SD.BookmarkManager.BookmarkAdded += OnBookmarkChanged;
        SD.BookmarkManager.BookmarkRemoved += OnBookmarkChanged;
        _eventsAttached = true;
    }

    private void OnBookmarkChanged(object? sender, BookmarkEventArgs e)
        => DispatcherQueue.TryEnqueue(Refresh);

    public void Refresh()
    {
        _breakpoints.Clear();
        foreach (var item in GetBreakpointItems())
            _breakpoints.Add(item);
    }

    private void RemoveSelected()
    {
        if (_listView.SelectedItem is not BreakpointItem item)
            return;
        var fileName = FileName.Create(item.File);
        var bookmark = SD.BookmarkManager.GetBookmarks(fileName)
            .FirstOrDefault(b => b.LineNumber == item.Line);
        if (bookmark != null)
            SD.BookmarkManager.RemoveMark(bookmark);
    }

    private static void ClearAll()
    {
        foreach (var bookmark in SD.BookmarkManager.Bookmarks.ToArray())
            SD.BookmarkManager.RemoveMark(bookmark);
    }

    public Task<IReadOnlyList<object>> GetSnapshotAsync()
    {
        IReadOnlyList<object> snapshot = _breakpoints
            .Select(item => (object)new
            {
                File = item.File,
                Line = item.Line,
                Enabled = item.Enabled,
                Location = item.Location
            })
            .ToArray();
        return Task.FromResult(snapshot);
    }

    public new void Dispose()
    {
        if (!_eventsAttached)
            return;
        SD.BookmarkManager.BookmarkAdded -= OnBookmarkChanged;
        SD.BookmarkManager.BookmarkRemoved -= OnBookmarkChanged;
        _eventsAttached = false;
    }

    private static IReadOnlyList<BreakpointItem> GetBreakpointItems()
    {
        return SD.BookmarkManager.Bookmarks
            .Where(bookmark => bookmark.FileName != null && bookmark.LineNumber > 0)
            .Select(bookmark => new BreakpointItem(
                bookmark.FileName.ToString(),
                bookmark.LineNumber,
                bookmark.CanToggle,
                $"{bookmark.FileName}:{bookmark.LineNumber}"))
            .OrderBy(item => item.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ToArray();
    }

    private sealed record BreakpointItem(string File, int Line, bool Enabled, string Location);
}
