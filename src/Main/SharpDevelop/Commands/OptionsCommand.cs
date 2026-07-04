using System.Threading.Tasks;
using ICSharpCode.Core;
using UnoDevelop.OptionPanels;

namespace UnoDevelop.Commands;

internal sealed class OptionsCommand : AbstractMenuCommand
{
    public override void Run()
    {
        _ = ShowAsync();
    }

    private static async Task ShowAsync()
    {
        var node = AddInTree.GetTreeNode("/SharpDevelop/Dialogs/OptionsDialog");
        var panels = node.BuildChildItems<IOptionPanelDescriptor>(null);
        var dialog = new OptionsDialog(panels, "OptionsDialog");
        dialog.XamlRoot = MainPage.Current?.XamlRoot;
        await dialog.ShowAsync();
    }
}
