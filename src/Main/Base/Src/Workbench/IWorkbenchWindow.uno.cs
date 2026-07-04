using System;
using System.Collections.Generic;

namespace ICSharpCode.SharpDevelop.Workbench;

public interface IWorkbenchWindow
{
    string Title { get; }

    bool IsDisposed { get; }

    IViewContent ActiveViewContent { get; set; }

    object? Icon { get; set; }

    event EventHandler ActiveViewContentChanged;

    IList<IViewContent> ViewContents { get; }

    void SwitchView(int viewNumber);

    bool CloseWindow(bool force);

    void SelectWindow();

    event EventHandler TitleChanged;
}
