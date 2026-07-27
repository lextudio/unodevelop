using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace UnoDevelop.Workbench;

internal sealed class ChooseLayoutComboBox : ComboBox
{
    int editIndex = -1;
    int resetIndex = -1;
    int oldItem;
    bool editingLayout;

    public ChooseLayoutComboBox()
    {
        MinWidth = 120;
        Width = 150;
        LayoutConfiguration.LayoutChanged += OnLayoutChanged;
        SD.ResourceService.LanguageChanged += OnLanguageChanged;
        SelectionChanged += OnSelectionChanged;
        RecreateItems();
    }

    void OnLanguageChanged(object? sender, EventArgs e) => RecreateItems();

    void RecreateItems()
    {
        editingLayout = true;
        try
        {
            Items.Clear();
            int index = 0;
            foreach (var config in LayoutConfiguration.Layouts)
            {
                if (LayoutConfiguration.CurrentLayoutName == config.Name)
                    index = Items.Count;
                Items.Add(config);
            }
            editIndex = Items.Count;
            Items.Add("Edit Layouts...");
            resetIndex = Items.Count;
            Items.Add("Reset to Default");
            SelectedIndex = index;
        }
        finally
        {
            editingLayout = false;
        }
    }

    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (editingLayout) return;
        LoggingService.Debug("ChooseLayoutComboBox selection changed");

        var configPath = LayoutConfiguration.ConfigLayoutPath;
        if (!Directory.Exists(configPath))
            Directory.CreateDirectory(configPath);

        if (oldItem != editIndex && oldItem != resetIndex)
            StoreCurrentLayout();

        if (SelectedIndex == editIndex)
        {
            editingLayout = true;
            ShowLayoutEditor();
            RecreateItems();
            editingLayout = false;
        }
        else if (SelectedIndex == resetIndex)
        {
            ResetToDefaults();
        }
        else if (SelectedIndex >= 0 && SelectedIndex < LayoutConfiguration.Layouts.Count)
        {
            var config = LayoutConfiguration.Layouts[SelectedIndex];
            LayoutConfiguration.CurrentLayoutName = config.Name;
            LoadCurrentLayout();
        }

