using System;
using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ICSharpCode.Core;
using System.Windows.Input;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfSeparator = System.Windows.Controls.Separator;
using WpfToolBar = System.Windows.Controls.ToolBar;
using WpfButtonBase = Microsoft.UI.Xaml.Controls.Primitives.ButtonBase;

namespace UnoDevelop.Services;

internal interface IUnoAddInMenuBarBuilder
{
    void PopulateMenuBar(MenuBar menuBar, object owner, string addInTreePath);
}

internal interface IUnoAddInToolbarBuilder
{
    void PopulateToolbar(WpfToolBar toolbar, object owner, string addInTreePath);

    // Re-evaluates each item's <Condition> + command CanExecute and updates IsEnabled in place —
    // the equivalent of SharpDevelop's ToolBarService.UpdateStatus / IStatusUpdate. Call it when
    // relevant state changes (solution opened/closed, run/debug started/stopped).
    void UpdateStatus(WpfToolBar toolbar);
}

internal sealed class UnoAddInMenuBarBuilder : IUnoAddInMenuBarBuilder
{
    public void PopulateMenuBar(MenuBar menuBar, object owner, string addInTreePath)
    {
        menuBar.Items.Clear();
        var descriptors = AddInTree.BuildItems<MenuItemDescriptor>(addInTreePath, owner, false);
        foreach (var descriptorObject in descriptors)
        {
            if (descriptorObject is not MenuItemDescriptor descriptor)
            {
                continue;
            }

            var item = CreateTopLevelItem(descriptor);
            if (item is not null)
            {
                menuBar.Items.Add(item);
            }
        }
    }

    private static MenuBarItem? CreateTopLevelItem(MenuItemDescriptor descriptor)
    {
        var failedAction = Condition.GetFailedAction(descriptor.Conditions, descriptor.Parameter);
        if (failedAction == ConditionFailedAction.Exclude)
        {
            return null;
        }

        var codon = descriptor.Codon;
        var item = new MenuBarItem
        {
            Title = codon.Properties["label"],
            IsEnabled = failedAction != ConditionFailedAction.Disable
        };

        foreach (var child in descriptor.SubItems)
        {
            if (child is not MenuItemDescriptor childDescriptor)
            {
                continue;
            }

            var menuItem = CreateFlyoutItem(childDescriptor);
            if (menuItem is not null)
            {
                item.Items.Add(menuItem);
            }
        }

        return item;
    }

    private static MenuFlyoutItemBase? CreateFlyoutItem(MenuItemDescriptor descriptor)
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
            _ => CreateCommandItem(descriptor, failedAction == ConditionFailedAction.Disable)
        };
    }

    private static MenuFlyoutSubItem CreateSubMenu(MenuItemDescriptor descriptor, bool disabled)
    {
        var item = new MenuFlyoutSubItem
        {
            Text = descriptor.Codon.Properties["label"],
            IsEnabled = !disabled
        };

        foreach (var child in descriptor.SubItems)
        {
            if (child is not MenuItemDescriptor childDescriptor)
            {
                continue;
            }

            var menuItem = CreateFlyoutItem(childDescriptor);
            if (menuItem is not null)
            {
                item.Items.Add(menuItem);
            }
        }

        return item;
    }

    private static MenuFlyoutItem CreateCommandItem(MenuItemDescriptor descriptor, bool disabledByCondition)
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

internal sealed class UnoAddInToolbarBuilder : IUnoAddInToolbarBuilder
{
    // Per-button re-evaluation closures, keyed weakly by the created control so a rebuilt toolbar
    // does not leak the old buttons.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, Action> StatusUpdaters = new();

    public void PopulateToolbar(WpfToolBar toolbar, object owner, string addInTreePath)
    {
        toolbar.Items.Clear();
        var descriptors = AddInTree.BuildItems<ToolbarItemDescriptor>(addInTreePath, owner, false);
        foreach (var descriptorObject in descriptors)
        {
            if (descriptorObject is not ToolbarItemDescriptor descriptor)
                continue;

            var item = CreateToolbarItem(descriptor);
            if (item is not null)
                toolbar.Items.Add(item);
        }
    }

    public void UpdateStatus(WpfToolBar toolbar)
    {
        foreach (var item in toolbar.Items)
        {
            if (item is UIElement element && StatusUpdaters.TryGetValue(element, out var update))
                update();
        }
    }

    // Builds the closure that re-applies a button's condition/command state, registers it for
    // UpdateStatus, and runs it once to set the initial enabled state.
    private static void RegisterStatusUpdater(WpfButtonBase button, ToolbarItemDescriptor descriptor, ICommand command)
    {
        void Update()
        {
            var failedAction = Condition.GetFailedAction(descriptor.Conditions, descriptor.Parameter);
            button.IsEnabled = failedAction != ConditionFailedAction.Disable
                && command.CanExecute(descriptor.Parameter);
        }

        StatusUpdaters.AddOrUpdate(button, Update);
        Update();
    }

