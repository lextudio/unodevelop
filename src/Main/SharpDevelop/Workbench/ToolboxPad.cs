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

/// <summary>
/// Shared Toolbox pad whose content is supplied by the active document.
/// </summary>
public sealed class ToolboxPad : UserControl
{
    readonly ContentControl _content = new();
    readonly TextBlock _empty = new()
    {
        Margin = new Thickness(12),
        Text = "No tools available for the current document.",
        TextWrapping = TextWrapping.Wrap
    };
    IWorkbenchWindow? _observedWindow;
    IToolboxProvider? _provider;

    public ToolboxPad()
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
        var activeWindow = SD.Workbench.ActiveWorkbenchWindow;
        if (ReferenceEquals(_observedWindow, activeWindow))
            return;
        if (_observedWindow is not null)
            _observedWindow.ActiveViewContentChanged -= OnWindowViewChanged;
        _observedWindow = activeWindow;
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
        _content.Content = _provider?.ToolboxContent ?? _empty;
    }

    static IToolboxProvider? GetProvider(IViewContent? view)
        => view?.GetService(typeof(IToolboxProvider)) as IToolboxProvider;

    public IReadOnlyList<object> GetSnapshot()
        => InvokeEnumerable("GetSnapshot");

    public IReadOnlyList<object> GetGroupSnapshot()
        => InvokeEnumerable("GetGroupSnapshot");

    IReadOnlyList<object> InvokeEnumerable(string methodName)
    {
        var result = _provider?.ToolboxContent.GetType().GetMethod(methodName)
            ?.Invoke(_provider.ToolboxContent, null) as IEnumerable;
        return result?.Cast<object>().ToArray() ?? Array.Empty<object>();
    }

    public bool SetGroupExpanded(string groupName, bool expanded)
        => _provider?.ToolboxContent.GetType().GetMethod(nameof(SetGroupExpanded))
            ?.Invoke(_provider.ToolboxContent, new object[] { groupName, expanded }) as bool? ?? false;
}
