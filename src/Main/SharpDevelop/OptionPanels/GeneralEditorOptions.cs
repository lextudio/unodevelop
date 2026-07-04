using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.OptionPanels;

internal sealed class GeneralEditorOptions : OptionPanel
{
    public GeneralEditorOptions()
    {
        DataContext = UnoCodeEditorOptions.Instance;

        var wordWrap = CreateCheckBox("Word wrap", nameof(UnoCodeEditorOptions.WordWrap));
        var folding = CreateCheckBox("Enable code folding", nameof(UnoCodeEditorOptions.EnableFolding));
        var changeMarker = CreateCheckBox("Show change marker margin", nameof(UnoCodeEditorOptions.EnableChangeMarkerMargin));
        var lineNumbers = CreateCheckBox("Show line numbers", nameof(UnoCodeEditorOptions.ShowLineNumbers));
        var hiddenDefs = CreateCheckBox("Show hidden definitions", nameof(UnoCodeEditorOptions.ShowHiddenDefinitions));

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(8),
                Spacing = 6,
                Children =
                {
                    CreateGroupBox("General", wordWrap, folding, changeMarker),
                    CreateGroupBox("Display", lineNumbers, hiddenDefs),
                }
            }
        };
    }

    private static FrameworkElement CreateGroupBox(string header, params FrameworkElement[] children)
    {
        var stack = new StackPanel { Spacing = 4 };
        foreach (var c in children)
            stack.Children.Add(c);

        return new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = header,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 4)
                    },
                    stack
                }
            }
        };
    }

    private static CheckBox CreateCheckBox(string text, string bindingPath)
    {
        var cb = new CheckBox { Content = text, Margin = new Thickness(2) };
        cb.SetBinding(Microsoft.UI.Xaml.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath(bindingPath),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay
            });
        return cb;
    }
}