    private static UIElement? CreateToolbarItem(ToolbarItemDescriptor descriptor)
    {
        var codon = descriptor.Codon;
        var type = codon.Properties.Contains("type") ? codon.Properties["type"] : "Item";
        var failedAction = Condition.GetFailedAction(descriptor.Conditions, descriptor.Parameter);
        if (failedAction == ConditionFailedAction.Exclude)
            return null;

        if (type == "Custom")
        {
            var className = codon.Properties["class"];
            if (!string.IsNullOrEmpty(className))
            {
                var instance = codon.AddIn.CreateObject(className);
                if (instance is Control control)
                {
                    // For combos etc. that manage their own layout switching,
                    // IsEnabled is always true (conditions only control display).
                    control.IsEnabled = failedAction != ConditionFailedAction.Disable;
                    return control;
                }
                if (instance is UIElement element)
                {
                    return element;
                }
            }
            return null;
        }

        return type switch
        {
            "Separator" => new WpfSeparator(),
            "CheckBox" => CreateToggleButton(descriptor, failedAction == ConditionFailedAction.Disable),
            _ => CreateButton(descriptor, failedAction == ConditionFailedAction.Disable)
        };
    }

    private static WpfButton CreateButton(ToolbarItemDescriptor descriptor, bool disabledByCondition)
    {
        var command = CommandWrapper.CreateLazyCommand(descriptor.Codon, descriptor.Conditions);
        var label = ResolveToolbarLabel(descriptor.Codon);
        var enabled = !disabledByCondition && command.CanExecute(descriptor.Parameter);
        var button = new WpfButton
        {
            Content = CreateToolbarIcon(descriptor.Codon),
            Tag = descriptor.Codon.Id,
            IsEnabled = enabled,
            Padding = new Thickness(3),
            MinWidth = 0,
            MinHeight = 0
        };
        // Flat toolbar chrome that still hovers: transparent only in the REST/disabled states, while
        // the themed PointerOver/Pressed brushes are left intact. Hard-setting Background/Border
        // (the previous approach) also suppressed the hover visual states — unlike the ToggleButton,
        // which is why only toggle items highlighted on hover.
        ToolbarVisuals.ApplyFlatToolbarChrome(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, descriptor.Codon.Id);
        ToolbarVisuals.WireDisabledWash(button);
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) =>
        {
            if (command.CanExecute(descriptor.Parameter))
                command.Execute(descriptor.Parameter);
        };
        RegisterStatusUpdater(button, descriptor, command);
        return button;
    }

    private static WpfToggleButton CreateToggleButton(ToolbarItemDescriptor descriptor, bool disabledByCondition)
    {
        var command = CommandWrapper.CreateLazyCommand(descriptor.Codon, descriptor.Conditions);
        var label = ResolveToolbarLabel(descriptor.Codon);
        var enabled = !disabledByCondition && command.CanExecute(descriptor.Parameter);
        var button = new WpfToggleButton
        {
            Content = CreateToolbarIcon(descriptor.Codon),
            Tag = descriptor.Codon.Id,
            IsEnabled = enabled,
            Padding = new Thickness(3),
            MinWidth = 0,
            MinHeight = 0
        };
        ToolbarVisuals.ApplyFlatToolbarChrome(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, descriptor.Codon.Id);
        ToolbarVisuals.WireDisabledWash(button);
        ToolTipService.SetToolTip(button, label);

        // Reflect a checkable command's state on the toggle (e.g. Show All Files).
        var checkable = CommandWrapper.Unwrap(command) as ICheckableMenuCommand;
        if (checkable is not null)
        {
            button.IsChecked = checkable.IsChecked(descriptor.Parameter);
            checkable.IsCheckedChanged += (_, _) =>
                button.IsChecked = checkable.IsChecked(descriptor.Parameter);
        }

        button.Click += (_, _) =>
        {
            if (command.CanExecute(descriptor.Parameter))
                command.Execute(descriptor.Parameter);
            // Re-sync after execution so the visual state matches the command.
            if (checkable is not null)
                button.IsChecked = checkable.IsChecked(descriptor.Parameter);
        };
        RegisterStatusUpdater(button, descriptor, command);
        return button;
    }

    private static UIElement? CreateToolbarIcon(Codon codon)
    {
        if (!codon.Properties.Contains("icon"))
            return null;

        return ToolbarVisuals.CreateToolbarIcon(codon.Properties["icon"]);
    }

    private static string ResolveToolbarLabel(Codon codon)
    {
        if (codon.Properties.Contains("tooltip"))
            return codon.Properties["tooltip"];
        if (codon.Properties.Contains("label"))
            return codon.Properties["label"];
        return codon.Id;
    }
}
