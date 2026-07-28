using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;

namespace UnoDevelop.Services;

internal sealed class UnoStatusBarService : IStatusBarService
{
    private Stack<ProgressCollector> waitingProgresses = new();
    private ProgressCollector? currentProgress;

    public void SetCaretPosition(int x, int y, int charOffset) { }
    public void SetSelectionSingle(int length) { }
    public void SetSelectionMulti(int rows, int cols) { }

    public void SetMessage(string message, bool highlighted = false, IImage? icon = null)
    {
        _ = SD.MainThread.InvokeAsync(() =>
        {
            MainPage.Current?.SetStatusBarMessage(message, highlighted);
        });
    }

    public IProgressMonitor CreateProgressMonitor(CancellationToken cancellationToken = default)
    {
        var progress = new ProgressCollector(SD.MainThread.SynchronizingObject, cancellationToken);
        AddProgress(progress);
        return progress.ProgressMonitor;
    }

    public IProgressMonitor CreateCancellableProgressMonitor(CancellationTokenSource cancellationTokenSource)
    {
        var progress = new ProgressCollector(SD.MainThread.SynchronizingObject, cancellationTokenSource);
        AddProgress(progress);
        return progress.ProgressMonitor;
    }

    public void AddProgress(ProgressCollector progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        SD.MainThread.VerifyAccess();
        if (currentProgress is not null)
        {
            currentProgress.ProgressMonitorDisposed -= progress_ProgressMonitorDisposed;
            currentProgress.PropertyChanged -= progress_PropertyChanged;
        }
        waitingProgresses.Push(currentProgress);
        SetActiveProgress(progress);
    }

    private void SetActiveProgress(ProgressCollector? progress)
    {
        SD.MainThread.VerifyAccess();
        currentProgress = progress;
        if (progress is null)
        {
            MainPage.Current?.UpdateStatusBarProgress(null, -1, OperationStatus.Normal, null);
            return;
        }
        progress.ProgressMonitorDisposed += progress_ProgressMonitorDisposed;
        if (progress.ProgressMonitorIsDisposed)
        {
            progress_ProgressMonitorDisposed(progress, null);
            return;
        }
        progress.PropertyChanged += progress_PropertyChanged;
    }

    private void progress_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Debug.Assert(sender == currentProgress);
        var p = currentProgress;
        if (p is not null)
        {
            // Only hand MainPage a cancel action while the operation is still cancellable, so the
            // status bar's cancel button disappears the moment cancellation has been requested
            // rather than inviting the user to click it again with no effect.
            MainPage.Current?.UpdateStatusBarProgress(p.TaskName, p.Progress, p.Status,
                p.IsCancellable ? p.Cancel : null);
        }
    }

    private void progress_ProgressMonitorDisposed(object? sender, EventArgs e)
    {
        Debug.Assert(sender == currentProgress);
        SetActiveProgress(waitingProgresses.Pop());
    }
}
