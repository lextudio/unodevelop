using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Workbench;

public interface IViewContent : IDisposable, ICanBeDirty, IServiceProvider
{
    object? Control { get; }

    object? InitiallyFocusedControl { get; }

    IWorkbenchWindow? WorkbenchWindow { get; set; }

    event EventHandler TabPageTextChanged;

    string TabPageText { get; }

    string TitleName { get; }

    event EventHandler TitleNameChanged;

    string InfoTip { get; }

    event EventHandler InfoTipChanged;

    void Save(OpenedFile file, Stream stream);

    void Load(OpenedFile file, Stream stream);

    IList<OpenedFile> Files { get; }

    OpenedFile? PrimaryFile { get; }

    FileName? PrimaryFileName { get; }

    INavigationPoint BuildNavPoint();

    bool IsDisposed { get; }

    event EventHandler Disposed;

    bool IsReadOnly { get; }

    bool IsViewOnly { get; }

    bool CloseWithSolution { get; }

    ICollection<IViewContent> SecondaryViewContents { get; }

    bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView);

    bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView);

    void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView);

    void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView);
}
