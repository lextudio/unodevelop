using System.Collections.Generic;
using ICSharpCode.Core;

namespace UnoDevelop.OptionPanels;

public class OptionPanelDoozer : IDoozer
{
    public bool HandleConditions => false;

    public object BuildItem(BuildItemArgs args)
    {
        var label = args.Codon["label"];
        var id = args.Codon.Id;

        var subItems = args.BuildSubItems<IOptionPanelDescriptor>();
        if (subItems.Count == 0)
        {
            if (args.Codon.Properties.Contains("class"))
            {
                return new DefaultOptionPanelDescriptor(
                    id, StringParser.Parse(label),
                    args.AddIn, args.Parameter,
                    args.Codon["class"]);
            }
            return new DefaultOptionPanelDescriptor(id, StringParser.Parse(label));
        }

        return new DefaultOptionPanelDescriptor(id, StringParser.Parse(label), subItems);
    }
}
