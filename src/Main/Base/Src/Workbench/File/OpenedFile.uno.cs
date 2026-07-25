using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Workbench;

public abstract class OpenedFile : ICanBeDirty
{
    public abstract event EventHandler FileClosed;

    public event EventHandler? IsDirtyChanged;

    public bool IsDirty { get; protected set; }

    public void MakeDirty()
    {
        IsDirty = true;
        OnIsDirtyChanged();
    }

    public bool IsUntitled { get; protected set; }

    public FileName? FileName { get; set; }

    public IList<IViewContent> RegisteredViewContents { get; } = new List<IViewContent>();

    protected void OnIsDirtyChanged()
    {
        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public virtual Stream OpenRead()
    {
        if (FileName == null)
            throw new InvalidOperationException("Cannot open an untitled file.");
        return File.OpenRead(FileName.ToString());
    }
}
