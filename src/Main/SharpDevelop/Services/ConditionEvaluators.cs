using System;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project;

namespace UnoDevelop.Services;

// Live execution/solution state consulted by the AddIn-tree condition evaluators. The shell
// (MainPage) wires these to its RunService / DebugService. Kept as a tiny static provider so the
// evaluators — which are singletons in the AddInTree — stay decoupled from the shell instance.
internal static class ExecutionState
{
    public static Func<bool>? IsRunning { get; set; }
    public static Func<bool>? IsDebugging { get; set; }
    // True while a debug session is paused at a breakpoint/step (break mode).
    public static Func<bool>? IsPaused { get; set; }
    // True while a unit-test run is in progress.
    public static Func<bool>? IsTestsRunning { get; set; }

    public static bool AnyActive =>
        (IsRunning?.Invoke() ?? false) || (IsDebugging?.Invoke() ?? false);
}

// UnoDevelop counterpart of IsProcessRunningConditionEvaluator. The SolutionOpen evaluator lives in
// UnoDevelop.Conditions (declared by the Explorer addin) and is reused here. SharpDevelop reads SD.Debugger;
// UnoDevelop drives execution through RunService + DebugService, surfaced via ExecutionState.
//   <Condition name="ExecutionActive" active="True"/>   passes while running or debugging
//   <Condition name="ExecutionActive" active="False"/>  passes while idle (default)
internal sealed class ExecutionActiveConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object caller, Condition condition)
    {
        var wanted = condition.Properties["active"];
        bool want = string.IsNullOrEmpty(wanted) || bool.Parse(wanted);
        return ExecutionState.AnyActive == want;
    }
}

// Passes while a debug session is attached. Mirrors IsProcessRunning isdebugging=... but scoped to
// UnoDevelop's DebugService.
//   <Condition name="Debugging" debugging="True"/>
internal sealed class DebuggingConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object caller, Condition condition)
    {
        var wanted = condition.Properties["debugging"];
        bool want = string.IsNullOrEmpty(wanted) || bool.Parse(wanted);
        return (ExecutionState.IsDebugging?.Invoke() ?? false) == want;
    }
}

// Passes while a unit-test run is in progress. Drives the Run All Tests / Stop Tests toolbar
// buttons declaratively so their enabled state survives ToolBarService.UpdateStatus re-evaluation.
//   <Condition name="TestsRunning" running="True"/>   passes while a test run is active
//   <Condition name="TestsRunning" running="False"/>  passes while idle (default)
internal sealed class TestsRunningConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object caller, Condition condition)
    {
        var wanted = condition.Properties["running"];
        bool want = string.IsNullOrEmpty(wanted) || bool.Parse(wanted);
        return (ExecutionState.IsTestsRunning?.Invoke() ?? false) == want;
    }
}

// Passes while the debuggee is paused (break mode) — the state in which stepping is valid.
// Mirrors SharpDevelop's IsProcessRunning isprocessrunning="False".
//   <Condition name="Paused" paused="True"/>
internal sealed class PausedConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object caller, Condition condition)
    {
        var wanted = condition.Properties["paused"];
        bool want = string.IsNullOrEmpty(wanted) || bool.Parse(wanted);
        return (ExecutionState.IsPaused?.Invoke() ?? false) == want;
    }
}
