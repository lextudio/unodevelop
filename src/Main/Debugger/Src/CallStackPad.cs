using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace UnoDevelop.Debugger;

public sealed class CallStackPad : UserControl
{
    private readonly ListView _listView;
    private readonly ObservableCollection<StackFrameItem> _frames = new();
    private IDebuggerService? _debugger;
    private int _activeThreadId;

    /// Fired (filePath, 1-based line) when the user activates a frame with source info.
    public event Action<string, int>? FrameActivated;

    public CallStackPad()
    {
        _listView = new ListView
        {
            ItemsSource = _frames,
            SelectionMode = ListViewSelectionMode.Single,
            Margin = new Thickness(4),
        };
        AutomationProperties.SetAutomationId(_listView, "call-stack-list");
        _listView.DoubleTapped += OnFrameDoubleTapped;

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(300) },
                new ColumnDefinition { Width = new GridLength(150) },
            },
            Padding = new Thickness(8, 4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        header.Children.Add(new TextBlock { Text = "Name", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = "Location", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        Grid.SetColumn(header.Children[^1], 1);

        var template = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(300) },
                    new ColumnDefinition { Width = new GridLength(150) },
                },
                Padding = new Thickness(4, 2),
            };
            var nameBlock = new TextBlock();
            nameBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Name") });
            nameBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            grid.Children.Add(nameBlock);
            var locBlock = new TextBlock();
            locBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Location") });
            locBlock.Opacity = 0.72;
            Grid.SetColumn(locBlock, 1);
            grid.Children.Add(locBlock);
            return grid;
        });
        _listView.ItemTemplate = template;

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_listView);
        Content = stack;
    }

    private void OnFrameDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_listView.SelectedItem is StackFrameItem item && item.HasSource)
            FrameActivated?.Invoke(item.FilePath!, item.Line);
    }

    public void Attach(IDebuggerService debugger)
    {
        _debugger = debugger;
        debugger.Stopped += OnStopped;
        debugger.Continued += OnContinued;
        debugger.DebugStopped += (_, _) => DispatcherQueue.TryEnqueue(() => _frames.Clear());
    }

    private void OnStopped(int threadId, string reason)
    {
        _activeThreadId = threadId;
        _ = RefreshAsync(threadId);
    }

    private void OnContinued()
    {
        DispatcherQueue.TryEnqueue(() => _frames.Clear());
    }

    private async Task RefreshAsync(int threadId)
    {
        if (_debugger is null) return;
        var frames = await _debugger.GetStackFramesAsync(threadId);
        DispatcherQueue.TryEnqueue(() =>
        {
            _frames.Clear();
            foreach (var f in frames)
                _frames.Add(new StackFrameItem(f));
        });
    }

    public Task<IReadOnlyList<object>> GetSnapshotAsync()
    {
        IReadOnlyList<object> snapshot = _frames
            .Select(f => (object)new
            {
                Name = f.Name,
                File = f.FilePath,
                Line = f.Line,
                Location = f.Location
            })
            .ToArray();
        return Task.FromResult(snapshot);
    }

    public new void Dispose()
    {
        if (_debugger is not null)
        {
            _debugger.Stopped -= OnStopped;
            _debugger.Continued -= OnContinued;
        }
    }

    private sealed class StackFrameItem
    {
        private readonly StackFrameInfo _frame;
        public StackFrameItem(StackFrameInfo frame) => _frame = frame;
        public string Name => _frame.Name;
        public string? FilePath => _frame.FilePath;
        public int Line => _frame.Line;
        public bool HasSource => !string.IsNullOrEmpty(_frame.FilePath) && _frame.Line > 0;
        public string Location => _frame.FilePath is { } p
            ? $"{Path.GetFileName(p)}:{_frame.Line}"
            : string.Empty;
    }
}
