using System;

namespace UnoDevelop.Debugger;

/// Entry point for the debugger addin.
public static class DebuggerAddin
{
    public static void Initialize(IDebuggerService debuggerService)
    {
        // All pads are now managed via XAML LayoutAnchorables in MainPage.
        // This method is kept as a hook for future addin-level initialization.
    }
}
