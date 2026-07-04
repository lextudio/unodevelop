using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.OptionPanels;

public sealed partial class OptionsDialog : ContentDialog
{
    private readonly List<IOptionPanel> _panels = new();

    public OptionsDialog(IEnumerable<IOptionPanelDescriptor> optionPanels, string dialogName)
    {
        if (optionPanels is null)
            throw new ArgumentNullException(nameof(optionPanels));

        InitializeComponent();

        var flatItems = FlattenPanelTree(optionPanels).ToList();
        _panelList.ItemsSource = flatItems;
        _panelList.DisplayMemberPath = nameof(OptionPanelItem.Title);
        if (flatItems.Count > 0)
            _panelList.SelectedIndex = 0;
    }

    private static IEnumerable<OptionPanelItem> FlattenPanelTree(IEnumerable<IOptionPanelDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            var children = descriptor.ChildOptionPanelDescriptors.ToList();

            if (descriptor.HasOptionPanel)
                yield return new OptionPanelItem(descriptor);

            foreach (var child in FlattenPanelTree(children))
                yield return child;
        }
    }

    private void OnPanelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_panelList.SelectedItem is OptionPanelItem item)
        {
            _contentArea.Content = item.Panel;
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        foreach (var panel in _panels)
        {
            if (!panel.SaveOptions())
            {
                args.Cancel = true;
                return;
            }
        }
        SD.PropertyService.Save();
    }

    private sealed class OptionPanelItem
    {
        private readonly IOptionPanelDescriptor _descriptor;
        private IOptionPanel? _panel;

        public string Title => _descriptor.Label;

        public object? Panel
        {
            get
            {
                if (_panel is null)
                {
                    _panel = _descriptor.OptionPanel;
                    _panel?.LoadOptions();
                }
                return _panel?.Control;
            }
        }

        public OptionPanelItem(IOptionPanelDescriptor descriptor)
        {
            _descriptor = descriptor;
        }
    }
}
