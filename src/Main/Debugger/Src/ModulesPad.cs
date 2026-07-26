using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Debugger;

public sealed class ModulesPad : UserControl
{
    private readonly ListView _listView;
    private readonly ObservableCollection<ModuleItem> _modules = new();
    private IDebuggerService? _debugger;

    public ModulesPad()
    {
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(200) },
                new ColumnDefinition { Width = new GridLength(120) },
                new ColumnDefinition { Width = new GridLength(200) },
            },
            Padding = new Thickness(8, 4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        header.Children.Add(new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Optimized", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 1);
        header.Children.Add(new TextBlock { Text = "Path", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 2);

        var template = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(120) },
                    new ColumnDefinition { Width = new GridLength(200) },
                },
                Padding = new Thickness(4, 2),
            };
            var name = new TextBlock();
            name.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("DisplayName") });
            name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            grid.Children.Add(name);
            var opt = new TextBlock();
            opt.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Optimized") });
            Grid.SetColumn(opt, 1);
            grid.Children.Add(opt);
            var path = new TextBlock();
            path.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("ModulePath") });
            path.TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis;
            path.Opacity = 0.72;
            Grid.SetColumn(path, 2);
            grid.Children.Add(path);
            return grid;
        });
        _listView = new ListView
        {
            ItemsSource = _modules,
            SelectionMode = ListViewSelectionMode.None,
            ItemTemplate = template,
            Margin = new Thickness(4),
        };
        AutomationProperties.SetAutomationId(_listView, "modules-list");

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_listView);
        Content = stack;
    }

    public void Attach(IDebuggerService debugger)
    {
        _debugger = debugger;
        debugger.Stopped += OnStopped;
        debugger.DebugStopped += OnSessionEnded;
        debugger.Continued += OnContinued;
    }

    private void OnStopped(int threadId, string reason) => _ = RefreshAsync();
    private void OnContinued() => DispatcherQueue.TryEnqueue(() => _modules.Clear());
    private void OnSessionEnded(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => _modules.Clear());

    private async Task RefreshAsync()
    {
        if (_debugger is null) return;
        var modules = await _debugger.GetModulesAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            _modules.Clear();
            foreach (var m in modules)
                _modules.Add(new ModuleItem(m));
        });
    }

    public Task<IReadOnlyList<object>> GetSnapshotAsync()
    {
        IReadOnlyList<object> snapshot = _modules
            .Select(m => (object)new
            {
                Name = m.DisplayName,
                Path = m.ModulePath,
                Optimized = m.Optimized
            })
            .ToArray();
        return Task.FromResult(snapshot);
    }

    public new void Dispose()
    {
        if (_debugger is not null)
        {
            _debugger.Stopped -= OnStopped;
            _debugger.DebugStopped -= OnSessionEnded;
            _debugger.Continued -= OnContinued;
        }
    }

    private sealed class ModuleItem
    {
        private readonly ModuleInfo _m;
        public ModuleItem(ModuleInfo m) => _m = m;
        public string DisplayName => _m.Name;
        public string Optimized => _m.IsOptimized ? "Yes" : "No";
        public string ModulePath => _m.Path ?? string.Empty;
    }
}
