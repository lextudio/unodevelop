using System;
using Microsoft.UI.Xaml;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Gui;
using UnoDevelop.Workbench;

namespace ICSharpCode.SharpDevelop;

internal sealed class PadDoozer : IDoozer
{
    public bool HandleConditions => false;

    public object BuildItem(BuildItemArgs args)
    {
        return new PadDescriptor(args.Codon, instance =>
        {
            if (instance is not FrameworkElement control)
                throw new InvalidOperationException($"{args.Codon.Properties["class"]} is not a Uno FrameworkElement.");

            return new UnoPadContent(control);
        });
    }
}
