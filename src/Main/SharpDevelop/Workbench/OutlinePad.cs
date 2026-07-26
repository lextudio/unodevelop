using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Workbench;

public sealed class OutlinePad : UserControl
{
    readonly ContentControl _content = new();
    readonly TextBlock _empty = new()
    {
        Margin = new Thickness(12),
        Text = "No outline available for the current document.",
        TextWrapping = TextWrapping.Wrap
    };
    IWorkbenchWindow? _observedWindow;
    IOutlineContentHost? _provider;

    public OutlinePad()
    {
        Content = _content;
        SD.Workbench.ActiveViewContentChanged += OnActiveViewChanged;
        SD.Workbench.ActiveWorkbenchWindowChanged += OnActiveViewChanged;
        UpdateProvider();
    }

    public bool HasProvider => _provider is not null;

    void OnActiveViewChanged(object? sender, EventArgs args)
    {
        ObserveActiveWindow();
        UpdateProvider();
    }

    void ObserveActiveWindow()
    {
        var window = SD.Workbench.ActiveWorkbenchWindow;
        if (ReferenceEquals(window, _observedWindow))
            return;
        if (_observedWindow is not null)
            _observedWindow.ActiveViewContentChanged -= OnWindowViewChanged;
        _observedWindow = window;
        if (_observedWindow is not null)
            _observedWindow.ActiveViewContentChanged += OnWindowViewChanged;
    }

    void OnWindowViewChanged(object? sender, EventArgs args) => UpdateProvider();

    void UpdateProvider()
    {
        ObserveActiveWindow();
        var window = SD.Workbench.ActiveWorkbenchWindow;
        _provider = GetProvider(window?.ActiveViewContent)
            ?? window?.ViewContents.Select(GetProvider).FirstOrDefault(candidate => candidate is not null);
        _content.Content = _provider?.OutlineContent ?? _empty;
    }

    static IOutlineContentHost? GetProvider(IViewContent? view)
        => view?.GetService(typeof(IOutlineContentHost)) as IOutlineContentHost;

    public IReadOnlyList<object> GetSnapshot()
    {
        var result = _provider?.OutlineContent.GetType().GetMethod("GetSnapshot")
            ?.Invoke(_provider.OutlineContent, null) as IEnumerable;
        return result?.Cast<object>().ToArray() ?? Array.Empty<object>();
    }
}
