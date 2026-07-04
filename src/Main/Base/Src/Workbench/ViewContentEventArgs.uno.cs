using System;

namespace ICSharpCode.SharpDevelop.Workbench;

public sealed class ViewContentEventArgs : EventArgs
{
    public ViewContentEventArgs(IViewContent content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public IViewContent Content { get; }
}
