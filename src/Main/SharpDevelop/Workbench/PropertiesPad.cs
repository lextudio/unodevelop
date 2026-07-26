using Microsoft.UI.Xaml.Controls;
using UnoPropertyGrid;

namespace UnoDevelop.Workbench;

// Properties pad backed by UnoPropertyGrid, mirroring WPF SharpDevelop's
// PropertyPad shown by the Solution Explorer "Properties" toolbar button.
public sealed class PropertiesPad : UserControl
{
    private readonly PropertyGridControl _grid;
    private object? _selectedObject;

    public PropertiesPad()
    {
        _grid = new PropertyGridControl
        {
            NameColumnWidth = 140,
            ShowDescriptionPane = true
        };
        Content = _grid;
    }

    public object? SelectedObject => _selectedObject;

    public void SetSelectedObject(object? value)
    {
        _selectedObject = value;
        _grid.SelectedObject = value;
    }

    public object GetSnapshot()
    {
        var element = _selectedObject as Microsoft.UI.Xaml.FrameworkElement;
        return new
        {
            SelectedType = _selectedObject?.GetType().Name,
            Name = element?.Name,
            Width = FiniteOrNull(element?.Width),
            Height = FiniteOrNull(element?.Height)
        };
    }

    private static double? FiniteOrNull(double? value)
        => value is { } number && double.IsFinite(number) ? number : null;
}
