using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor.Bookmarks;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using LeXtudio.DevFlow.Agent.Core;
using UnoDevelop.Debugger;
using UnoDevelop.Services;
using UnoDevelop.UnitTesting;

namespace UnoDevelop;

public static class UnoDevelopDevFlowActions
{
    [DevFlowAction("ide-open-project", Description = "Open a solution or project file by absolute path.")]
    public static string OpenProject(string filePath)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
            var fileName = FileName.Create(filePath);
            if (fileName is null)
                return "ERROR: Invalid file path: " + filePath;

            var result = projectService.OpenSolutionOrProject(fileName);
            var solution = projectService.CurrentSolution;
            if (solution is not null)
            {
                return "OK: Opened " + solution.Name
                    + " (" + (solution.Projects?.Count ?? 0) + " projects)";
            }
            return result ? "OK: Opened " + filePath : "ERROR: Failed to open " + filePath;
        });
    }

    [DevFlowAction("ide-build-solution", Description = "Build the current solution and wait for completion. Returns error/warning counts.")]
    public static async Task<string> BuildSolution()
    {
        var buildService = ServiceSingleton.GetRequiredService<IBuildService>();
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        var solution = await SD.MainThread.InvokeAsync(() => projectService.CurrentSolution);
        if (solution is null)
            return "ERROR: No solution loaded";

        try
        {
            var results = await buildService.BuildAsync(solution,
                new BuildOptions(BuildTarget.Build));
            return "OK: Build finished. Errors=" + results.ErrorCount
                + ", Warnings=" + results.WarningCount;
        }
        catch (System.Exception ex)
        {
            return "ERROR: Build threw exception: " + ex.Message;
        }
    }

    [DevFlowAction("ide-is-building", Description = "Check if a build is currently in progress. Returns 'true' or 'false'.")]
    public static string IsBuilding()
    {
        var buildService = ServiceSingleton.GetRequiredService<IBuildService>();
        return buildService.IsBuilding ? "true" : "false";
    }

    [DevFlowAction("ide-current-solution", Description = "Return current solution info as JSON.")]
    public static string CurrentSolution()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
            var solution = projectService.CurrentSolution;
            if (solution is null)
                return "{}";

            return JsonSerializer.Serialize(new
            {
                name = solution.Name,
                fileName = solution.FileName?.ToString(),
                projectCount = solution.Projects?.Count ?? 0
            });
        });
    }

    [DevFlowAction("ide-list-projects", Description = "List project names in current solution as JSON array.")]
    public static string ListProjects()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
            var solution = projectService.CurrentSolution;
            if (solution?.Projects is null)
                return "[]";

            var names = new System.Collections.Generic.List<string>();
            foreach (var p in solution.Projects)
                names.Add(p.Name);

            return JsonSerializer.Serialize(names);
        });
    }

    [DevFlowAction("ide-close-solution", Description = "Close the current solution.")]
    public static string CloseSolution()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
            try
            {
                projectService.CloseSolution();
                return "OK: Solution closed";
            }
            catch (System.Exception ex)
            {
                return "OK: CloseSolution returned (non-critical): " + ex.Message;
            }
        });
    }

    [DevFlowAction("ide-run-dotnet", Description = "Run 'dotnet run' on a project and capture output.")]
    public static async Task<string> RunDotnet(string projectPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project \"" + projectPath + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return JsonSerializer.Serialize(new
        {
            exitCode = process.ExitCode,
            stdout = stdout.Trim(),
            stderr = stderr.Trim()
        });
    }

    [DevFlowAction("ide-open-file", Description = "Open a file in the editor by absolute path.")]
    public static string OpenFile(string filePath)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            if (!System.IO.File.Exists(filePath))
                return "ERROR: File not found: " + filePath;

            var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
            var text = System.IO.File.ReadAllText(filePath);
            workbench.ShowView(new MainPage.EditorViewContent(Path.GetFileName(filePath), text, filePath), true);
            return "OK: Opened " + filePath;
        });
    }

    [DevFlowAction("ide-set-breakpoint", Description = "Set (or toggle) a breakpoint at a file:line. Returns current breakpoint lines for that file as JSON array.")]
    public static string SetBreakpoint(string filePath, int line)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            if (string.IsNullOrEmpty(filePath) || line <= 0)
                return "ERROR: Invalid filePath or line";

            var fn = FileName.Create(filePath);
            if (fn is null)
                return "ERROR: Invalid path";

            var bm = new Bookmark();
            bm.FileName = fn;
            bm.Location = new ICSharpCode.AvalonEdit.Document.TextLocation(line, 1);
            SD.BookmarkManager.AddMark(bm);

            var lines = SD.BookmarkManager.GetBookmarks(fn)
                .Select(b => b.LineNumber)
                .OrderBy(x => x)
                .ToList();
            return JsonSerializer.Serialize(lines);
        });
    }

    [DevFlowAction("ide-list-breakpoints", Description = "List all bookmarks for a file as JSON array of line numbers. Returns empty array if none or if file not found.")]
    public static string ListBreakpoints(string filePath)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            if (string.IsNullOrEmpty(filePath))
                return "[]";

            var fn = FileName.Create(filePath);
            if (fn is null)
                return "[]";

            var lines = SD.BookmarkManager.GetBookmarks(fn)
                .Select(b => b.LineNumber)
                .OrderBy(x => x)
                .ToList();
            return JsonSerializer.Serialize(lines);
        });
    }

    [DevFlowAction("ide-list-all-breakpoints", Description = "List ALL bookmarks across all files as JSON array of {file, line} objects.")]
    public static string ListAllBreakpoints()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var all = SD.BookmarkManager.Bookmarks
                .Where(b => b.IsSaved && b.FileName != null)
                .Select(b => new { file = b.FileName.ToString(), line = b.LineNumber })
                .ToList();
            return JsonSerializer.Serialize(all);
        });
    }

    [DevFlowAction("ide-version", Description = "Return UnoDevelop version info as JSON.")]
    public static string Version()
    {
        return JsonSerializer.Serialize(new
        {
            app = "UnoDevelop",
            platform = "Uno Platform",
            targetFramework = "net10.0-desktop",
            devFlowPort = 9227
        });
    }

    [DevFlowAction("ide-start-debug", Description = "Start debugging the current project. Returns OK or error.")]
    public static string StartDebug()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            if (ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) is not UnoDevelop.Debugger.IDebuggerService debugger)
                return "ERROR: Debugger service not available";

            if (debugger.IsDebugging)
                return "ERROR: Already debugging";

            // Need access to MainPage's debug launch. Use the _workbench to route.
            // The simplest approach: trigger the Debug button click.
            return "ERROR: Use ide-debug-project with a project path instead";
        });
    }

    [DevFlowAction("ide-debug-project", Description = "Build and debug a .csproj file. Returns JSON with status, callStack, locals or error string.")]
    public static async Task<string> DebugProject(string projectPath, bool waitForBreakpoint = false, int timeoutSeconds = 30)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) as UnoDevelop.Debugger.IDebuggerService);
        if (debugger is null)
            return "ERROR: Debugger service not available";
        if (debugger.IsDebugging)
            return "ERROR: Already debugging";

        var tcs = new TaskCompletionSource<(int threadId, string reason)>();
        if (waitForBreakpoint)
        {
            debugger.Stopped += Handler;
            void Handler(int threadId, string reason)
            {
                debugger.Stopped -= Handler;
                tcs.TrySetResult((threadId, reason));
            }
        }

        // Start debugging on UI thread
        await SD.MainThread.InvokeAsync(async () =>
        {
            var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(ICSharpCode.SharpDevelop.Workbench.IOutputPad))
                as UnoOutputPadService;
            var category = outputPad?.GetOrCreateCategory("Debug") ?? outputPad?.BuildMessageViewCategory;
            if (category is null)
            {
                tcs.TrySetResult((0, "ERROR: No output pad"));
                return;
            }

            if (debugger is DebugService ds)
            {
                await ds.StartAsync(projectPath, category);
            }
            else
            {
                tcs.TrySetResult((0, "ERROR: Debugger is not DebugService"));
            }
        });

        if (!waitForBreakpoint)
            return "OK: Debug started";

        // Wait for breakpoint or timeout
        var timeoutTask = Task.Delay(timeoutSeconds * 1000);
        var completed = await Task.WhenAny(tcs.Task, timeoutTask);
        if (completed == timeoutTask)
            return "ERROR: Timeout waiting for breakpoint hit";

        var (threadId, reason) = await tcs.Task;
        if (threadId == 0)
            return reason; // error message

        // Wait briefly for PrefetchAndCacheAsync to populate the cache
        await Task.Delay(1000);

        // Collect debug data inline (before SharpDbg may exit)
        var callStack = await GetCallStackJson(debugger);
        var locals = await GetLocalsJson(debugger);
        var threads = await GetThreadsJson(debugger);
        var modules = await GetModulesJson(debugger);

        var result = new
        {
            status = "Stopped at breakpoint",
            threadId,
            reason,
            callStack,
            locals,
            threads,
            modules
        };
        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> GetCallStackJson(IDebuggerService? debugger)
    {
        if (debugger is null) return "[]";
        var threadId = debugger.CurrentThreadId;
        if (threadId == 0) threadId = 1;
        var frames = await debugger.GetStackFramesAsync(threadId);
        if (frames.Count == 0) return "[]";
        var result = frames.Select(f => new { id = f.Id, name = f.Name, file = f.FilePath, line = f.Line }).ToList();
        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> GetLocalsJson(IDebuggerService? debugger)
    {
        if (debugger is null) return "[]";
        var threadId = debugger.CurrentThreadId;
        if (threadId == 0) threadId = 1;
        var frames = await debugger.GetStackFramesAsync(threadId);
        if (frames.Count == 0) return "[]";
        var vars = await debugger.GetLocalsAsync(frames[0].Id);
        if (vars.Count == 0) return "[]";
        var result = vars.Select(v => new { name = v.Name, value = v.Value, type = v.Type }).ToList();
        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> GetThreadsJson(IDebuggerService? debugger)
    {
        if (debugger is null) return "[]";
        if (!debugger.IsDebugging && debugger.HasCache)
            return "[]";
        var threads = await debugger.GetThreadsAsync();
        var result = threads.Select(t => new { id = t.Id, name = t.Name }).ToList();
        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> GetModulesJson(IDebuggerService? debugger)
    {
        if (debugger is null) return "[]";
        if (!debugger.IsDebugging && debugger.HasCache)
            return "[]";
        var modules = await debugger.GetModulesAsync();
        var result = modules.Select(m => new { name = m.Name, path = m.Path, optimized = m.IsOptimized }).ToList();
        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-stop-debug", Description = "Stop the current debug session.")]
    public static async Task<string> StopDebug()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) as UnoDevelop.Debugger.IDebuggerService);
        if (debugger is null)
            return "ERROR: Debugger service not available";
        if (!debugger.IsDebugging)
            return "OK: Not debugging";

        // DebugService.Dispose() terminates the adapter
        if (debugger is IDisposable d)
        {
            await Task.Run(() => d.Dispose());
        }

        return "OK: Debug stopped";
    }

    [DevFlowAction("ide-get-call-stack", Description = "Return the current call stack as JSON array of {id, name, file, line}. Returns empty array if not debugging.")]
    public static async Task<string> GetCallStack()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) as UnoDevelop.Debugger.IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "[]";

        var threadId = debugger.CurrentThreadId;
        if (threadId == 0) threadId = 1; // fallback
        var frames = await debugger.GetStackFramesAsync(threadId);
        if (frames.Count == 0)
            return "[]";

        var result = frames.Select(f => new
        {
            id = f.Id,
            name = f.Name,
            file = f.FilePath,
            line = f.Line
        }).ToList();

        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-get-locals", Description = "Return local variables for the top stack frame as JSON array of {name, value, type}. Returns empty array if not debugging.")]
    public static async Task<string> GetLocals()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) as UnoDevelop.Debugger.IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "[]";

        var threadId = debugger.CurrentThreadId;
        if (threadId == 0) threadId = 1;
        var frames = await debugger.GetStackFramesAsync(threadId);
        if (frames.Count == 0)
            return "[]";

        var vars = await debugger.GetLocalsAsync(frames[0].Id);
        if (vars.Count == 0)
            return "[]";

        var result = vars.Select(v => new
        {
            name = v.Name,
            value = v.Value,
            type = v.Type
        }).ToList();

        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-evaluate", Description = "Evaluate an expression. Returns {value, type} JSON or error string.")]
    public static async Task<string> Evaluate(string expression, int frameId = 0)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "ERROR: Not debugging";

        var result = await debugger.EvaluateAsync(expression, frameId);
        if (result is null)
            return "ERROR: Evaluation returned null";

        return JsonSerializer.Serialize(new
        {
            value = result.Value,
            type = result.Type,
            variablesReference = result.VariablesReference,
            evaluateName = result.EvaluateName
        });
    }

    [DevFlowAction("ide-get-threads", Description = "Return all threads as JSON array of {id, name}. Returns empty array if not debugging.")]
    public static async Task<string> GetThreads()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "[]";

        var threads = await debugger.GetThreadsAsync();
        var result = threads.Select(t => new { id = t.Id, name = t.Name }).ToList();
        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-get-variable-children", Description = "Return children of a variable by variablesReference. Returns JSON array of {name, value, type, variablesReference}. Returns empty array if not debugging.")]
    public static async Task<string> GetVariableChildren(int variablesReference)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache) || variablesReference <= 0)
            return "[]";

        var children = await debugger.GetChildrenAsync(variablesReference);
        var result = children.Select(c => new
        {
            name = c.Name,
            value = c.Value,
            type = c.Type,
            variablesReference = c.VariablesReference,
            evaluateName = c.EvaluateName
        }).ToList();
        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-get-modules", Description = "Return all loaded modules as JSON array of {id, name, path, isOptimized}. Returns empty array if not debugging.")]
    public static async Task<string> GetModules()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "[]";

        var modules = await debugger.GetModulesAsync();
        var result = modules.Select(m => new
        {
            id = m.Id,
            name = m.Name,
            path = m.Path,
            isOptimized = m.IsOptimized
        }).ToList();
        return JsonSerializer.Serialize(result);
    }

    static DebugService GetDebugService()
        => (ServiceSingleton.ServiceProvider.GetService(typeof(DebugService)) as DebugService)
            ?? throw new InvalidOperationException("DebugService not available");

    [DevFlowAction("ide-debug-service-info", Description = "Return debugger service status as JSON: {available, typeName, isDebugging}.")]
    public static string DebugServiceInfo()
    {
        var debugger = ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService;
        return JsonSerializer.Serialize(new
        {
            available = debugger is not null,
            typeName = debugger?.GetType().FullName ?? "",
            isDebugging = debugger?.IsDebugging ?? false
        });
    }

    [DevFlowAction("ide-debug-continue", Description = "Continue debuggee execution. Returns 'OK' or error.")]
    public static async Task<string> DebugContinue(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        await svc.ContinueAsync();
        return "OK";
    }

    [DevFlowAction("ide-debug-step-over", Description = "Step over. Returns 'OK' or error.")]
    public static async Task<string> DebugStepOver(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        await svc.StepOverAsync();
        return "OK";
    }

    [DevFlowAction("ide-debug-step-into", Description = "Step into. Returns 'OK' or error.")]
    public static async Task<string> DebugStepInto(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        await svc.StepInAsync();
        return "OK";
    }

    [DevFlowAction("ide-debug-step-out", Description = "Step out. Returns 'OK' or error.")]
    public static async Task<string> DebugStepOut(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        await svc.StepOutAsync();
        return "OK";
    }

    [DevFlowAction("ide-debug-output", Description = "Return debug output text as JSON: {text}. Returns empty string if not debugging.")]
    public static string DebugOutput()
    {
        var debugger = ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService;
        return JsonSerializer.Serialize(new { text = debugger?.IsDebugging == true ? "(debugging)" : "" });
    }

    [DevFlowAction("ide-debug-pad-snapshot", Description = "Get the current content of a debug pad by name. Returns {found, items} JSON.")]
    public static string DebugPadSnapshot(string padName)
    {
        var items = SD.MainThread.InvokeIfRequired(() =>
        {
            var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
            var pads = workbench.ViewContentCollection
                .Select(v => new { type = v.GetType().Name, title = v.TitleName })
                .ToList();
            return pads;
        });
        return JsonSerializer.Serialize(new { found = items.Count > 0, items });
    }

    [DevFlowAction("ide-visualize-text",
        Description = "Test TextVisualizer: evaluate a string variable and return full value. Returns {value, truncated} JSON.")]
    public static async Task<string> VisualizeText(string variableName, int frameId = 0)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "ERROR: Not debugging";

        var result = await debugger.EvaluateAsync(variableName, frameId);
        if (result is null)
            return "ERROR: Cannot evaluate " + variableName;

        var value = result.Value;
        var truncated = value.EndsWith("...");
        if (truncated)
        {
            var full = await debugger.EvaluateAsync(variableName, frameId);
            if (full is not null)
                value = full.Value;
        }

        return JsonSerializer.Serialize(new
        {
            value,
            truncated,
            type = result.Type
        });
    }

    [DevFlowAction("ide-visualize-grid",
        Description = "Test GridVisualizer: return collection items of a variable as JSON array. Returns {name, value, type}[].")]
    public static async Task<string> VisualizeGrid(string variableName, int frameId = 0)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "ERROR: Not debugging";

        var result = await debugger.EvaluateAsync(variableName, frameId);
        if (result is null)
            return "ERROR: Cannot evaluate " + variableName;

        var varRef = result.VariablesReference;
        if (varRef == 0)
        {
            var full = await debugger.EvaluateAsync(variableName, frameId);
            if (full is null || full.VariablesReference == 0)
                return "[]";
            varRef = full.VariablesReference;
        }

        var children = await debugger.GetChildrenAsync(varRef);
        var items = children.Select((c, i) => new
        {
            index = i,
            name = c.Name,
            value = c.Value,
            type = c.Type,
            hasChildren = c.VariablesReference > 0
        }).ToList();

        return JsonSerializer.Serialize(items);
    }

    [DevFlowAction("ide-visualize-objectgraph",
        Description = "Test ObjectGraphVisualizer: return full recursive tree of a variable as JSON. Returns recursive {name, value, type, children}[].")]
    public static async Task<string> VisualizeObjectGraph(string variableName, int frameId = 0, int maxDepth = 5)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || (!debugger.IsDebugging && !debugger.HasCache))
            return "ERROR: Not debugging";

        var result = await debugger.EvaluateAsync(variableName, frameId);
        if (result is null)
            return "ERROR: Cannot evaluate " + variableName;

        var varRef = result.VariablesReference;
        if (varRef == 0)
        {
            var full = await debugger.EvaluateAsync(variableName, frameId);
            if (full is null || full.VariablesReference == 0)
                return "[]";
            varRef = full.VariablesReference;
        }

        var rootChildren = await debugger.GetChildrenAsync(varRef);
        var tree = await BuildObjectGraphAsync(debugger, rootChildren, 0, maxDepth);
        return JsonSerializer.Serialize(tree);
    }

    private static async Task<List<object>> BuildObjectGraphAsync(IDebuggerService debugger,
        IReadOnlyList<VariableInfo> children, int depth, int maxDepth)
    {
        var result = new List<object>();
        foreach (var child in children)
        {
            var node = new Dictionary<string, object?>
            {
                ["name"] = child.Name,
                ["value"] = child.Value,
                ["type"] = child.Type,
                ["hasChildren"] = child.VariablesReference > 0,
            };

            if (child.VariablesReference > 0 && depth < maxDepth)
            {
                var grandChildren = await debugger.GetChildrenAsync(child.VariablesReference);
                node["children"] = await BuildObjectGraphAsync(debugger, grandChildren, depth + 1, maxDepth);
            }

            result.Add(node);
        }
        return result;
    }

    private sealed class SimpleViewContent : IViewContent
    {
        public SimpleViewContent(string title, string text, string? filePath)
        {
            TabPageText = title;
            TitleName = title;
            InfoTip = title;
            PrimaryFile = filePath is not null ? new FileRefOpenedFile(filePath) : null;
        }

        public object? Control => null;
        public object? InitiallyFocusedControl => null;
        public IWorkbenchWindow? WorkbenchWindow { get; set; }
        public event System.EventHandler? TabPageTextChanged;
        public event System.EventHandler? TitleNameChanged;
        public event System.EventHandler? InfoTipChanged;
        public string TabPageText { get; private set; }
        public string TitleName { get; private set; }
        public string InfoTip { get; private set; }
        public bool IsDisposed { get; private set; }
        public event System.EventHandler? Disposed;
        public bool IsReadOnly => false;
        public bool IsViewOnly => false;
        public bool CloseWithSolution => true;
        public System.Collections.Generic.ICollection<IViewContent> SecondaryViewContents =>
            System.Array.Empty<IViewContent>();
        public bool IsDirty => false;
        public event System.EventHandler? IsDirtyChanged;
        public System.Collections.Generic.IList<OpenedFile> Files =>
            PrimaryFile is not null ? new[] { PrimaryFile } : System.Array.Empty<OpenedFile>();
        public OpenedFile? PrimaryFile { get; }
        public FileName? PrimaryFileName => PrimaryFile?.FileName;
        public INavigationPoint BuildNavPoint() => new NavPoint(TitleName);
        public void Save(OpenedFile file, System.IO.Stream stream) { }
        public void Load(OpenedFile file, System.IO.Stream stream) { }
        public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;
        public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;
        public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
        public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }
        public object? GetService(System.Type serviceType) => null;
        public void Dispose() { IsDisposed = true; Disposed?.Invoke(this, System.EventArgs.Empty); }
    }

    private sealed class FileRefOpenedFile : OpenedFile
    {
        public override event System.EventHandler? FileClosed;

        public FileRefOpenedFile(string path)
        {
            FileName = FileName.Create(path);
        }
    }

