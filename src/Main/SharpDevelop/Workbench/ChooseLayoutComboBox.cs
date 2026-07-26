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
        {
            // Store current layout state — no-op for now, Uno workbench does not
            // have AvalonDock's layout serialization.
        }

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
        }

        oldItem = SelectedIndex;
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

        var listBox = new ListBox { MinHeight = 200 };
        foreach (var name in CustomLayoutNames)
            listBox.Items.Add(name);
        panel.Children.Add(listBox);

        var addButton = new Button { Content = "Add Layout", Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(addButton);

        editor.Content = panel;

        _ = editor.ShowAsync();
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
    }

    void OnLayoutChanged(object? sender, EventArgs e)
    {
        if (editingLayout) return;
        LoggingService.Debug("ChooseLayoutComboBox.OnLayoutChanged");
        for (int i = 0; i < Items.Count; ++i)
        {
            if (Items[i] is LayoutConfiguration config && config.Name == LayoutConfiguration.CurrentLayoutName)
            {
                SelectedIndex = i;
                break;
            }
        }
    }
}
