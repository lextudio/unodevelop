using System;
using System.Collections.ObjectModel;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.ApplicationModel.DataTransfer;

namespace UnoDevelop.Workbench;

public sealed class SearchResultsPad : UserControl
{
    private readonly UnoSearchResultsService _service;
    private readonly ObservableCollection<SearchResultEntry> _results = new();
    private readonly TextBlock _title;
    private readonly ListView _resultView;

    public SearchResultsPad()
        : this(ServiceSingleton.GetRequiredService<UnoSearchResultsService>())
    {
    }

    public SearchResultsPad(UnoSearchResultsService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));

        _title = new TextBlock
        {
            Text = _service.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(8, 6, 8, 4)
        };

        _resultView = new ListView
        {
            ItemsSource = _results,
            SelectionMode = ListViewSelectionMode.Extended,
            ItemTemplate = CreateItemTemplate(),
            Margin = new Thickness(0)
        };
        _resultView.DoubleTapped += (_, _) => JumpToSelected();
        BuildContextMenu();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_title, 0);
        Grid.SetRow(_resultView, 1);
        root.Children.Add(_title);
        root.Children.Add(_resultView);
        Content = root;

        _service.ResultsChanged += ServiceResultsChanged;
        Refresh();
    }

    private void ServiceResultsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        _title.Text = $"{_service.Title} ({_service.Results.Count})";
        _results.Clear();
        foreach (var result in _service.Results)
        {
            _results.Add(result);
        }
    }

    private static DataTemplate CreateItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                Padding = new Thickness(8, 3, 8, 3)
            };

            var location = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            location.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(SearchResultEntry.Location)) });

            var preview = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            preview.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(SearchResultEntry.Preview)) });

            panel.Children.Add(location);
            panel.Children.Add(preview);
            return panel;
        });
    }

    private void BuildContextMenu()
    {
        var menu = new MenuFlyout();

        var goTo = new MenuFlyoutItem { Text = "Go to Location" };
        goTo.Click += (_, _) => JumpToSelected();
        menu.Items.Add(goTo);

        var copy = new MenuFlyoutItem { Text = "Copy" };
        copy.Click += (_, _) => CopySelectionToClipboard();
        menu.Items.Add(copy);

        var selectAll = new MenuFlyoutItem { Text = "Select All" };
        selectAll.Click += (_, _) => SelectAllItems();
        menu.Items.Add(selectAll);

        var clear = new MenuFlyoutItem { Text = "Clear" };
        clear.Click += (_, _) => _service.Clear();
        menu.Items.Add(clear);

        _resultView.ContextFlyout = menu;
    }

    private void SelectAllItems()
    {
        _resultView.SelectedItems.Clear();
        foreach (var item in _results)
        {
            _resultView.SelectedItems.Add(item);
        }
    }

    private SearchResultEntry[] GetSelectedResults()
    {
        var selected = _resultView.SelectedItems.OfType<SearchResultEntry>().ToArray();
        if (selected.Length == 0 && _resultView.SelectedItem is SearchResultEntry single)
        {
            selected = new[] { single };
        }

        return selected;
    }

    private void JumpToSelected()
    {
        var result = GetSelectedResults().FirstOrDefault();
        if (result is null)
        {
            return;
        }

        SD.FileService.JumpToFilePosition(result.FileName, result.Line, result.Column);
    }

    private void CopySelectionToClipboard()
    {
        var selected = GetSelectedResults();
        if (selected.Length == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, selected.Select(item => $"{item.Location}\t{item.Preview}"));
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
