using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.Project;

namespace UnoDevelop.Services;

public enum UnoTaskType
{
    Error,
    Warning,
    Message,
    Comment
}

public sealed class UnoTask
{
    public UnoTask(string? fileName, string description, int column, int line, UnoTaskType taskType)
    {
        FileName = fileName;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Column = column;
        Line = line;
        TaskType = taskType;
    }

    public string? FileName { get; }
    public string? File => string.IsNullOrEmpty(FileName) ? null : System.IO.Path.GetFileName(FileName);
    public string? Path => string.IsNullOrEmpty(FileName) ? null : System.IO.Path.GetDirectoryName(FileName);
    public string Description { get; }
    public int Column { get; }
    public int Line { get; }
    public UnoTaskType TaskType { get; }
    public object? Tag { get; init; }

    public static UnoTask FromDiagnostic(string fileName, LanguageDiagnostic diagnostic)
    {
        return new UnoTask(
            fileName,
            string.IsNullOrEmpty(diagnostic.Id)
                ? diagnostic.Message
                : diagnostic.Message + " (" + diagnostic.Id + ")",
            diagnostic.Span.Start.Column,
            diagnostic.Span.Start.Line,
            ToTaskType(diagnostic.Severity))
        {
            Tag = diagnostic
        };
    }

    public static UnoTask FromBuildError(BuildError error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        var taskType = error.IsMessage
            ? UnoTaskType.Message
            : error.IsWarning ? UnoTaskType.Warning : UnoTaskType.Error;
        var description = string.IsNullOrEmpty(error.ErrorCode)
            ? error.ErrorText
            : error.ErrorText + " (" + error.ErrorCode + ")";

        return new UnoTask(
            string.IsNullOrEmpty(error.FileName) ? null : error.FileName,
            description,
            Math.Max(error.Column, 1),
            Math.Max(error.Line, 1),
            taskType)
        {
            Tag = error
        };
    }

    static UnoTaskType ToTaskType(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Error => UnoTaskType.Error,
            DiagnosticSeverity.Warning => UnoTaskType.Warning,
            _ => UnoTaskType.Message
        };
    }
}

public sealed class UnoTaskEventArgs : EventArgs
{
    public UnoTaskEventArgs(UnoTask task)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
    }

    public UnoTask Task { get; }
}

public sealed class UnoTaskService
{
    private readonly List<UnoTask> _tasks = new();
    private readonly Dictionary<UnoTaskType, int> _taskCount = new();

    public IEnumerable<UnoTask> Tasks => _tasks.Where(task => task.TaskType != UnoTaskType.Comment).ToArray();
    public IEnumerable<UnoTask> CommentTasks => _tasks.Where(task => task.TaskType == UnoTaskType.Comment).ToArray();
    public int TaskCount => _tasks.Count - GetCount(UnoTaskType.Comment);

    public bool InUpdate
    {
        get;
        private set;
    }

    public event EventHandler<UnoTaskEventArgs>? Added;
    public event EventHandler<UnoTaskEventArgs>? Removed;
    public event EventHandler? Cleared;
    public event EventHandler? InUpdateChanged;

    public int GetCount(UnoTaskType type)
    {
        return _taskCount.TryGetValue(type, out var count) ? count : 0;
    }

    public void Clear()
    {
        _taskCount.Clear();
        _tasks.Clear();
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public void ClearExceptCommentTasks()
    {
        var comments = CommentTasks.ToArray();
        Clear();
        AddRange(comments);
    }

    public void ReplaceLanguageDiagnostics(string fileName, IReadOnlyList<LanguageDiagnostic> diagnostics)
    {
        var wasInUpdate = InUpdate;
        SetInUpdate(true);
        try
        {
            RemoveLanguageDiagnosticTasks(fileName);
            AddRange(diagnostics.Select(diagnostic => UnoTask.FromDiagnostic(fileName, diagnostic)));
        }
        finally
        {
            SetInUpdate(wasInUpdate);
        }
    }

    public void ClearLanguageDiagnostics(string fileName)
    {
        var wasInUpdate = InUpdate;
        SetInUpdate(true);
        try
        {
            RemoveLanguageDiagnosticTasks(fileName);
        }
        finally
        {
            SetInUpdate(wasInUpdate);
        }
    }

    public void Add(UnoTask task)
    {
        _tasks.Add(task);
        _taskCount[task.TaskType] = GetCount(task.TaskType) + 1;
        Added?.Invoke(this, new UnoTaskEventArgs(task));
    }

    public void AddRange(IEnumerable<UnoTask> tasks)
    {
        foreach (var task in tasks)
        {
            Add(task);
        }
    }

    public void Remove(UnoTask task)
    {
        if (!_tasks.Remove(task))
        {
            return;
        }

        _taskCount[task.TaskType] = Math.Max(0, GetCount(task.TaskType) - 1);
        Removed?.Invoke(this, new UnoTaskEventArgs(task));
    }

    void RemoveLanguageDiagnosticTasks(string fileName)
    {
        foreach (var task in _tasks
            .Where(task => string.Equals(task.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && task.Tag is LanguageDiagnostic)
            .ToArray())
        {
            Remove(task);
        }
    }

    void SetInUpdate(bool value)
    {
        if (InUpdate == value)
        {
            return;
        }

        InUpdate = value;
        InUpdateChanged?.Invoke(this, EventArgs.Empty);
    }
}
