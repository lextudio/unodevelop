using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LeXtudioRichTextBlock = LeXtudio.UI.Xaml.Controls.RichTextBlock;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Paragraph = System.Windows.Documents.Paragraph;
using Run = System.Windows.Documents.Run;

namespace UnoDevelop.Debugger;

public sealed class ImmediatePad : UserControl
{
    private readonly LeXtudioRichTextBlock _output;
    private readonly TextBox _input;
    private readonly ScrollViewer _scrollViewer;
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private IDebuggerService? _debugger;
    private int _activeFrameId;

    public ImmediatePad()
    {
        _output = new LeXtudioRichTextBlock
        {
            Margin = new Thickness(8, 4),
            FontFamily = new FontFamily("Consolas, monospace"),
            IsTextSelectionEnabled = true,
        };
        AutomationProperties.SetAutomationId(_output, "immediate-output");
        _scrollViewer = new ScrollViewer
        {
            Content = _output,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
        };

        var prompt = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4),
        };
        prompt.Children.Add(new TextBlock
        {
            Text = "> ",
            FontFamily = new FontFamily("Consolas, monospace"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _input = new TextBox
        {
            PlaceholderText = "Type expression and press Enter...",
            FontFamily = new FontFamily("Consolas, monospace"),
        };
        AutomationProperties.SetAutomationId(_input, "immediate-input");
        _input.KeyDown += OnInputKeyDown;
        prompt.Children.Add(_input);

        var splitter = new Border
        {
            Height = 3,
            Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        };

        var rootSplit = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(2, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
        };
        Grid.SetRow(_scrollViewer, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(prompt, 2);
        rootSplit.Children.Add(_scrollViewer);
        rootSplit.Children.Add(splitter);
        rootSplit.Children.Add(prompt);
        Content = rootSplit;
    }

    public void Attach(IDebuggerService debugger)
    {
        _debugger = debugger;
        debugger.Stopped += (threadId, reason) =>
        {
            if (_debugger is not null)
                Task.Run(() => ResolveFrameAsync(threadId));
        };
    }

    private async Task ResolveFrameAsync(int threadId)
    {
        if (_debugger is null) return;
        var frames = await _debugger.GetStackFramesAsync(threadId);
        if (frames.Count > 0)
            _activeFrameId = frames[0].Id;
    }

    private void OnInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var text = _input.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _history.Add(text);
            _historyIndex = _history.Count;
            _input.Text = string.Empty;
            _ = EvaluateExpressionAsync(text);
        }
        else if (e.Key == VirtualKey.Up)
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                _input.Text = _history[_historyIndex];
                _input.Select(_input.Text.Length, 0);
            }
        }
        else if (e.Key == VirtualKey.Down)
        {
            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                _input.Text = _history[_historyIndex];
            }
            else
            {
                _historyIndex = _history.Count;
                _input.Text = string.Empty;
            }
            _input.Select(_input.Text.Length, 0);
        }
    }

    private async Task EvaluateExpressionAsync(string expression)
    {
        AppendParagraph($"> {expression}", isCommand: true);
        if (_debugger is null)
        {
            AppendParagraph("  <not debugging>", isError: true);
            return;
        }
        try
        {
            var result = await _debugger.EvaluateAsync(expression, _activeFrameId);
            if (result is not null)
            {
                AppendParagraph($"  = {result.Value}");
                if (!string.IsNullOrEmpty(result.Type))
                    AppendParagraph($"  ({result.Type})", isDimmed: true);
            }
            else
            {
                AppendParagraph("  Unable to evaluate expression", isError: true);
            }
        }
        catch (Exception ex)
        {
            AppendParagraph($"  Error: {ex.Message}", isError: true);
        }
    }

    private void AppendParagraph(string text, bool isCommand = false, bool isError = false, bool isDimmed = false)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var para = new Paragraph { Margin = new Thickness(0) };
            var run = new Run { Text = text };
            if (isCommand)
                run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            if (isError)
                run.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            if (isDimmed)
                run.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            para.Inlines.Add(run);
            _output.Blocks.Add(para);
            _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
        });
    }

    public new void Dispose() { }
}