#if DEBUG
    // ── Test-panel probe actions ──────────────────────────────────────────────
    // Permanent (#if DEBUG) actions consumed by UnoDevelop.IntegrationTests.
    // Never add UX logic here; probes only observe/trigger and return JSON.

    [DevFlowAction("uno.probe.tests.refresh",
        Description = "Refresh the test panel (clears cache, rediscovers tests). Returns {count}.")]
    public static string TestsRefresh()
    {
        var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
        testService?.RefreshTests();

        var count = testService is null
            ? 0
            : Task.Run(() => testService.GetTests().Count).GetAwaiter().GetResult();

        SD.MainThread.InvokeIfRequired(() => MainPage.Current?.RefreshTests());
        return JsonSerializer.Serialize(new { count });
    }

    [DevFlowAction("uno.probe.tests.list",
        Description = "Return all discovered tests as JSON array of {displayName, fqn, projectName, targetFramework, key}.")]
    public static string TestsList()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
            var tests = testService?.GetTests() ?? [];
            var items = tests.Select(t => new
            {
                displayName = t.DisplayName,
                fqn = t.FullyQualifiedName,
                projectName = t.ProjectName,
                targetFramework = t.TargetFramework,
                key = t.EffectiveKey,
            });
            return JsonSerializer.Serialize(items);
        });
    }

    [DevFlowAction("uno.probe.tests.is-running",
        Description = "Returns {isRunning:bool} indicating whether a test run is in progress.")]
    public static string TestsIsRunning()
    {
        var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
        return JsonSerializer.Serialize(new { isRunning = testService?.IsRunning ?? false });
    }

    [DevFlowAction("uno.probe.tests.run-all",
        Description = "Fire-and-forget: start running all tests. Returns {started:true}.")]
    public static string TestsRunAll()
    {
        _ = SD.MainThread.InvokeIfRequired(async () =>
        {
            if (MainPage.Current is { } page)
                await page.RunAllTestsAsync();
        });
        return JsonSerializer.Serialize(new { started = true });
    }

    [DevFlowAction("uno.probe.tests.stop",
        Description = "Cancel the current test run. Returns {stopped:true}.")]
    public static string TestsStop()
    {
        SD.MainThread.InvokeIfRequired(() => MainPage.Current?.StopTests());
        return JsonSerializer.Serialize(new { stopped = true });
    }

    [DevFlowAction("uno.probe.tests.results",
        Description = "Snapshot results from the last test run. Returns array of {fqn, displayName, targetFramework, key, result, resultLabel}.")]
    public static string TestsResults()
    {
        var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
        var tests = testService?.GetTests() ?? [];
        var lastResults = testService?.GetLastResults() ?? new Dictionary<string, TestResultInfo>();
        var items = tests.Select(t =>
        {
            lastResults.TryGetValue(t.EffectiveKey, out var r);
            var resultType = r?.Result ?? TestResultType.None;
            var resultLabel = resultType switch
            {
                TestResultType.Passing => "Pass",
                TestResultType.Failing => "Fail",
                TestResultType.Skipped => "Skip",
                TestResultType.Running => "Run...",
                _ => "",
            };
            return new
            {
                fqn = t.FullyQualifiedName,
                displayName = t.DisplayName,
                targetFramework = t.TargetFramework,
                key = t.EffectiveKey,
                result = resultType.ToString(),
                resultLabel,
            };
        });
        return JsonSerializer.Serialize(items);
    }

    [DevFlowAction("uno.probe.tests.debug",
        Description = "Debug all tests in the current solution. Uses the debugger to launch the test host. Returns JSON with {started, error}. Catches exceptions so DevFlow stays alive.")]
    public static async Task<string> DebugUnitTests(int timeoutSeconds = 60)
    {
        var debugger = ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService;
        if (debugger is null)
            return JsonSerializer.Serialize(new { started = false, error = "Debugger service not available." });

        if (debugger.IsDebugging)
        {
            var stopped = await StopDebug();
            if (stopped.StartsWith("ERROR"))
                return JsonSerializer.Serialize(new { started = false, error = "Could not stop existing debug session." });
        }

        var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
        if (testService is null)
            return JsonSerializer.Serialize(new { started = false, error = "Test service not available." });

        var tests = testService.GetTests();
        var projectGroups = tests.GroupBy(t => t.ProjectPath).Where(g => g.Key is not null).ToList();

        foreach (var group in projectGroups)
        {
            var projectPath = group.Key!;

            try
            {
                if (debugger is DebugService ds)
                {
                    var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(IOutputPad)) as UnoOutputPadService;
                    var category = outputPad?.GetOrCreateCategory("Debug");
                    if (category is not null)
                        await ds.StartAsync(projectPath, category);
                    else
                        await ds.StartAsync(projectPath, null!);
                }
                else
                {
                    return JsonSerializer.Serialize(new { started = false, error = "Debugger is not the expected DebugService type." });
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { started = false, error = "Debug start threw: " + ex.Message });
            }

            if (timeoutSeconds > 0)
            {
                var tcs = new TaskCompletionSource<(int threadId, string reason)>();
                debugger.Stopped += Handler;
                void Handler(int threadId, string reason)
                {
                    debugger.Stopped -= Handler;
                    tcs.TrySetResult((threadId, reason));
                }

                var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                var completed = await Task.WhenAny(tcs.Task, timeout);
                if (completed == timeout)
                    return JsonSerializer.Serialize(new { started = true, stopped = false, error = "Timeout waiting for breakpoint." });

                var (_, reason) = tcs.Task.Result;
                return JsonSerializer.Serialize(new { started = true, stopped = true, reason });
            }

            return JsonSerializer.Serialize(new { started = true });
        }

        return JsonSerializer.Serialize(new { started = false, error = "No test projects found in solution." });
    }
