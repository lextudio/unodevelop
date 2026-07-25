using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.StartPage;

public class ShowStartPageCommand : AbstractMenuCommand
{
    static ShowStartPageCommand()
    {
        SD.ProjectService.SolutionOpened += delegate {
            foreach (IViewContent v in SD.Workbench.ViewContentCollection.ToArray())
            {
                if (v is StartPageViewContent)
                    v.WorkbenchWindow?.CloseWindow(true);
            }
        };
    }

    public override void Run()
    {
        foreach (IViewContent view in SD.Workbench.ViewContentCollection)
        {
            if (view is StartPageViewContent)
            {
                view.WorkbenchWindow?.SelectWindow();
                return;
            }
        }
        SD.Workbench.ShowView(new StartPageViewContent());
    }
}
