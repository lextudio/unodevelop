using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnoDevelop.Debugger;

/// Minimal debugger contract consumed by debug pads.
/// Implemented by the main assembly's DebugService and injected into pads at init time.
public interface IDebuggerService
{
    bool IsDebugging { get; }

    bool HasCache { get; }

    /// The thread ID from the most recent Stopped event.
    int CurrentThreadId { get; }

    /// Fired (threadId, reason) when the debuggee stops.
    event Action<int, string>? Stopped;

    /// Fired when execution resumes.
    event Action? Continued;

    /// Fired (filePath, 1-based line) when execution position is known.
    event Action<string, int>? ExecutionPositionChanged;

    /// Fired when the debug session ends.
    event EventHandler? DebugStopped;

    /// Fired when threads change (e.g. after a breakpoint hit).
    event Action? ThreadsChanged;

    /// Retrieve the call stack for the given thread.
    Task<IReadOnlyList<StackFrameInfo>> GetStackFramesAsync(int threadId);

    /// Retrieve local variables in a given stack frame scope.
    Task<IReadOnlyList<VariableInfo>> GetLocalsAsync(int frameId);

    /// Evaluate an expression in the current debug context.
    /// <param name="expression">The expression to evaluate (e.g. variable name).</param>
    /// <param name="frameId">The stack frame ID, or 0 for the top frame.</param>
    /// <returns>A VariableInfo with the result, or null on failure.</returns>
    Task<VariableInfo?> EvaluateAsync(string expression, int frameId = 0);

    /// Retrieve children of a variable (e.g. collection elements, object fields).
    Task<IReadOnlyList<VariableInfo>> GetChildrenAsync(int variablesReference);

    /// Retrieve all threads in the debuggee process.
    Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync();

    /// Retrieve all loaded modules (DLLs) in the debuggee process.
    Task<IReadOnlyList<ModuleInfo>> GetModulesAsync();
}

public sealed record ThreadInfo(int Id, string Name);

public sealed record ModuleInfo(int Id, string Name, string? Path, bool IsOptimized);

public sealed record StackFrameInfo(int Id, string Name, string? FilePath, int Line);

public sealed record VariableInfo(
    string Name,
    string Value,
    string Type,
    int VariablesReference,
    string? EvaluateName = null);
