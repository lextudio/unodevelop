using System.Collections.Generic;

namespace UnoDevelop.OptionPanels;

public interface IOptionPanelDescriptor
{
    string ID { get; }
    string Label { get; }
    IEnumerable<IOptionPanelDescriptor> ChildOptionPanelDescriptors { get; }
    IOptionPanel? OptionPanel { get; }
    bool HasOptionPanel { get; }
}
