using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Debugger;

public sealed class WatchPad : UserControl
{
    private readonly ListView _listView;
    private readonly ObservableCollection<WatchItem> _watches = new();
    private readonly TextBox _expressionInput;
    private IDebuggerService? _debugger;

    public WatchPad()
    {
        _expressionInput = new TextBox
        {
            PlaceholderText = "Type expression and press Enter to add watch...",
            Margin = new Thickness(4, 4, 4, 0),
        };
        AutomationProperties.SetAutomationId(_expressionInput, "watch-input");
        _expressionInput.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                AddWatch(_expressionInput.Text);
                _expressionInput.Text = string.Empty;
            }
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(160) },
                new ColumnDefinition { Width = new GridLength(200) },
                new ColumnDefinition { Width = new GridLength(100) },
            },
            Padding = new Thickness(8, 4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        header.Children.Add(new TextBlock { Text = "Expression", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
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
                    new ColumnDefinition { Width = new GridLength(160) },
                    new ColumnDefinition { Width = new GridLength(200) },
                    new ColumnDefinition { Width = new GridLength(100) },
                },
                Padding = new Thickness(4, 2),
            };
            var expr = new TextBlock();
            expr.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Expression") });
            expr.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace");
            grid.Children.Add(expr);
            var val = new TextBlock();
            val.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("DisplayValue") });
            val.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace");
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);
            var typ = new TextBlock();
            typ.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Type") });
            typ.Opacity = 0.6;
            Grid.SetColumn(typ, 2);
            grid.Children.Add(typ);
            return grid;
        });
        _listView = new ListView
        {
            ItemsSource = _watches,
            SelectionMode = ListViewSelectionMode.Single,
            Margin = new Thickness(4, 0, 4, 4),
            ItemTemplate = template,
        };
        AutomationProperties.SetAutomationId(_listView, "watch-list");
        _listView.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Delete && _listView.SelectedItem is WatchItem item)
            {
                _watches.Remove(item);
            }
        };

        var stack = new StackPanel();
        stack.Children.Add(_expressionInput);
        stack.Children.Add(header);
        stack.Children.Add(_listView);
        Content = stack;
    }

    public void Attach(IDebuggerService debugger)
    {
        _debugger = debugger;
        debugger.Stopped += OnStopped;
        debugger.Continued += OnContinued;
        debugger.DebugStopped += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var w in _watches) w.ClearValue();
        });
    }

    public void AddWatch(string expression)
    {
        var trimmed = expression?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        if (_watches.Any(w => w.Expression == trimmed)) return;
        _watches.Add(new WatchItem(trimmed));
    }

    private void OnStopped(int threadId, string reason)
    {
        _ = RefreshAllAsync();
    }

    private void OnContinued()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var w in _watches) w.ClearValue();
        });
    }

    private async Task RefreshAllAsync()
    {
        if (_debugger is null) return;
        foreach (var w in _watches.ToList())
        {
            var result = await _debugger.EvaluateAsync(w.Expression);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (result is not null)
                    w.SetValue(result.Value, result.Type);
                else
                    w.SetError("Unable to evaluate");
            });
        }
    }

    public new void Dispose()
    {
        if (_debugger is not null)
        {
            _debugger.Stopped -= OnStopped;
            _debugger.Continued -= OnContinued;
        }
    }

    private sealed class WatchItem
    {
        public string Expression { get; }
        public string DisplayValue { get; private set; } = string.Empty;
        public string Type { get; private set; } = string.Empty;

        public WatchItem(string expression) => Expression = expression;

        public void SetValue(string value, string type)
        {
            DisplayValue = value;
            Type = type;
        }

        public void SetError(string error)
        {
            DisplayValue = $"<{error}>";
            Type = string.Empty;
        }

        public void ClearValue()
        {
            DisplayValue = string.Empty;
            Type = string.Empty;
        }
    }
}
