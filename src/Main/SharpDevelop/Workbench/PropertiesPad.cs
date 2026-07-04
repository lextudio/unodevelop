using Microsoft.UI.Xaml.Controls;
using UnoPropertyGrid;

namespace UnoDevelop.Workbench;

// Properties pad backed by UnoPropertyGrid, mirroring WPF SharpDevelop's
// PropertyPad shown by the Solution Explorer "Properties" toolbar button.
public sealed class PropertiesPad : UserControl
{
    private readonly PropertyGridControl _grid;

    public PropertiesPad()
    {
        _grid = new PropertyGridControl
        {
            NameColumnWidth = 140,
            ShowDescriptionPane = true
        };
        Content = _grid;
    }

    public void SetSelectedObject(object? value) => _grid.SelectedObject = value;
}
