using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Debugger;

public sealed class LocalsPad : UserControl
{
    private readonly TreeView _treeView;
    private IDebuggerService? _debugger;
    private int _activeThreadId;
    private int _activeFrameId;

    /// <summary>
    /// Optional factory: given a VariableInfo, returns visualizer (name, action) pairs
    /// to populate the picker button. Set by the host app (SharpDevelop) during startup.
    /// </summary>
    public static Func<VariableInfo, IReadOnlyList<(string Name, Action Execute)>?>? GetVisualizerActions { get; set; }

    public LocalsPad()
    {
        _treeView = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            Margin = new Thickness(4),
        };
        AutomationProperties.SetAutomationId(_treeView, "locals-tree");
        _treeView.Expanding += OnNodeExpanding;
        Content = BuildLayout();
    }

    private UIElement BuildLayout()
    {
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(200) },
                new ColumnDefinition { Width = new GridLength(100) },
                new ColumnDefinition { Width = new GridLength(32) },
            },
            Padding = new Thickness(8, 4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        header.Children.Add(new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Value", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 1);
        header.Children.Add(new TextBlock { Text = "Type", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 2);

        var template = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(100) },
                    new ColumnDefinition { Width = new GridLength(32) },
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
            // Visualizer picker button — hidden by default, shown by OnVisualizerButtonLoaded
            var pickerButton = new Button
            {
                Content = "\u2026", // ellipsis character
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                FontSize = 14,
                Visibility = Microsoft.UI.Xaml.Visibility.Collapsed,
            };
            pickerButton.Loaded += (s, _) => OnVisualizerButtonLoaded(s as Button);
            Grid.SetColumn(pickerButton, 3);
            grid.Children.Add(pickerButton);
            return grid;
        });
        _treeView.ItemTemplate = template;

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_treeView);
        return stack;
    }

    private static void OnVisualizerButtonLoaded(Button? button)
    {
        if (button is null) return;
        // Walk up to the TreeViewNode via the DataContext chain
        // The button's DataContext is the node's Content (LocalItem)
        if (button.DataContext is not LocalItem item) return;
        var actions = GetVisualizerActions?.Invoke(item.Info);
        if (actions is null || actions.Count == 0) return;

        button.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        button.Click += (s, e) =>
        {
            var flyout = new MenuFlyout();
            foreach (var (name, execute) in actions)
            {
                var item = new MenuFlyoutItem { Text = name };
                item.Click += (_, _) => execute();
                flyout.Items.Add(item);
            }
            flyout.ShowAt(button);
        };
    }

    public void Attach(IDebuggerService debugger)
    {
        _debugger = debugger;
        debugger.Stopped += OnStopped;
        debugger.Continued += OnContinued;
        debugger.DebugStopped += (_, _) => DispatcherQueue.TryEnqueue(() => _treeView.RootNodes.Clear());
    }

    private void OnStopped(int threadId, string reason)
    {
        _activeThreadId = threadId;
        _ = RefreshAsync(threadId);
    }

    private void OnContinued()
    {
        DispatcherQueue.TryEnqueue(() => _treeView.RootNodes.Clear());
    }

    private async Task RefreshAsync(int threadId)
    {
        if (_debugger is null) return;
        var frames = await _debugger.GetStackFramesAsync(threadId);
        if (frames.Count == 0) return;

        _activeFrameId = frames[0].Id;
        var variables = await _debugger.GetLocalsAsync(_activeFrameId);

        DispatcherQueue.TryEnqueue(() =>
        {
            _treeView.RootNodes.Clear();
            foreach (var v in variables)
                _treeView.RootNodes.Add(CreateNode(v));
        });
    }

    private static TreeViewNode CreateNode(VariableInfo v)
    {
        var node = new TreeViewNode
        {
            Content = new LocalItem(v),
            HasUnrealizedChildren = v.VariablesReference > 0,
        };
        return node;
    }

    public Task<IReadOnlyList<object>> GetSnapshotAsync()
    {
        var snapshot = _treeView.RootNodes
            .Select(node => node.Content)
            .OfType<LocalItem>()
            .Select(item => (object)new
            {
                Name = item.Name,
                Value = item.Value,
                Type = item.Type,
                VariablesReference = item.Info.VariablesReference
            })
            .ToArray();
        return Task.FromResult<IReadOnlyList<object>>(snapshot);
    }

    private async void OnNodeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node is not TreeViewNode node) return;
        if (node.Content is not LocalItem item || item.Info.VariablesReference == 0) return;
        if (node.Children.Count > 0) return; // already loaded

        if (_debugger is null) return;
        var children = await _debugger.GetChildrenAsync(item.Info.VariablesReference);
        if (children.Count == 0) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var child in children)
                node.Children.Add(CreateNode(child));
        });
    }

    public new void Dispose()
    {
        if (_debugger is not null)
        {
            _debugger.Stopped -= OnStopped;
            _debugger.Continued -= OnContinued;
        }
    }

    private sealed class LocalItem
    {
        public LocalItem(VariableInfo v) => Info = v;
        public VariableInfo Info { get; }
        public string Name => Info.Name;
        public string Value => Info.Value;
        public string Type => Info.Type;
    }
}
