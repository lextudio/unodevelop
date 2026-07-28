using System.Collections.Generic;
using System.Linq;
using ICSharpCode.Core;

namespace UnoDevelop.OptionPanels;

public class DefaultOptionPanelDescriptor : IOptionPanelDescriptor
{
    private readonly string _id;
    private List<IOptionPanelDescriptor>? _childDescriptors;
    private IOptionPanel? _optionPanel;
    private AddIn? _addin;
    private object? _owner;
    private string? _optionPanelPath;

    public string ID => _id;

    public string Label { get; set; }

    public IEnumerable<IOptionPanelDescriptor> ChildOptionPanelDescriptors =>
        _childDescriptors ?? Enumerable.Empty<IOptionPanelDescriptor>();

    public IOptionPanel? OptionPanel
    {
        get
        {
            if (_optionPanelPath is not null)
            {
                if (_optionPanel is null && _addin is not null)
                {
                    _optionPanel = (IOptionPanel?)_addin.CreateObject(_optionPanelPath);
                    if (_optionPanel is not null)
                        _optionPanel.Owner = _owner;
                }
                _optionPanelPath = null;
                _addin = null;
            }
            return _optionPanel;
        }
    }

    public bool HasOptionPanel => _optionPanelPath is not null;

    public DefaultOptionPanelDescriptor(string id, string label)
    {
        _id = id;
        Label = label;
    }

    public DefaultOptionPanelDescriptor(string id, string label, List<IOptionPanelDescriptor> childDescriptors)
        : this(id, label)
    {
        _childDescriptors = childDescriptors;
    }

    public DefaultOptionPanelDescriptor(string id, string label, AddIn addin, object? owner, string optionPanelPath)
        : this(id, label)
    {
        _addin = addin;
        _owner = owner;
        _optionPanelPath = optionPanelPath;
    }
}
