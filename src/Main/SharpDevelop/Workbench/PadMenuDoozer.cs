using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop;

internal sealed class PadMenuDoozer : IDoozer
{
    public bool HandleConditions => false;

    public object BuildItem(BuildItemArgs args)
    {
        var category = args.Codon.Properties["category"];
        return new PadMenuDescriptor(category);
    }
}

internal sealed record PadMenuDescriptor(string Category);
