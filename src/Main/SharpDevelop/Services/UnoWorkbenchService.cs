using System;
using System.Collections.Generic;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Services;

internal sealed class UnoWorkbenchService : IWorkbench
{
    private readonly List<IViewContent> _views = new();
    private readonly List<IWorkbenchWindow> _windows = new();
    private readonly List<PadDescriptor> _pads = new();
    private Action<IViewContent, bool>? _showViewInUi;
    private Action? _closeAllViewsInUi;
    private IViewContent? _activeView;
    private IWorkbenchWindow? _activeWindow;

    public object? MainWindow => null;

    public bool FullScreen { get; set; }

    public ICollection<IViewContent> ViewContentCollection => _views;

    public ICollection<IViewContent> PrimaryViewContents => _views;

    public IList<IWorkbenchWindow> WorkbenchWindowCollection => _windows;

    public IList<PadDescriptor> PadContentCollection => _pads;

    public IWorkbenchWindow? ActiveWorkbenchWindow => _activeWindow;

    public event EventHandler? ActiveWorkbenchWindowChanged;

    public IViewContent? ActiveViewContent => _activeView;

    public event EventHandler? ActiveViewContentChanged;

    public IServiceProvider? ActiveContent => _activeView;

    public event EventHandler? ActiveContentChanged;

    public bool IsActiveWindow => true;

    public string CurrentLayoutConfiguration { get; set; } = "Default";

    public event EventHandler<ViewContentEventArgs>? ViewOpened;

    public event EventHandler<ViewContentEventArgs>? ViewClosed;

    /// Fired on the calling thread when a new pad is registered via ActivatePad.
    public event EventHandler<PadDescriptor>? PadAdded;

    public void Initialize()
    {
    }

    public void BindUiHost(Action<IViewContent, bool> showViewInUi, Action closeAllViewsInUi)
    {
        _showViewInUi = showViewInUi;
        _closeAllViewsInUi = closeAllViewsInUi;
    }

    public void ShowView(IViewContent content)
    {
        ShowView(content, true);
    }

    public void ShowView(IViewContent content, bool switchToOpenedView)
    {
        var shouldActivate = switchToOpenedView || _activeView is null;

        if (!_views.Contains(content))
        {
            _views.Add(content);
            ViewOpened?.Invoke(this, new ViewContentEventArgs(content));
        }

        _showViewInUi?.Invoke(content, shouldActivate);

        if (content.WorkbenchWindow is not null && !_windows.Contains(content.WorkbenchWindow))
        {
            _windows.Add(content.WorkbenchWindow);
        }

        if (shouldActivate)
        {
            SetActiveView(content);
        }
    }

    public bool ActivateView(IViewContent content)
    {
        if (!_views.Contains(content))
        {
            return false;
        }

        _showViewInUi?.Invoke(content, true);
        SetActiveView(content);
        return true;
    }

    public void ActivatePad(PadDescriptor content)
    {
        if (!_pads.Contains(content))
        {
            _pads.Add(content);
            PadAdded?.Invoke(this, content);
        }
    }

    public PadDescriptor? GetPad(Type type)
    {
        foreach (var pad in _pads)
        {
            if (string.Equals(pad.ClassName, type.FullName, StringComparison.Ordinal))
            {
                return pad;
            }
        }

        return null;
    }

    public void CloseAllViews()
    {
        if (_views.Count == 0)
        {
            return;
        }

        var closing = _views.ToArray();
        _views.Clear();
        foreach (var view in closing)
        {
            ViewClosed?.Invoke(this, new ViewContentEventArgs(view));
        }

        _windows.Clear();
        SetActiveView(null);
        _closeAllViewsInUi?.Invoke();
    }

    public bool CloseAllSolutionViews(bool force)
    {
        CloseAllViews();
        return true;
    }

    private void SetActiveView(IViewContent? view)
    {
        var activeWindow = view?.WorkbenchWindow;
        var activeViewChanged = !ReferenceEquals(_activeView, view);
        var activeWindowChanged = !ReferenceEquals(_activeWindow, activeWindow);

        if (!activeViewChanged && !activeWindowChanged)
        {
            return;
        }

        _activeView = view;
        _activeWindow = activeWindow;

        if (activeViewChanged)
        {
            ActiveViewContentChanged?.Invoke(this, EventArgs.Empty);
            ActiveContentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (activeWindowChanged)
        {
            ActiveWorkbenchWindowChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
