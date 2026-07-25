using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Debugger;

public sealed class ThreadsPad : UserControl
{
    private readonly ListView _listView;
    private readonly ObservableCollection<ThreadItem> _threads = new();
    private IDebuggerService? _debugger;

    public ThreadsPad()
    {
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(60) },
                new ColumnDefinition { Width = new GridLength(200) },
            },
            Padding = new Thickness(8, 4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        header.Children.Add(new TextBlock { Text = "ID", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 1);

        var template = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(60) },
                    new ColumnDefinition { Width = new GridLength(200) },
                },
                Padding = new Thickness(4, 2),
            };
            var id = new TextBlock();
            id.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Id") });
            id.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace");
            grid.Children.Add(id);
            var name = new TextBlock();
            name.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("DisplayName") });
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            return grid;
        });
        _listView = new ListView
        {
            ItemsSource = _threads,
            SelectionMode = ListViewSelectionMode.None,
            ItemTemplate = template,
            Margin = new Thickness(4),
        };
        AutomationProperties.SetAutomationId(_listView, "threads-list");

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
    private void OnContinued() => DispatcherQueue.TryEnqueue(() => _threads.Clear());

    private void OnSessionEnded(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => _threads.Clear());

    private async Task RefreshAsync()
    {
        if (_debugger is null) return;
        var threads = await _debugger.GetThreadsAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            _threads.Clear();
            foreach (var t in threads)
                _threads.Add(new ThreadItem(t));
        });
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

    private sealed class ThreadItem
    {
        private readonly ThreadInfo _t;
        public ThreadItem(ThreadInfo t) => _t = t;
        public string Id => _t.Id.ToString();
        public string DisplayName => _t.Name;
    }
}
