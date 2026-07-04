using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.OptionPanels;

internal sealed class BehaviorOptions : OptionPanel
{
    public BehaviorOptions()
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
                    CreateTabsGroup(),
                    CreateBehaviorGroup(),
                }
            }
        };
    }

    private static FrameworkElement CreateTabsGroup()
    {
        var indentSizeBox = new NumberBox
        {
            Minimum = 1,
            Maximum = 16,
            Value = UnoCodeEditorOptions.Instance.IndentationSize,
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4)
        };
        indentSizeBox.ValueChanged += (_, args) =>
            UnoCodeEditorOptions.Instance.IndentationSize = (int)args.NewValue;

        var convertTabs = CreateCheckBox("Convert tabs to spaces", nameof(UnoCodeEditorOptions.ConvertTabsToSpaces));
        var smartIndent = CreateCheckBox("Use smart indentation", nameof(UnoCodeEditorOptions.UseSmartIndentation));

        return CreateGroupBox("Tabs",
            CreateRow("Indentation size:", indentSizeBox),
            convertTabs,
            smartIndent);
    }

    private static FrameworkElement CreateBehaviorGroup()
    {
        var mouseWheel = CreateCheckBox("Mouse wheel zoom", nameof(UnoCodeEditorOptions.MouseWheelZoom));
        var hideCursor = CreateCheckBox("Hide cursor while typing", nameof(UnoCodeEditorOptions.HideCursorWhileTyping));
        var cutCopy = CreateCheckBox("Cut/copy whole line", nameof(UnoCodeEditorOptions.CutCopyWholeLine));
        var virtualSpace = CreateCheckBox("Enable virtual space", nameof(UnoCodeEditorOptions.EnableVirtualSpace));
        var ctrlClick = CreateCheckBox("Ctrl+click go to definition", nameof(UnoCodeEditorOptions.CtrlClickGoToDefinition));
        var autoBlock = CreateCheckBox("Auto-insert block end", nameof(UnoCodeEditorOptions.AutoInsertBlockEnd));

        return CreateGroupBox("Behavior", mouseWheel, hideCursor, cutCopy, virtualSpace, ctrlClick, autoBlock);
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
