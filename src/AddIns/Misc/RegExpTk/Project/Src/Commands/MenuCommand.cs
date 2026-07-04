using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;

namespace UnoDevelop.AddIns.Misc.RegExpTk;

public sealed class ShowRegularExpressionToolkitCommand : AbstractMenuCommand
{
    public override void Run() => SD.Workbench.ShowView(new RegularExpressionToolkitViewContent());
}
