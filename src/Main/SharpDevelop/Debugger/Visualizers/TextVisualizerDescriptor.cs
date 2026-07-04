using System;
using ICSharpCode.SharpDevelop.Debugging;
using UnoDevelop;

namespace UnoDevelop.Debugger.Visualizers;

public sealed class TextVisualizerDescriptor : IVisualizerDescriptor
{
    public bool IsVisualizerAvailable(string typeName)
        => string.Equals(typeName, "string", StringComparison.OrdinalIgnoreCase)
        || string.Equals(typeName, "System.String", StringComparison.Ordinal);

    public IVisualizerCommand CreateVisualizerCommand(VariableInfo variable, Func<VariableInfo?> reevaluate)
        => new TextVisualizerCommand(variable, reevaluate);
}

public sealed class TextVisualizerCommand : IVisualizerCommand
{
    private readonly VariableInfo _variable;
    private readonly Func<VariableInfo?> _reevaluate;

    public TextVisualizerCommand(VariableInfo variable, Func<VariableInfo?> reevaluate)
    {
        _variable = variable;
        _reevaluate = reevaluate;
    }

    public void Execute()
    {
        var value = _variable.Value;
        // If truncated (DAP often truncates at ~1KB), try to get full value
        if (_variable.EvaluateName is not null && value.EndsWith("..."))
        {
            var full = _reevaluate();
            if (full is not null)
                value = full.Value;
        }
        _ = ShowWindowAsync(value);
    }

    private static async System.Threading.Tasks.Task ShowWindowAsync(string text)
    {
        // Use a simple ContentDialog for the text visualizer.
        // For long text we could use a full window, but ContentDialog
        // avoids the complexity of creating a new Window in Uno/WinUI.
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Text Visualizer",
            Content = new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
                Content = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = text,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Courier New, monospace"),
                    Margin = new Microsoft.UI.Xaml.Thickness(4),
                    IsTextSelectionEnabled = true,
                    MaxWidth = 600,
                }
            },
            PrimaryButtonText = "Close",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
        };
        // Must have an XamlRoot set on WinUI
        dialog.XamlRoot = MainPage.Current?.XamlRoot;
        await dialog.ShowAsync();
    }
}
