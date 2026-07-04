using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.OptionPanels;

internal sealed class TextViewOptions : OptionPanel
{
    public TextViewOptions()
    {
        DataContext = UnoCodeEditorOptions.Instance;

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(8),
                Spacing = 6,
                Children =
                {
                    CreateMarkersGroup(),
                    CreateRulerGroup(),
                }
            }
        };
    }

    private static FrameworkElement CreateMarkersGroup()
    {
        var showSpaces = CreateCheckBox("Show spaces", nameof(UnoCodeEditorOptions.ShowSpaces));
        var showTabs = CreateCheckBox("Show tabs", nameof(UnoCodeEditorOptions.ShowTabs));
        var showEol = CreateCheckBox("Show end-of-line markers", nameof(UnoCodeEditorOptions.ShowEndOfLine));
        var underline = CreateCheckBox("Underline errors", nameof(UnoCodeEditorOptions.UnderlineErrors));
        var highlightBrackets = CreateCheckBox("Highlight brackets", nameof(UnoCodeEditorOptions.HighlightBrackets));
        var highlightLine = CreateCheckBox("Highlight current line", nameof(UnoCodeEditorOptions.HighlightCurrentLine));
        var highlightSymbol = CreateCheckBox("Highlight symbol", nameof(UnoCodeEditorOptions.HighlightSymbol));
        var animations = CreateCheckBox("Enable animations", nameof(UnoCodeEditorOptions.EnableAnimations));

        return CreateGroupBox("Markers",
            showSpaces, showTabs, showEol, underline,
            highlightBrackets, highlightLine, highlightSymbol, animations);
    }

    private static FrameworkElement CreateRulerGroup()
    {
        var rulerCheck = CreateCheckBox("Show column ruler", nameof(UnoCodeEditorOptions.ShowColumnRuler));

        var rulerPosBox = new NumberBox
        {
            Minimum = 1,
            Maximum = 999,
            Value = UnoCodeEditorOptions.Instance.ColumnRulerPosition,
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        rulerPosBox.ValueChanged += (_, args) =>
            UnoCodeEditorOptions.Instance.ColumnRulerPosition = (int)args.NewValue;

        return CreateGroupBox("Ruler", rulerCheck, CreateRow("Column position:", rulerPosBox));
    }

    private static FrameworkElement CreateRow(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        });
        panel.Children.Add(control);
        return panel;
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
