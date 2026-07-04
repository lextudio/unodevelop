using System;
using System.Collections.Generic;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Gui;

namespace ICSharpCode.SharpDevelop.Workbench;

[SDService("SD.Workbench")]
public interface IWorkbench
{
    object? MainWindow { get; }

    bool FullScreen { get; set; }

    ICollection<IViewContent> ViewContentCollection { get; }

    ICollection<IViewContent> PrimaryViewContents { get; }

    IList<IWorkbenchWindow> WorkbenchWindowCollection { get; }

    IList<PadDescriptor> PadContentCollection { get; }

    IWorkbenchWindow? ActiveWorkbenchWindow { get; }

    event EventHandler ActiveWorkbenchWindowChanged;

    IViewContent? ActiveViewContent { get; }

    event EventHandler ActiveViewContentChanged;

    IServiceProvider? ActiveContent { get; }

    event EventHandler ActiveContentChanged;

    bool IsActiveWindow { get; }

    void Initialize();

    void ShowView(IViewContent content);

    void ShowView(IViewContent content, bool switchToOpenedView);

    void ActivatePad(PadDescriptor content);

    PadDescriptor? GetPad(Type type);

    void CloseAllViews();

    bool CloseAllSolutionViews(bool force);

    string CurrentLayoutConfiguration { get; set; }

    event EventHandler<ViewContentEventArgs> ViewOpened;

    event EventHandler<ViewContentEventArgs> ViewClosed;
}
