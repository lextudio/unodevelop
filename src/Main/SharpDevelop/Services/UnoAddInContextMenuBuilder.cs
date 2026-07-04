using System;
using System.Collections;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;
using ICSharpCode.Core;

namespace UnoDevelop.Services;

internal interface IUnoAddInContextMenuBuilder
{
    MenuFlyout CreateContextMenu(object owner, string addInTreePath);
}

internal sealed class UnoAddInContextMenuBuilder : IUnoAddInContextMenuBuilder
{
    public MenuFlyout CreateContextMenu(object owner, string addInTreePath)
    {
        var flyout = new MenuFlyout();
        var descriptors = AddInTree.BuildItems<MenuItemDescriptor>(addInTreePath, owner, false);
        AddItems(flyout.Items, descriptors);
        return flyout;
    }

    private void AddItems(IList<MenuFlyoutItemBase> target, IEnumerable descriptors)
    {
        foreach (var descriptorObject in descriptors)
        {
            if (descriptorObject is not MenuItemDescriptor descriptor)
            {
                continue;
            }

            var item = CreateMenuItem(descriptor);
            if (item is not null)
            {
                target.Add(item);
            }
        }
    }

    private MenuFlyoutItemBase? CreateMenuItem(MenuItemDescriptor descriptor)
    {
        var codon = descriptor.Codon;
        var type = codon.Properties.Contains("type") ? codon.Properties["type"] : "Command";
        var failedAction = Condition.GetFailedAction(descriptor.Conditions, descriptor.Parameter);
        if (failedAction == ConditionFailedAction.Exclude)
        {
            return null;
        }

        return type switch
        {
            "Separator" => new MenuFlyoutSeparator(),
            "Menu" => CreateSubMenu(descriptor, failedAction == ConditionFailedAction.Disable),
            "Item" or "Command" => CreateCommandItem(descriptor, failedAction == ConditionFailedAction.Disable),
            _ => null
        };
    }

    private MenuFlyoutSubItem CreateSubMenu(MenuItemDescriptor descriptor, bool disabled)
    {
        var item = new MenuFlyoutSubItem
        {
            Text = descriptor.Codon.Properties["label"],
            IsEnabled = !disabled
        };
        AddItems(item.Items, descriptor.SubItems);
        return item;
    }

    private MenuFlyoutItem CreateCommandItem(MenuItemDescriptor descriptor, bool disabledByCondition)
    {
        var command = CommandWrapper.CreateLazyCommand(descriptor.Codon, descriptor.Conditions);
        var item = new MenuFlyoutItem
        {
            Text = descriptor.Codon.Properties["label"],
            IsEnabled = !disabledByCondition && command.CanExecute(descriptor.Parameter)
        };
        item.Click += (_, _) =>
        {
            if (command.CanExecute(descriptor.Parameter))
            {
                command.Execute(descriptor.Parameter);
            }
        };
        return item;
    }
}
