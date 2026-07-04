using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using UnoDevelop.AddIns.Misc.AddInScout;

namespace UnoDevelop.AddIns.Misc.AddInScout;

public sealed class ShowAddInScoutCommand : AbstractMenuCommand
{
    public override void Run() => SD.Workbench.ShowView(new AddInScoutViewContent());
}
