using System;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Conditions;

[Flags]
public enum WindowState
{
    None = 0,
    Untitled = 1,
    Dirty = 2,
    ViewOnly = 4,
}

internal sealed class ProjectActiveConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object? caller, Condition condition)
    {
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        return projectService.CurrentProject is not null;
    }
}

internal sealed class WindowActiveConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object? caller, Condition condition)
    {
        string activeWindow = condition.Properties["activewindow"];
        if (activeWindow == "*")
            return SD.Workbench.ActiveWorkbenchWindow != null;

        Type? activeWindowType = condition.AddIn.FindType(activeWindow);
        if (activeWindowType == null)
            return false;

        if (SD.GetActiveViewContentService(activeWindowType) != null)
            return true;

        if (SD.Workbench.ActiveWorkbenchWindow?.ActiveViewContent == null)
            return false;

        Type currentType = SD.Workbench.ActiveWorkbenchWindow.ActiveViewContent.GetType();
        if (currentType.FullName == activeWindow)
            return true;
        foreach (Type interf in currentType.GetInterfaces())
        {
            if (interf.FullName == activeWindow)
                return true;
        }
        while ((currentType = currentType.BaseType) != null)
        {
            if (currentType.FullName == activeWindow)
                return true;
        }
        return false;
    }
}

internal sealed class ActiveWindowStateConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object? caller, Condition condition)
    {
        var activeWorkbenchWindow = SD.Workbench.ActiveWorkbenchWindow;
        if (activeWorkbenchWindow == null)
            return false;

        var windowState = condition.Properties.Get("windowstate", WindowState.None);
        var nowindowState = condition.Properties.Get("nowindowstate", WindowState.None);

        bool isWindowStateOk = false;
        if (windowState != WindowState.None)
        {
            if ((windowState & WindowState.Dirty) > 0)
                isWindowStateOk |= activeWorkbenchWindow.ViewContents.Any(vc => vc.IsDirty);
            if ((windowState & WindowState.Untitled) > 0)
                isWindowStateOk |= IsUntitled(activeWorkbenchWindow.ActiveViewContent);
            if ((windowState & WindowState.ViewOnly) > 0)
                isWindowStateOk |= IsViewOnly(activeWorkbenchWindow.ActiveViewContent);
        }
        else
        {
            isWindowStateOk = true;
        }

        if (nowindowState != WindowState.None)
        {
            if ((nowindowState & WindowState.Dirty) > 0)
                isWindowStateOk &= !activeWorkbenchWindow.ViewContents.Any(vc => vc.IsDirty);
            if ((nowindowState & WindowState.Untitled) > 0)
                isWindowStateOk &= !IsUntitled(activeWorkbenchWindow.ActiveViewContent);
            if ((nowindowState & WindowState.ViewOnly) > 0)
                isWindowStateOk &= !IsViewOnly(activeWorkbenchWindow.ActiveViewContent);
        }
        return isWindowStateOk;
    }

    static bool IsUntitled(IViewContent? viewContent)
    {
        if (viewContent == null) return false;
        var file = viewContent.PrimaryFile;
        return file != null && file.IsUntitled;
    }

    static bool IsViewOnly(IViewContent? viewContent)
        => viewContent != null && viewContent.IsViewOnly;
}

internal sealed class OpenWindowStateConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object? caller, Condition condition)
    {
        if (SD.Workbench == null) return false;

        var windowState = condition.Properties.Get("openwindowstate", WindowState.None);
        var nowindowState = condition.Properties.Get("noopenwindowstate", WindowState.None);

        foreach (var view in SD.Workbench.ViewContentCollection)
        {
            bool isWindowStateOk = false;
            if (windowState != WindowState.None)
            {
                if ((windowState & WindowState.Dirty) > 0)
                    isWindowStateOk |= view.IsDirty;
                if ((windowState & WindowState.Untitled) > 0)
                {
                    var file = view.PrimaryFile;
                    isWindowStateOk |= file != null && file.IsUntitled;
                }
                if ((windowState & WindowState.ViewOnly) > 0)
                    isWindowStateOk |= view.IsViewOnly;
            }
            else
            {
                isWindowStateOk = true;
            }
            if (nowindowState != WindowState.None)
            {
                if ((nowindowState & WindowState.Dirty) > 0)
                    isWindowStateOk &= !view.IsDirty;
                if ((nowindowState & WindowState.Untitled) > 0)
                {
                    var file = view.PrimaryFile;
                    isWindowStateOk &= file == null || !file.IsUntitled;
                }
                if ((nowindowState & WindowState.ViewOnly) > 0)
                    isWindowStateOk &= !view.IsViewOnly;
            }
            if (isWindowStateOk) return true;
        }
        return false;
    }
}

internal sealed class CanNavigateBackConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object? caller, Condition condition)
        => false;
}

internal sealed class CanNavigateForwardConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object? caller, Condition condition)
        => false;
}
