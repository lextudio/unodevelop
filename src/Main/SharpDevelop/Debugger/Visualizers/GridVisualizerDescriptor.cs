using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Debugging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnoDevelop.Debugger;

namespace UnoDevelop.Debugger.Visualizers;

public sealed class GridVisualizerDescriptor : IVisualizerDescriptor
{
    private static readonly string[] CollectionTypePrefixes =
    [
        "System.Collections",
        "System.Array",
        "System.Linq",
    ];

    private static readonly string[] CollectionTypeNames =
    [
        "Array",
        "ArrayList",
        "List",
        "Dictionary",
        "HashSet",
        "SortedList",
        "SortedDictionary",
        "Queue",
        "Stack",
        "LinkedList",
        "Collection",
        "ObservableCollection",
        "ReadOnlyCollection",
        "IEnumerable",
        "ICollection",
        "IList",
        "IDictionary",
    ];

    public bool IsVisualizerAvailable(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        // Arrays: int[], string[,], etc.
        if (typeName.EndsWith("[]") || typeName.EndsWith("[,]") || typeName.EndsWith("[,,]"))
            return true;
        // Parameterized types: List<int>, Dictionary<string,int>
        if (typeName.Contains('<') || typeName.Contains('`'))
            return true;
        // Named collections
        foreach (var prefix in CollectionTypePrefixes)
        {
            if (typeName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        foreach (var name in CollectionTypeNames)
        {
            if (typeName.IndexOf(name, StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }

    public IVisualizerCommand CreateVisualizerCommand(VariableInfo variable, Func<VariableInfo?> reevaluate)
        => new GridVisualizerCommand(variable, reevaluate);
}

public sealed class GridVisualizerCommand : IVisualizerCommand
{
    private readonly VariableInfo _variable;
    private readonly Func<VariableInfo?> _reevaluate;

    public GridVisualizerCommand(VariableInfo variable, Func<VariableInfo?> reevaluate)
    {
        _variable = variable;
        _reevaluate = reevaluate;
    }

    public void Execute()
    {
        _ = ShowWindowAsync();
    }

    private async Task ShowWindowAsync()
    {
        if (_variable.VariablesReference == 0)
        {
            // Try re-evaluating to get a full value with children
            var full = _reevaluate();
            if (full is null || full.VariablesReference == 0)
                return;
        }

        var children = await GetChildrenAsync();
        if (children.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = $"Collection Visualizer - {_variable.Name}",
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = MainPage.Current?.XamlRoot,
            Content = BuildListView(children),
        };
        _ = dialog.ShowAsync();
    }

    private async Task<IReadOnlyList<VariableInfo>> GetChildrenAsync()
    {
        var svc = MainPage.Current?.DebugService;
        if (svc is null) return Array.Empty<VariableInfo>();

        // The collection may be nested under indexed children; get first level
        if (_variable.VariablesReference == 0)
        {
            var full = _reevaluate();
            if (full is null || full.VariablesReference == 0)
                return Array.Empty<VariableInfo>();
            return await svc.GetChildrenAsync(full.VariablesReference);
        }
        return await svc.GetChildrenAsync(_variable.VariablesReference);
    }

    private static UIElement BuildListView(IReadOnlyList<VariableInfo> items)
    {
        var listView = new ListView
        {
            ItemsSource = items.Select((v, i) => new GridItem
            {
                Index = i,
                Name = v.Name,
                Value = v.Value,
                Type = v.Type,
            }).ToList(),
            SelectionMode = ListViewSelectionMode.None,
            Margin = new Thickness(4),
        };

        var template = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(50) },
                    new ColumnDefinition { Width = new GridLength(120) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(100) },
                },
                Padding = new Thickness(4, 2),
            };
            var idx = new TextBlock();
            idx.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Index") });
            grid.Children.Add(idx);
            var name = new TextBlock();
            name.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Name") });
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var val = new TextBlock();
            val.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value") });
            Grid.SetColumn(val, 2);
            grid.Children.Add(val);
            var typ = new TextBlock();
            typ.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Type") });
            Grid.SetColumn(typ, 3);
            grid.Children.Add(typ);
            return grid;
        });
        listView.ItemTemplate = template;

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(50) },
                new ColumnDefinition { Width = new GridLength(120) },
                new ColumnDefinition { Width = new GridLength(200) },
                new ColumnDefinition { Width = new GridLength(100) },
            },
            Padding = new Thickness(8, 4),
        };
        header.Children.Add(new TextBlock { Text = "#", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 1);
        header.Children.Add(new TextBlock { Text = "Value", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 2);
        header.Children.Add(new TextBlock { Text = "Type", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 3);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(listView);
        return stack;
    }

    private sealed record GridItem
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
        public string Type { get; init; } = "";
    }
}