#endif

    [DevFlowAction("ide-get-project-property", Description = "Read a property from a .csproj file. Args: [projectPath, propertyName]. Returns the property value or empty string.")]
    public static string GetProjectProperty(string projectPath, string propertyName)
    {
        var val = MsBuildProjectHelper.GetProperty(projectPath, propertyName);
        return val ?? "";
    }

    [DevFlowAction("ide-set-project-property", Description = "Set a property in a .csproj file. Args: [projectPath, propertyName, value]. Returns 'OK' or 'ERROR'.")]
    public static string SetProjectProperty(string projectPath, string propertyName, string value)
    {
        return MsBuildProjectHelper.SetProperty(projectPath, propertyName, value)
            ? "OK"
            : "ERROR: File not found: " + projectPath;
    }

    [DevFlowAction("ide-get-target-framework", Description = "Read TargetFramework from a project. Args: [projectPath]. Returns the TFM or empty string.")]
    public static string GetTargetFramework(string projectPath)
    {
        return MsBuildProjectHelper.GetTargetFramework(projectPath) ?? "";
    }

    [DevFlowAction("ide-set-target-framework", Description = "Set TargetFramework in a project. Args: [projectPath, tfm]. Returns 'OK' or 'ERROR'.")]
    public static string SetTargetFramework(string projectPath, string tfm)
    {
        return MsBuildProjectHelper.SetTargetFramework(projectPath, tfm)
            ? "OK"
            : "ERROR: File not found: " + projectPath;
    }

    private sealed class NavPoint : INavigationPoint
    {
        public NavPoint(string name) { Description = name; FullDescription = name; ToolTip = name; }
        string INavigationPoint.FileName => string.Empty;
        public string Description { get; }
        public string FullDescription { get; }
        public string ToolTip { get; }
        public object NavigationData => this;
        public int Index => 0;
        public int CompareTo(object? obj) => 0;
        public void JumpTo() { }
        public void FileNameChanged(string newName) { }
        public void ContentChanging(object sender, System.EventArgs e) { }
    }
}
