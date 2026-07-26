using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
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

    public object? GetService(Type serviceType)
        => serviceType.IsInstanceOfType(_control) ? _control : null;

    public async Task<IReadOnlyList<object>> GetSnapshotAsync()
    {
        var method = _control.GetType().GetMethod("GetSnapshotAsync", BindingFlags.Instance | BindingFlags.Public);
        if (method is null)
            return Array.Empty<object>();

        var result = method.Invoke(_control, Array.Empty<object>());
        if (result is Task<IReadOnlyList<object>> typedTask)
            return await typedTask.ConfigureAwait(false);

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            return resultProperty?.GetValue(task) as IReadOnlyList<object> ?? Array.Empty<object>();
        }

        return result as IReadOnlyList<object> ?? Array.Empty<object>();
    }

    public void Dispose()
    {
        if (_control is IDisposable disposable)
            disposable.Dispose();
    }
}
