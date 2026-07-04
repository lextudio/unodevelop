using System;
using Microsoft.UI.Xaml;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Workbench;

internal sealed class UnoPadContent : IPadContent
{
    private readonly FrameworkElement _control;

    public UnoPadContent(FrameworkElement control)
    {
        _control = control;
    }

    public object Control => _control;

    public object InitiallyFocusedControl => _control;

    public object? GetService(Type serviceType) => null;

    public void Dispose()
    {
        if (_control is IDisposable disposable)
            disposable.Dispose();
    }
}
