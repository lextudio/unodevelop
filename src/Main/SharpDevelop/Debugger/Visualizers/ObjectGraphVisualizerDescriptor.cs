using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Debugging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnoDevelop.Debugger;

namespace UnoDevelop.Debugger.Visualizers;

public sealed class ObjectGraphVisualizerDescriptor : IVisualizerDescriptor
{
    public bool IsVisualizerAvailable(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        return true;
    }

    public IVisualizerCommand CreateVisualizerCommand(VariableInfo variable, Func<VariableInfo?> reevaluate)
        => new ObjectGraphVisualizerCommand(variable, reevaluate);
}

public sealed class ObjectGraphVisualizerCommand : IVisualizerCommand
{
    private readonly VariableInfo _variable;
    private readonly Func<VariableInfo?> _reevaluate;

    public ObjectGraphVisualizerCommand(VariableInfo variable, Func<VariableInfo?> reevaluate)
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
        var varRef = _variable.VariablesReference;
        if (varRef == 0)
        {
            var full = _reevaluate();
            if (full is null || full.VariablesReference == 0) return;
            varRef = full.VariablesReference;
        }

        var svc = MainPage.Current?.DebugService;
        if (svc is null) return;

        var rootChildren = await svc.GetChildrenAsync(varRef);
        if (rootChildren.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = $"Object Graph - {_variable.Name} ({_variable.Type})",
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = MainPage.Current?.XamlRoot,
            Content = BuildTree(svc, rootChildren),
        };
        _ = dialog.ShowAsync();
    }

    private static UIElement BuildTree(IDebuggerService svc, IReadOnlyList<VariableInfo> children)
    {
        var treeView = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            Margin = new Thickness(4),
        };

        var template = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(180) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(120) },
                },
                Padding = new Thickness(4, 2),
            };
            var nameBlock = new TextBlock();
            nameBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Name") });
            nameBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            grid.Children.Add(nameBlock);
            var valueBlock = new TextBlock();
            valueBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value") });
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
            var typeBlock = new TextBlock();
            typeBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Type") });
            typeBlock.Opacity = 0.6;
            Grid.SetColumn(typeBlock, 2);
            grid.Children.Add(typeBlock);
            return grid;
        });
        treeView.ItemTemplate = template;

        treeView.Expanding += async (s, args) =>
        {
            if (args.Node is not TreeViewNode node) return;
            if (node.Content is not GraphItem item || item.Variable.VariablesReference == 0) return;
            if (node.Children.Count > 0) return;

            var grandChildren = await svc.GetChildrenAsync(item.Variable.VariablesReference);
            if (grandChildren.Count == 0) return;

            foreach (var child in grandChildren)
                node.Children.Add(new TreeViewNode
                {
                    Content = new GraphItem(child),
                    HasUnrealizedChildren = child.VariablesReference > 0,
                });
        };

        foreach (var child in children)
            treeView.RootNodes.Add(new TreeViewNode
            {
                Content = new GraphItem(child),
                HasUnrealizedChildren = child.VariablesReference > 0,
            });

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(180) },
                new ColumnDefinition { Width = new GridLength(200) },
                new ColumnDefinition { Width = new GridLength(120) },
            },
            Padding = new Thickness(8, 4),
        };
        header.Children.Add(new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Value", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 1);
        header.Children.Add(new TextBlock { Text = "Type", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 2);

        var stack = new StackPanel { MinWidth = 520 };
        stack.Children.Add(header);
        stack.Children.Add(treeView);
        return stack;
    }

    private sealed record GraphItem(VariableInfo Variable)
    {
        public string Name => Variable.Name;
        public string Value => Variable.Value;
        public string Type => Variable.Type;
    }
}