        oldItem = SelectedIndex;
    }

    /// <summary>Serializes the outgoing layout to its own file, mirroring OpenDevelop's WorkbenchLayout.StoreConfiguration.</summary>
    internal static void StoreCurrentLayout()
    {
        var current = LayoutConfiguration.CurrentLayout;
        if (current is null || current.ReadOnly)
            return;
        if (LayoutConfiguration.CurrentLayoutFileName is string fileName)
            UnoDevelop.MainPage.Current?.SaveCurrentLayout(fileName);
    }

    /// <summary>
    /// Restores the now-current layout: its own saved file if one exists yet, else its bundled
    /// template - mirroring OpenDevelop's WorkbenchLayout.TryLoadConfiguration.
    /// </summary>
    internal static void LoadCurrentLayout()
    {
        if (LayoutConfiguration.CurrentLayoutFileName is string fileName && File.Exists(fileName))
        {
            UnoDevelop.MainPage.Current?.RestoreLayout(fileName);
            return;
        }
        if (LayoutConfiguration.CurrentLayoutTemplateFileName is string templateFileName)
            UnoDevelop.MainPage.Current?.RestoreLayout(templateFileName);
    }

    static IEnumerable<string> CustomLayoutNames
        => LayoutConfiguration.Layouts
            .Where(l => l.Custom)
            .Select(l => l.Name);

    void ShowLayoutEditor()
    {
        var editor = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Edit Layouts",
            CloseButtonText = "Cancel",
            PrimaryButtonText = "Save",
            XamlRoot = XamlRoot,
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Layout names:" });

        var listBox = new ListBox { MinHeight = 200, SelectionMode = SelectionMode.Single };
        foreach (var name in CustomLayoutNames)
            listBox.Items.Add(name);
        panel.Children.Add(listBox);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var addButton = new Button { Content = "Add Layout" };
        var removeButton = new Button { Content = "Remove Selected" };
        buttonRow.Children.Add(addButton);
        buttonRow.Children.Add(removeButton);
        panel.Children.Add(buttonRow);

        addButton.Click += (_, _) =>
        {
            var name = SD.MessageService.ShowInputBox("Add Layout", "Enter a name for the new layout:", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (listBox.Items.Cast<string>().Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                return;
            listBox.Items.Add(name);
        };

        removeButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is string selected)
                listBox.Items.Remove(selected);
        };

        editor.Content = panel;

        editor.PrimaryButtonClick += (_, _) => ReconcileEditedLayouts(listBox.Items.Cast<string>().ToList());

        _ = editor.ShowAsync();
    }

    /// <summary>
    /// Same reconciliation OpenDevelop's ChooseLayoutComboBox does after its StringListEditorDialog
    /// returns: add newly-listed names as custom layouts (copying the Default layout's template as
    /// a starting point via LayoutConfiguration.CreateCustom), and drop any existing custom layout
    /// no longer in the list, then persist. Extracted from the dialog's Save handler so DevFlow/
    /// integration tests can exercise it directly without driving the ContentDialog's UI.
    /// </summary>
    static void ReconcileEditedLayouts(IReadOnlyList<string> newNames)
    {
        var oldNames = new List<string>(CustomLayoutNames);

        foreach (var newLayoutName in newNames)
        {
            if (!oldNames.Contains(newLayoutName))
                LayoutConfiguration.CreateCustom(newLayoutName);
        }

        LayoutConfiguration.Layouts.RemoveAll(lc => lc.Custom && !newNames.Contains(lc.Name));

        LayoutConfiguration.SaveCustomLayoutConfiguration();
    }

    /// <summary>Test hook: exercises the exact same Store-old/switch/Load-new sequence a real dropdown selection does.</summary>
    internal static void SwitchLayoutForTesting(string layoutName)
    {
        StoreCurrentLayout();
        LayoutConfiguration.CurrentLayoutName = layoutName;
        LoadCurrentLayout();
    }

    /// <summary>Test hook: exercises the exact same Add+Save reconciliation a real "Edit Layouts" dialog Save click does.</summary>
    internal static void AddAndSaveLayoutForTesting(string name)
    {
        var newNames = CustomLayoutNames.ToList();
        if (!newNames.Contains(name))
            newNames.Add(name);
        ReconcileEditedLayouts(newNames);
    }

    /// <summary>Test hook: exercises the exact same Remove+Save reconciliation a real "Edit Layouts" dialog Save click does.</summary>
    internal static void RemoveAndSaveLayoutForTesting(string name)
    {
        var newNames = CustomLayoutNames.Where(existing => existing != name).ToList();
        ReconcileEditedLayouts(newNames);
    }

    void ResetToDefaults()
    {
        var configPath = LayoutConfiguration.ConfigLayoutPath;
        var dataPath = LayoutConfiguration.DataLayoutPath;

        foreach (var config in LayoutConfiguration.Layouts)
        {
            var dataFile = Path.Combine(dataPath, config.FileName);
            var configFile = Path.Combine(configPath, config.FileName);
            if (File.Exists(dataFile) && File.Exists(configFile))
            {
                try { File.Delete(configFile); } catch { }
            }
        }
        LayoutConfiguration.ReloadDefaultLayout();
        LoadCurrentLayout();
    }

    void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (editingLayout) return;
        LoggingService.Debug("ChooseLayoutComboBox.OnLayoutChanged");

        // Guard the SelectedIndex assignment below: LayoutConfiguration.CurrentLayoutName's setter
        // calls this synchronously, so whenever CurrentLayoutName is changed from outside this
        // ComboBox's own OnSelectionChanged (e.g. SwitchLayoutForTesting, ReloadDefaultLayout), the
        // SelectedIndex change below would otherwise re-fire SelectionChanged reentrantly - running
        // StoreCurrentLayout/LoadCurrentLayout a second time mid-switch and clobbering the just-saved
        // config file with the pre-switch layout's content.
        editingLayout = true;
        try
        {
            for (int i = 0; i < Items.Count; ++i)
            {
                if (Items[i] is LayoutConfiguration config && config.Name == LayoutConfiguration.CurrentLayoutName)
                {
                    SelectedIndex = i;
                    oldItem = i;
                    break;
                }
            }
        }
        finally
        {
            editingLayout = false;
        }
    }
}
