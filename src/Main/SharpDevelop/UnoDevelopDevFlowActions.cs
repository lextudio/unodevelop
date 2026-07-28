using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Bookmarks;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.LanguageServices.Lsp;
using ICSharpCode.SharpDevelop.LanguageServices.Roslyn;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.UI.Xaml.Controls;
using UnoDevelop.Debugger;
using UnoDevelop.Services;
using ICSharpCode.UnitTesting;
using ICSharpCode.UnitTesting.Mtp;
using ICSharpCode.SharpDevelop.Services;

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
                return """{"success":false}""";

            var result = projectService.OpenSolutionOrProject(fileName);
            var solution = projectService.CurrentSolution;
            if (solution is not null)
            {
                return """{"success":true}""";
            }
            return result ? """{"success":true}""" : """{"success":false}""";
        });
    }

    [DevFlowAction("ide-build-solution", Description = "Build the current solution and wait for completion. Returns error/warning counts.")]
    public static async Task<string> BuildSolution()
    {
        var buildService = ServiceSingleton.GetRequiredService<IBuildService>();
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        var operation = await SD.MainThread.InvokeAsync(async () =>
        {
            var solution = projectService.CurrentSolution;
            if (solution is null)
                return "ERROR: No solution loaded";

            try
            {
                var results = await buildService.BuildAsync(solution,
                    new BuildOptions(BuildTarget.Build));
                return "{\"result\":\"Success\",\"errors\":" + results.ErrorCount
                    + ",\"warnings\":" + results.WarningCount + "}";
            }
            catch (System.Exception ex)
            {
                return JsonSerializer.Serialize(new { result = "Error", message = ex.Message });
            }
        });
        return await operation;
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

    [DevFlowAction("ide-list-addins", Description = "List loaded AddIn identities as JSON.")]
    public static string ListAddIns()
    {
        return SD.MainThread.InvokeIfRequired(() =>
            JsonSerializer.Serialize(
                ServiceSingleton.GetRequiredService<IAddInTree>().AddIns
                    .Where(addIn => addIn.Enabled)
                    .SelectMany(addIn => addIn.Manifest.Identities.Keys)
                    .OrderBy(identity => identity, StringComparer.Ordinal)
                    .ToArray()));
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

            ServiceSingleton.GetRequiredService<IFileService>().OpenFile(FileName.Create(filePath), true);
            return """{"opened":true}""";
        });
    }

    [DevFlowAction("ide-set-breakpoint", Description = "Set a breakpoint at a file:line. Returns {success,file,lines}.")]
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
            return JsonSerializer.Serialize(new { success = lines.Contains(line), file = fn.ToString(), lines });
        });
    }

    [DevFlowAction("ide-clear-breakpoints", Description = "Clear all breakpoints/bookmarks. Returns {success}.")]
    public static string ClearBreakpoints()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            foreach (var bookmark in SD.BookmarkManager.Bookmarks.ToArray())
                SD.BookmarkManager.RemoveMark(bookmark);
            return """{"success":true}""";
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

    [DevFlowAction("ide-list-all-breakpoints", Description = "List ALL bookmarks across all files as JSON array of {File, Line} objects.")]
    public static string ListAllBreakpoints()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var all = SD.BookmarkManager.Bookmarks
                .Where(b => b.IsSaved && b.FileName != null)
                .Select(b => new { File = b.FileName.ToString(), Line = b.LineNumber })
                .ToList();
            return JsonSerializer.Serialize(all);
        });
    }

    [DevFlowAction("ide-pad-active-state",
        Description = "Report a pad's docking anchorable IsSelected/IsActive state by class name (e.g. 'UnoDevelop.Workbench.SolutionExplorerPad'). Returns {found, isSelected, isActive} JSON.")]
    public static string GetPadActiveState(string className)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var field = typeof(MainPage).GetField("_padWindows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var padWindows = field?.GetValue(MainPage.Current) as System.Collections.IDictionary;
            var anchorable = padWindows?[className];
            if (anchorable is null)
                return """{"found":false}""";

            var isSelected = anchorable.GetType().GetProperty("IsSelected")?.GetValue(anchorable) as bool?;
            var isActive = anchorable.GetType().GetProperty("IsActive")?.GetValue(anchorable) as bool?;
            return JsonSerializer.Serialize(new { found = true, isSelected, isActive });
        });
    }

    [DevFlowAction("ide-pad-hide", Description = "Hide a pad by ContentId (PadDescriptor.ClassName), so a docking arrangement can be authored before saving it as a layout template. Args: [contentId]. Returns {hidden} JSON.")]
    public static string HidePad(string contentId)
    {
        return SD.MainThread.InvokeIfRequired(() =>
            JsonSerializer.Serialize(new { hidden = MainPage.Current?.HidePadForTesting(contentId) ?? false }));
    }

    [DevFlowAction("ide-pad-show", Description = "Re-show a pad previously hidden via ide-pad-hide. Args: [contentId]. Returns {shown} JSON.")]
    public static string ShowPadByContentId(string contentId)
    {
        return SD.MainThread.InvokeIfRequired(() =>
            JsonSerializer.Serialize(new { shown = MainPage.Current?.ShowPadForTesting(contentId) ?? false }));
    }

    [DevFlowAction("ide-layout-save", Description = "Serialize the current docking layout to a file via the real AvalonDock XmlLayoutSerializer. Args: [path]. Returns {savedBytes} JSON.")]
    public static string SaveLayoutToFile(string path)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            MainPage.Current?.SaveCurrentLayout(path);
            var savedBytes = System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : 0;
            return JsonSerializer.Serialize(new { savedBytes });
        });
    }

    [DevFlowAction("ide-layout-restore", Description = "Deserialize a docking layout from a file via the real AvalonDock XmlLayoutSerializer, restoring pane sizes/positions and re-attaching pad content by ContentId. Args: [path].")]
    public static string RestoreLayoutFromFile(string path)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            MainPage.Current?.RestoreLayout(path);
            return JsonSerializer.Serialize(new { restored = true });
        });
    }

    [DevFlowAction("ide-layout-paths-diag", Description = "Diagnostic: report LayoutConfiguration.CurrentLayoutFileName/CurrentLayoutTemplateFileName and their File.Exists state.")]
    public static string LayoutPathsDiag()
    {
        var configFile = UnoDevelop.Workbench.LayoutConfiguration.CurrentLayoutFileName;
        var templateFile = UnoDevelop.Workbench.LayoutConfiguration.CurrentLayoutTemplateFileName;
        return JsonSerializer.Serialize(new
        {
            currentLayoutName = UnoDevelop.Workbench.LayoutConfiguration.CurrentLayoutName,
            configFile,
            configFileExists = configFile is not null && System.IO.File.Exists(configFile),
            templateFile,
            templateFileExists = templateFile is not null && System.IO.File.Exists(templateFile),
        });
    }

    [DevFlowAction("ide-dock-pane-diag", Description = "Diagnostic: report each dock pane's (Left/Right/Bottom/Document) child ContentIds/titles and DockWidth/DockHeight, to verify layout save/restore round-trips correctly and that panes stay live after a restore. Returns JSON.")]
    public static string GetDockPaneDiag()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var mp = MainPage.Current;
            if (mp is null) return "{}";
            return JsonSerializer.Serialize(mp.GetDockPaneDiagForTesting());
        });
    }

    [DevFlowAction("ide-layout-list", Description = "List all layouts currently loaded (built-in + custom), as shown in the main toolbar's layout dropdown. Returns {layouts:[{name,displayName,custom,readOnly}]} JSON.")]
    public static string ListLayouts()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var layouts = UnoDevelop.Workbench.LayoutConfiguration.Layouts
                .Select(l => new { name = l.Name, displayName = l.DisplayName, custom = l.Custom, readOnly = l.ReadOnly })
                .ToArray();
            return JsonSerializer.Serialize(new { layouts });
        });
    }

    [DevFlowAction("ide-layout-switch", Description = "Switch to a layout by name, exercising the exact Store-old/switch/Load-new sequence the main toolbar's layout dropdown runs. Args: [name]. Returns {currentLayoutName} JSON.")]
    public static string SwitchLayout(string layoutName)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            UnoDevelop.Workbench.ChooseLayoutComboBox.SwitchLayoutForTesting(layoutName);
            return JsonSerializer.Serialize(new { currentLayoutName = UnoDevelop.Workbench.LayoutConfiguration.CurrentLayoutName });
        });
    }

    [DevFlowAction("ide-layout-add", Description = "Add (and persist) a custom layout by name, exercising the same reconciliation the Edit Layouts dialog's Save button runs. Args: [name]. Returns {customLayouts} JSON.")]
    public static string AddLayout(string name)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            UnoDevelop.Workbench.ChooseLayoutComboBox.AddAndSaveLayoutForTesting(name);
            return JsonSerializer.Serialize(new { customLayouts = UnoDevelop.Workbench.LayoutConfiguration.Layouts.Where(l => l.Custom).Select(l => l.Name).ToArray() });
        });
    }

    [DevFlowAction("ide-layout-remove", Description = "Remove (and persist) a custom layout by name, exercising the same reconciliation the Edit Layouts dialog's Save button runs. Args: [name]. Returns {customLayouts} JSON.")]
    public static string RemoveLayout(string name)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            UnoDevelop.Workbench.ChooseLayoutComboBox.RemoveAndSaveLayoutForTesting(name);
            return JsonSerializer.Serialize(new { customLayouts = UnoDevelop.Workbench.LayoutConfiguration.Layouts.Where(l => l.Custom).Select(l => l.Name).ToArray() });
        });
    }

    [DevFlowAction("ide-layout-config-file-exists", Description = "Check whether the custom LayoutConfig.xml file exists on disk and contains the given layout name. Args: [name]. Returns {exists, containsName} JSON.")]
    public static string LayoutConfigFileContains(string name)
    {
        var path = System.IO.Path.Combine(UnoDevelop.Workbench.LayoutConfiguration.ConfigLayoutPath, "LayoutConfig.xml");
        var exists = System.IO.File.Exists(path);
        var containsName = exists && System.IO.File.ReadAllText(path).Contains(name, StringComparison.Ordinal);
        return JsonSerializer.Serialize(new { exists, containsName });
    }

    [DevFlowAction("ide-git-status", Description = "Report the cached git status for a file path (as computed the last time Solution Explorer's tree was built). Args: [filePath]. Returns {status} JSON.")]
    public static string GetGitStatus(string filePath)
    {
        return JsonSerializer.Serialize(new { status = UnoDevelop.Services.GitStatusService.GetStatus(filePath).ToString() });
    }

    [DevFlowAction("ide-solution-explorer-node-kinds",
        Description = "Diagnostic: tally the actual rendered Solution Explorer tree node kinds (File, GhostFile, Folder, GhostFolder, ...) plus the current ShowAllFiles toggle state. Returns {showAllFiles, kindCounts, sampleGhostFiles} JSON.")]
    public static string GetSolutionExplorerNodeKinds()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var pad = SD.Workbench.PadContentCollection.FirstOrDefault(p =>
                p.ClassName.EndsWith("SolutionExplorerPad", StringComparison.OrdinalIgnoreCase));
            var control = pad?.PadContent?.Control;
            var treeProperty = control?.GetType().GetProperty("Tree");
            var tree = treeProperty?.GetValue(control) as Microsoft.UI.Xaml.Controls.TreeView;
            if (tree is null)
                return """{"found":false,"error":"SolutionExplorerPad/Tree not found"}""";

            var kindCounts = new Dictionary<string, int>();
            var gitStatusCounts = new Dictionary<string, int>();
            var sampleGhostFiles = new List<string>();
            var sampleGitStatusFiles = new List<object>();

            void Walk(IEnumerable<Microsoft.UI.Xaml.Controls.TreeViewNode> nodes)
            {
                foreach (var node in nodes)
                {
                    var context = node.Content;
                    var kindProperty = context?.GetType().GetProperty("Kind");
                    var kind = kindProperty?.GetValue(context)?.ToString() ?? "null";
                    kindCounts[kind] = kindCounts.GetValueOrDefault(kind) + 1;

                    var gitStatus = context?.GetType().GetProperty("GitStatus")?.GetValue(context)?.ToString() ?? "null";
                    gitStatusCounts[gitStatus] = gitStatusCounts.GetValueOrDefault(gitStatus) + 1;

                    if (kind == "GhostFile" && sampleGhostFiles.Count < 20)
                    {
                        var fullPath = context?.GetType().GetProperty("FullPath")?.GetValue(context) as string;
                        sampleGhostFiles.Add(fullPath ?? "?");
                    }

                    if (gitStatus != "None" && sampleGitStatusFiles.Count < 20)
                    {
                        var fullPath = context?.GetType().GetProperty("FullPath")?.GetValue(context) as string;
                        sampleGitStatusFiles.Add(new { fullPath, gitStatus });
                    }

                    Walk(node.Children);
                }
            }

            Walk(tree.RootNodes);

            return JsonSerializer.Serialize(new
            {
                found = true,
                showAllFiles = UnoDevelop.Services.SolutionExplorerTreeBuilder.ShowAllFiles,
                kindCounts,
                sampleGhostFiles,
                gitStatusCounts,
                sampleGitStatusFiles
            });
        });
    }

    [DevFlowAction("ide-pads", Description = "List registered workbench pads as JSON array.")]
    public static string Pads()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
            var pads = workbench.PadContentCollection
                .Select(p => new { title = p.Title, className = p.ClassName })
                .ToArray();
            return JsonSerializer.Serialize(pads);
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
            return JsonSerializer.Serialize(new { started = false, error = "Debugger service not available." });
        if (debugger.IsDebugging)
            return JsonSerializer.Serialize(new { started = false, error = "Already debugging." });

        var stopSequence = debugger.CurrentStopSequence;

        // Start debugging on UI thread
        await SD.MainThread.InvokeAsync(async () =>
        {
            var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(ICSharpCode.SharpDevelop.Workbench.IOutputPad))
                as UnoOutputPadService;
            var category = outputPad?.GetOrCreateCategory("Debug") ?? outputPad?.BuildMessageViewCategory;
            if (category is null)
            {
                return;
            }

            if (debugger is DebugService ds)
            {
                await ds.StartAsync(projectPath, category);
            }
            else
            {
                throw new InvalidOperationException("Debugger is not DebugService");
            }
        });

        if (!waitForBreakpoint)
            return JsonSerializer.Serialize(new { started = true, stopped = false, isDebugging = debugger.IsDebugging, isProcessRunning = debugger.IsProcessRunning });

        if (!await WaitForStopAsync(debugger, stopSequence, timeoutSeconds))
            return JsonSerializer.Serialize(new { started = debugger.IsDebugging, stopped = false, error = "Timeout waiting for breakpoint hit.", isDebugging = debugger.IsDebugging, isProcessRunning = debugger.IsProcessRunning });

        var result = new
        {
            started = true,
            stopped = true,
            isDebugging = debugger.IsDebugging,
            isProcessRunning = debugger.IsProcessRunning,
            threadId = debugger.CurrentThreadId,
            currentFile = debugger.CurrentFile,
            currentLine = debugger.CurrentLine,
            stopSequence = debugger.CurrentStopSequence
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
        var result = frames.Select(f => new { Id = f.Id, Name = f.Name, File = f.FilePath, Line = f.Line }).ToList();
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
        var result = vars.Select(v => new { Name = v.Name, Value = v.Value, Type = v.Type }).ToList();
        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> GetThreadsJson(IDebuggerService? debugger)
    {
        if (debugger is null) return "[]";
        if (!debugger.IsProcessRunning)
            return "[]";
        var threads = await debugger.GetThreadsAsync();
        var result = threads.Select(t => new { Id = t.Id, Name = t.Name }).ToList();
        return JsonSerializer.Serialize(result);
    }

    private static async Task<string> GetModulesJson(IDebuggerService? debugger)
    {
        if (debugger is null) return "[]";
        if (!debugger.IsProcessRunning)
            return "[]";
        var modules = await debugger.GetModulesAsync();
        var result = modules.Select(m => new { Name = m.Name, Path = m.Path, Optimized = m.IsOptimized }).ToList();
        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-stop-debug", Description = "Stop the current debug session.")]
    public static async Task<string> StopDebug()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) as UnoDevelop.Debugger.IDebuggerService);
        if (debugger is null)
            return JsonSerializer.Serialize(new { success = false, error = "Debugger service not available", isDebugging = false, isProcessRunning = false });
        if (!debugger.IsDebugging && !debugger.HasCache)
            return JsonSerializer.Serialize(new { success = true, isDebugging = false, isProcessRunning = false });

        // DebugService.Dispose() terminates the adapter and clears any stale caches.
        if (debugger is IDisposable d)
        {
            await Task.Run(() => d.Dispose());
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            isDebugging = debugger.IsDebugging,
            isProcessRunning = debugger.IsProcessRunning
        });
    }

    [DevFlowAction("ide-get-call-stack", Description = "Return the current call stack as JSON array of {id, name, file, line}. Returns empty array if not debugging.")]
    public static async Task<string> GetCallStack()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) as UnoDevelop.Debugger.IDebuggerService);
        if (debugger is null || !debugger.IsProcessRunning)
            return "[]";

        var threadId = debugger.CurrentThreadId;
        if (threadId == 0) threadId = 1; // fallback
        var frames = await debugger.GetStackFramesAsync(threadId);
        if (frames.Count == 0)
            return "[]";

        var result = frames.Select(f => new
        {
            Id = f.Id,
            Name = f.Name,
            File = f.FilePath,
            Line = f.Line
        }).ToList();

        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-get-locals", Description = "Return local variables for the top stack frame as JSON array of {name, value, type}. Returns empty array if not debugging.")]
    public static async Task<string> GetLocals()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(UnoDevelop.Debugger.IDebuggerService)) as UnoDevelop.Debugger.IDebuggerService);
        if (debugger is null || !debugger.IsProcessRunning)
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
            Name = v.Name,
            Value = v.Value,
            Type = v.Type
        }).ToList();

        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-evaluate", Description = "Evaluate an expression. Returns {value, type} JSON or error string.")]
    public static async Task<string> Evaluate(string expression, int frameId = 0)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || !debugger.IsProcessRunning)
            return "ERROR: Not debugging";

        if (frameId == 0 && debugger.CurrentThreadId != 0)
        {
            var frames = await debugger.GetStackFramesAsync(debugger.CurrentThreadId);
            frameId = frames.Count > 0 ? frames[0].Id : 0;
        }

        var result = await debugger.EvaluateAsync(expression, frameId);
        if (result is null)
            return "ERROR: Evaluation returned null";

        return JsonSerializer.Serialize(new
        {
            Name = result.Name,
            Value = result.Value,
            Type = result.Type,
            VariablesReference = result.VariablesReference,
            EvaluateName = result.EvaluateName
        });
    }

    [DevFlowAction("ide-get-threads", Description = "Return all threads as JSON array of {id, name}. Returns empty array if not debugging.")]
    public static async Task<string> GetThreads()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || !debugger.IsProcessRunning)
            return "[]";

        var threads = await debugger.GetThreadsAsync();
        var result = threads.Select(t => new { Id = t.Id, Name = t.Name }).ToList();
        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-get-variable-children", Description = "Return children of a variable by variablesReference. Returns JSON array of {name, value, type, variablesReference}. Returns empty array if not debugging.")]
    public static async Task<string> GetVariableChildren(int variablesReference)
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || !debugger.IsProcessRunning || variablesReference <= 0)
            return "[]";

        var children = await debugger.GetChildrenAsync(variablesReference);
        var result = children.Select(c => new
        {
            Name = c.Name,
            Value = c.Value,
            Type = c.Type,
            VariablesReference = c.VariablesReference,
            EvaluateName = c.EvaluateName
        }).ToList();
        return JsonSerializer.Serialize(result);
    }

    [DevFlowAction("ide-get-modules", Description = "Return all loaded modules as JSON array of {id, name, path, isOptimized}. Returns empty array if not debugging.")]
    public static async Task<string> GetModules()
    {
        var debugger = SD.MainThread.InvokeIfRequired(() =>
            ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService);
        if (debugger is null || !debugger.IsProcessRunning)
            return "[]";

        var modules = await debugger.GetModulesAsync();
        var result = modules.Select(m => new
        {
            Id = m.Id,
            Name = m.Name,
            Path = m.Path,
            IsOptimized = m.IsOptimized
        }).ToList();
        return JsonSerializer.Serialize(result);
    }

    static DebugService GetDebugService()
        => (ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as DebugService)
            ?? throw new InvalidOperationException("DebugService not available");

    [DevFlowAction("ide-debug-service-info", Description = "Return debugger service status as JSON.")]
    public static string DebugServiceInfo()
    {
        var debugger = ServiceSingleton.ServiceProvider.GetService(typeof(IDebuggerService)) as IDebuggerService;
        return JsonSerializer.Serialize(new
        {
            available = debugger is not null,
            typeName = debugger?.GetType().FullName ?? "",
            isDebugging = debugger?.IsDebugging ?? false,
            isProcessRunning = debugger?.IsProcessRunning ?? false,
            hasCache = debugger?.HasCache ?? false,
            currentThreadId = debugger?.CurrentThreadId ?? 0,
            currentStopSequence = debugger?.CurrentStopSequence ?? 0,
            currentFile = debugger?.CurrentFile,
            currentLine = debugger?.CurrentLine ?? 0
        });
    }

    [DevFlowAction("ide-debug-continue", Description = "Continue debuggee execution. Returns 'OK' or error.")]
    public static async Task<string> DebugContinue(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        var stopSequence = svc.CurrentStopSequence;
        await svc.ContinueAsync();
        var stopped = await WaitForStopAsync(svc, stopSequence, timeoutSeconds);
        return JsonSerializer.Serialize(DebugLocation(svc, stopped));
    }

    [DevFlowAction("ide-debug-step-over", Description = "Step over. Returns 'OK' or error.")]
    public static async Task<string> DebugStepOver(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        var stopSequence = svc.CurrentStopSequence;
        await svc.StepOverAsync();
        var stopped = await WaitForStopAsync(svc, stopSequence, timeoutSeconds);
        return JsonSerializer.Serialize(DebugLocation(svc, stopped));
    }

    [DevFlowAction("ide-debug-step-into", Description = "Step into. Returns 'OK' or error.")]
    public static async Task<string> DebugStepInto(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        var stopSequence = svc.CurrentStopSequence;
        await svc.StepInAsync();
        var stopped = await WaitForStopAsync(svc, stopSequence, timeoutSeconds);
        return JsonSerializer.Serialize(DebugLocation(svc, stopped));
    }

    [DevFlowAction("ide-debug-step-out", Description = "Step out. Returns 'OK' or error.")]
    public static async Task<string> DebugStepOut(int timeoutSeconds = 30)
    {
        var svc = GetDebugService();
        var stopSequence = svc.CurrentStopSequence;
        await svc.StepOutAsync();
        var stopped = await WaitForStopAsync(svc, stopSequence, timeoutSeconds);
        return JsonSerializer.Serialize(DebugLocation(svc, stopped));
    }

    [DevFlowAction("ide-debug-output", Description = "Return debug output text as JSON: {text}. Returns empty string if not debugging.")]
    public static string DebugOutput()
    {
        var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(IOutputPad)) as UnoOutputPadService;
        var text = outputPad?.Categories.FirstOrDefault(c => c.DisplayCategory == "Debug")?.Text ?? string.Empty;
        return JsonSerializer.Serialize(new { text });
    }

    [DevFlowAction("ide-build-output", Description = "Return the 'Build' output category text. Returns JSON: {text}.")]
    public static string BuildOutputText()
    {
        var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(IOutputPad)) as UnoOutputPadService;
        var text = outputPad?.Categories.FirstOrDefault(c => c.DisplayCategory == "Build")?.Text ?? string.Empty;
        return JsonSerializer.Serialize(new { text });
    }

    [DevFlowAction("ide-tests-output", Description = "Return the 'Tests' output category text (build/discovery/run log, including MTP-upgrade warnings for non-MTP test projects). Returns JSON: {text}.")]
    public static string TestsOutput()
    {
        var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(IOutputPad)) as UnoOutputPadService;
        var text = outputPad?.Categories.FirstOrDefault(c => c.DisplayCategory == "Tests")?.Text ?? string.Empty;
        return JsonSerializer.Serialize(new { text });
    }

    [DevFlowAction("ide-debug-pad-snapshot", Description = "Get the current content of a debug pad by name. Returns {found, items} JSON.")]
    public static async Task<string> DebugPadSnapshot(string padName)
    {
        var snapshotTask = await SD.MainThread.InvokeAsync(async () =>
        {
            var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
            var pad = workbench.PadContentCollection.FirstOrDefault(p =>
                string.Equals(p.ClassName, padName, StringComparison.OrdinalIgnoreCase)
                || p.ClassName.EndsWith("." + padName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Title, padName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Title.Replace(" ", string.Empty), padName, StringComparison.OrdinalIgnoreCase));
            if (pad is null)
                return JsonSerializer.Serialize(new { found = false, padName, items = Array.Empty<object>() });

            pad.CreatePad();
            var content = pad.PadContent;
            var method = content?.GetType().GetMethod("GetSnapshotAsync", BindingFlags.Instance | BindingFlags.Public);
            var items = method is null ? Array.Empty<object>() : await InvokeSnapshotAsync(content!, method);
            return JsonSerializer.Serialize(new
            {
                found = true,
                title = pad.Title,
                category = pad.Category,
                className = pad.ClassName,
                items
            });
        });
        return await snapshotTask;
    }

    private static async Task<bool> WaitForStopAsync(IDebuggerService debugger, int stopSequence, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (debugger.IsDebugging
                && debugger.CurrentStopSequence > stopSequence
                && debugger.CurrentLine > 0)
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static object DebugLocation(IDebuggerService debugger, bool stopped)
        => new
        {
            stopped,
            isDebugging = debugger.IsDebugging,
            isProcessRunning = debugger.IsProcessRunning,
            threadId = debugger.CurrentThreadId,
            currentFile = debugger.CurrentFile,
            currentLine = debugger.CurrentLine,
            stopSequence = debugger.CurrentStopSequence
        };

    private static async Task<object[]> InvokeSnapshotAsync(object content, MethodInfo method)
    {
        var result = method.Invoke(content, Array.Empty<object>());
        if (result is Task<IReadOnlyList<object>> typedTask)
            return (await typedTask).ToArray();
        if (result is Task task)
        {
            await task;
            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            return (resultProperty?.GetValue(task) as IEnumerable<object>)?.ToArray() ?? Array.Empty<object>();
        }
        return (result as IEnumerable<object>)?.ToArray() ?? Array.Empty<object>();
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
            Index = i,
            Name = c.Name,
            Value = c.Value,
            Type = c.Type,
            HasChildren = c.VariablesReference > 0
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

    // Flattens the classic ICSharpCode.UnitTesting tree (ITestSolution -> ITestProject ->
    // MtpTargetFramework -> [namespace] -> class -> method) down to just the leaf MtpTestMethod
    // nodes, which is what these probe actions have always reported one-per-test - the DevFlow
    // JSON contract predates and doesn't need to know about the tree shape underneath it.
    private static IEnumerable<MtpTestMethod> EnumerateLeafTests(ITest test)
    {
        if (test is MtpTestMethod method)
        {
            yield return method;
            yield break;
        }
        foreach (var child in test.NestedTests)
            foreach (var leaf in EnumerateLeafTests(child))
                yield return leaf;
    }

    private static string MapResult(TestResultType result) => result switch
    {
        TestResultType.Success => "Passing",
        TestResultType.Failure => "Failing",
        TestResultType.Ignored => "Skipped",
        _ => "None",
    };

    [DevFlowAction("uno.probe.tests.refresh",
        Description = "Refresh the test panel (clears cache, rediscovers tests). Returns {count}.")]
    public static string TestsRefresh()
    {
        var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
        if (testService is null)
            return JsonSerializer.Serialize(new { count = 0 });

        Task refreshTask = Task.CompletedTask;
        SD.MainThread.InvokeIfRequired(() => refreshTask = MainPage.Current?.RefreshTestsAsync() ?? Task.CompletedTask);
        refreshTask.GetAwaiter().GetResult();

        var count = SD.MainThread.InvokeIfRequired(() => EnumerateLeafTests(testService.OpenSolution).Count());
        return JsonSerializer.Serialize(new { count });
    }

    [DevFlowAction("uno.probe.tests.list",
        Description = "Return all discovered tests as JSON array of {displayName, fqn, projectName, targetFramework, key}.")]
    public static string TestsList()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
            var tests = testService is null ? [] : EnumerateLeafTests(testService.OpenSolution);
            var items = tests.Select(t =>
            {
                var fqn = t.FullyQualifiedName;
                return new
                {
                    displayName = t.DisplayName,
                    fqn,
                    projectName = t.ParentProject.DisplayName,
                    targetFramework = t.TargetFramework,
                    key = fqn,
                };
            });
            return JsonSerializer.Serialize(items);
        });
    }

    [DevFlowAction("uno.probe.tests.is-running",
        Description = "Returns {isRunning:bool} indicating whether a test run is in progress.")]
    public static string TestsIsRunning()
    {
        var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
        return JsonSerializer.Serialize(new { isRunning = testService?.IsRunningTests ?? false });
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
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var testService = ServiceSingleton.ServiceProvider.GetService(typeof(ITestService)) as ITestService;
            var tests = testService is null ? [] : EnumerateLeafTests(testService.OpenSolution);
            var items = tests.Select(t =>
            {
                var resultLabel = t.Result switch
                {
                    TestResultType.Success => "Pass",
                    TestResultType.Failure => "Fail",
                    TestResultType.Ignored => "Skip",
                    _ => "",
                };
                return new
                {
                    fqn = t.FullyQualifiedName,
                    displayName = t.DisplayName,
                    targetFramework = t.TargetFramework,
                    key = t.FullyQualifiedName,
                    result = MapResult(t.Result),
                    resultLabel,
                };
            });
            return JsonSerializer.Serialize(items);
        });
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

        var projectGroups = SD.MainThread.InvokeIfRequired(() => testService.OpenSolution.NestedTests
            .OfType<MtpTestProject>()
            .Select(p => p.Project.FileName?.ToString())
            .Where(path => path is not null)
            .ToList());

        foreach (var projectPath in projectGroups)
        {
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

    [DevFlowAction("ide-active-view", Description = "Inspect the active view content.")]
    public static string GetActiveView()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var viewContent = SD.Workbench.ActiveViewContent;
            if (viewContent == null)
                return """{"active":false}""";

            var typeName = viewContent.GetType().FullName;
            var editor = viewContent.GetService<ITextEditor>();
            var textEditor = viewContent.GetService(typeof(TextEditor)) as TextEditor;
            string? text = null;
            try { text = editor?.Document.Text; } catch { }
            var fileName = viewContent.PrimaryFileName?.ToString();

            string? syntaxHighlighting = null;
            if (fileName != null)
            {
                var ext = System.IO.Path.GetExtension(fileName);
                if (ext != null)
                {
                    try
                    {
                        syntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext)?.Name;
                    }
                    catch { }
                }
            }

            return JsonSerializer.Serialize(new
            {
                active = true,
                typeName,
                isAvalonEdit = typeName != null && typeName.Contains("AvalonEdit"),
                fileName,
                syntaxHighlighting,
                editorSyntaxHighlighting = textEditor?.SyntaxHighlighting?.Name,
                highlightedLineSource = textEditor?.HighlightedLineSource?.GetType().Name,
                textLength = text?.Length
            });
        });
    }

    [DevFlowAction("ide-editor-foldings", Description = "Return the active editor folding strategy and sections.")]
    public static string GetEditorFoldings()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var editor = SD.Workbench.ActiveViewContent?.GetService(typeof(TextEditor)) as TextEditor;
            if (editor is null)
                return """{"found":false,"strategy":"None","count":0}""";
            var snapshot = MainPage.GetFoldingSnapshot(editor);
            return JsonSerializer.Serialize(new
            {
                found = true,
                strategy = snapshot.Strategy,
                count = snapshot.Count
            });
        });
    }

    [DevFlowAction("ide-xaml-preview-status",
        Description = "Check the XAML Designer secondary view's preview status for the active workbench window. Returns {found, statusText, hasRenderedPreview} JSON.")]
    public static string GetXamlPreviewStatus()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var window = SD.Workbench.ActiveWorkbenchWindow;
            var designerView = window?.ViewContents
                .FirstOrDefault(vc => vc.GetType().FullName == "ICSharpCode.XamlDesigner.XamlDesignerViewContent");

            if (designerView is null)
            {
                var bindings = AddInTree.BuildItems<DisplayBindingDescriptor>(
                    "/SharpDevelop/Workbench/DisplayBindings", null, false);
                return JsonSerializer.Serialize(new
                {
                    found = false,
                    activeFile = window?.ActiveViewContent?.PrimaryFileName?.ToString(),
                    views = window?.ViewContents.Select(vc => vc.GetType().FullName).ToArray() ?? Array.Empty<string>(),
                    displayBindings = bindings.Select(binding => new
                    {
                        binding.Id,
                        binding.IsSecondary,
                        LoadedType = binding.IsSecondary
                            ? binding.SecondaryBinding?.GetType().AssemblyQualifiedName
                            : binding.Binding?.GetType().AssemblyQualifiedName
                    }).ToArray(),
                    addIns = ServiceSingleton.GetRequiredService<IAddInTree>().AddIns
                        .SelectMany(addIn => addIn.Manifest.Identities.Keys)
                        .ToArray()
                });
            }

            var type = designerView.GetType();
            var statusText = type.GetProperty("StatusText")?.GetValue(designerView) as string;
            var hasRenderedPreview = type.GetProperty("HasRenderedPreview")?.GetValue(designerView) as bool?;
            var snapshot = type.GetMethod("GetSnapshot")?.Invoke(designerView, null) as System.Collections.IEnumerable;
            var selectedElementType = type.GetProperty("SelectedElementType")?.GetValue(designerView) as string;
            var hasSelectionAdorner = type.GetProperty("HasSelectionAdorner")?.GetValue(designerView) as bool?;

            return JsonSerializer.Serialize(new
            {
                found = true,
                statusText,
                hasRenderedPreview = hasRenderedPreview ?? false,
                activeView = window is null ? null : GetDocumentViewLabel(window, window.ActiveViewContent),
                views = window?.ViewContents.Select(view => GetDocumentViewLabel(window, view)).ToArray() ?? Array.Empty<string>(),
                items = snapshot?.Cast<object>().ToArray() ?? Array.Empty<object>(),
                selectedElementType,
                hasSelectionAdorner = hasSelectionAdorner ?? false
            });
        });
    }

    [DevFlowAction("ide-xaml-switch-view",
        Description = "Switch the active XAML document between its Source and Design views.")]
    public static string SwitchXamlView(string viewName)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var window = SD.Workbench.ActiveWorkbenchWindow;
            if (window is null)
                return """{"success":false,"error":"No active workbench window."}""";

            var index = window.ViewContents
                .Select((view, index) => new { view, index })
                .FirstOrDefault(item =>
                    string.Equals(GetDocumentViewLabel(window, item.view), viewName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.view.TabPageText, viewName, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;
            if (index < 0)
                return JsonSerializer.Serialize(new { success = false, error = "View not found: " + viewName });

            window.SwitchView(index);
            return JsonSerializer.Serialize(new
            {
                success = true,
                activeView = GetDocumentViewLabel(window, window.ActiveViewContent)
            });
        });
    }

    private static string GetDocumentViewLabel(IWorkbenchWindow window, IViewContent view)
        => window.ViewContents.IndexOf(view) == 0 && window.ViewContents.Count > 1
            ? "Code"
            : view.TabPageText;

    /// <summary>
    /// Finds a type in an AddIn's runtime assembly, forcing that assembly to load if it hasn't
    /// been touched yet - AddIns whose codons are never built at startup (e.g. Misc tool windows
    /// with no /SharpDevelop/Services registration) never get their assembly loaded into the
    /// AppDomain otherwise, so a naive AppDomain.CurrentDomain.GetAssemblies() lookup silently
    /// fails. Mirrors the established pattern in ServiceBootstrapper.InitializeTextTemplatingAddIn
    /// (addIn.Runtimes -> runtime.LoadedAssembly, which lazily loads on first access).
    /// </summary>
    private static Type? FindAddInType(string addInIdentity, string typeFullName)
    {
        var addIn = ServiceSingleton.GetRequiredService<IAddInTree>().AddIns
            .FirstOrDefault(candidate => candidate.Manifest.Identities.ContainsKey(addInIdentity));
        if (addIn is null)
            return null;

        foreach (var runtime in addIn.Runtimes)
        {
            var type = runtime.LoadedAssembly?.GetType(typeFullName);
            if (type is not null)
                return type;
        }

        return null;
    }

    [DevFlowAction("ide-open-android-device-manager",
        Description = "Open the Android Device Manager tool view. Optional args: [sdkRoot]. Returns {opened} JSON.")]
    public static string OpenAndroidDeviceManager(string? sdkRoot = null)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
            var type = FindAddInType("ICSharpCode.AndroidDeviceManager", "ICSharpCode.AndroidDeviceManager.AndroidDeviceManagerViewContent");
            if (type is null)
                return """{"opened":false,"error":"AndroidDeviceManager assembly/type not found"}""";

            var view = Activator.CreateInstance(type) as IViewContent;
            if (view is null)
                return """{"opened":false,"error":"Could not construct AndroidDeviceManagerViewContent"}""";

            if (!string.IsNullOrEmpty(sdkRoot))
            {
                var sdkPathBoxField = type.GetField("_sdkPathBox", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sdkPathBoxField?.GetValue(view) is TextBox sdkPathBox)
                    sdkPathBox.Text = sdkRoot;
            }

            workbench.ShowView(view, true);
            return """{"opened":true}""";
        });
    }

    [DevFlowAction("ide-android-device-refresh",
        Description = "Refresh the active Android Device Manager view's AVD list (runs the real `avdmanager list avd`). Returns {success} JSON.")]
    public static async Task<string> RefreshAndroidDeviceManager()
    {
        var view = await SD.MainThread.InvokeAsync(() => SD.Workbench.ActiveViewContent);
        var type = view?.GetType();
        if (type?.FullName != "ICSharpCode.AndroidDeviceManager.AndroidDeviceManagerViewContent")
            return """{"success":false,"error":"Active view is not the Android Device Manager"}""";

        var method = type.GetMethod("RefreshAsync");
        if (method?.Invoke(view, null) is Task task)
            await task;
        return """{"success":true}""";
    }

    [DevFlowAction("ide-android-device-list",
        Description = "List the AVDs currently shown in the active Android Device Manager view. Returns {found, avds:[{name,device,target,basedOn}]} JSON.")]
    public static string ListAndroidDevices()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var view = SD.Workbench.ActiveViewContent;
            var type = view?.GetType();
            if (type?.FullName != "ICSharpCode.AndroidDeviceManager.AndroidDeviceManagerViewContent")
                return """{"found":false}""";

            var method = type.GetMethod("GetAvdsForTesting");
            var avds = (method?.Invoke(view, null) as System.Collections.IEnumerable)?.Cast<object>()
                .Select(avd =>
                {
                    var avdType = avd.GetType();
                    return new
                    {
                        name = avdType.GetProperty("Name")?.GetValue(avd) as string,
                        device = avdType.GetProperty("Device")?.GetValue(avd) as string,
                        target = avdType.GetProperty("Target")?.GetValue(avd) as string,
                        basedOn = avdType.GetProperty("BasedOn")?.GetValue(avd) as string
                    };
                }).ToArray() ?? Array.Empty<object>();

            return JsonSerializer.Serialize(new { found = true, avds });
        });
    }

    [DevFlowAction("ide-open-addin-scout",
        Description = "Open the AddIn Scout tool view (lists loaded AddIns, their enabled state, and the AddInTree). Returns {opened} JSON.")]
    public static string OpenAddInScout()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
            var type = FindAddInType("UnoDevelop.AddInScout", "UnoDevelop.AddIns.Misc.AddInScout.AddInScoutViewContent");
            if (type is null)
                return """{"opened":false,"error":"AddInScout assembly/type not found"}""";

            var view = Activator.CreateInstance(type) as IViewContent;
            if (view is null)
                return """{"opened":false,"error":"Could not construct AddInScoutViewContent"}""";

            workbench.ShowView(view, true);
            return """{"opened":true}""";
        });
    }

    [DevFlowAction("ide-addin-scout-list",
        Description = "List the AddIns shown in the active AddIn Scout view. Returns {found, addIns:[{name,identity,enabled,preinstalled}]} JSON.")]
    public static string ListAddInScoutAddIns()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var view = SD.Workbench.ActiveViewContent;
            var type = view?.GetType();
            if (type?.FullName != "UnoDevelop.AddIns.Misc.AddInScout.AddInScoutViewContent")
                return """{"found":false}""";

            var method = type.GetMethod("GetAddInsForTesting");
            var addIns = (method?.Invoke(view, null) as System.Collections.IEnumerable)?.Cast<object>()
                .Select(item =>
                {
                    var itemType = item.GetType();
                    return new
                    {
                        name = itemType.GetField("Item1")?.GetValue(item) as string,
                        identity = itemType.GetField("Item2")?.GetValue(item) as string,
                        enabled = itemType.GetField("Item3")?.GetValue(item) as bool?,
                        preinstalled = itemType.GetField("Item4")?.GetValue(item) as bool?
                    };
                }).ToArray() ?? Array.Empty<object>();

            return JsonSerializer.Serialize(new { found = true, addIns });
        });
    }

    [DevFlowAction("ide-addin-toggle-enabled",
        Description = "Toggle an AddIn's enabled state by name or primary identity, via the active AddIn Scout view. Persists via the real upstream AddInManager. Returns {success, enabled} JSON.")]
    public static string ToggleAddInEnabled(string nameOrIdentity)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var view = SD.Workbench.ActiveViewContent;
            var type = view?.GetType();
            if (type?.FullName != "UnoDevelop.AddIns.Misc.AddInScout.AddInScoutViewContent")
                return """{"success":false,"error":"Active view is not the AddIn Scout"}""";

            var method = type.GetMethod("ToggleEnabledByName");
            var result = method?.Invoke(view, new object[] { nameOrIdentity }) as bool?;
            if (result is null)
                return JsonSerializer.Serialize(new { success = false, error = $"AddIn '{nameOrIdentity}' not found" });

            return JsonSerializer.Serialize(new { success = true, enabled = result.Value });
        });
    }

    [DevFlowAction("ide-addin-nuget-search",
        Description = "Search configured NuGet feeds for AddIn packages via the active AddIn Scout view's NuGet tab (AddInManager2's Available-tab equivalent). Args: [searchTerm]. Returns {found, results:[{id,version,description}]} JSON.")]
    public static async Task<string> SearchAddInNuGetPackages(string searchTerm)
    {
        var view = SD.MainThread.InvokeIfRequired(() => SD.Workbench.ActiveViewContent);
        var type = view?.GetType();
        if (type?.FullName != "UnoDevelop.AddIns.Misc.AddInScout.AddInScoutViewContent")
            return """{"found":false,"error":"Active view is not the AddIn Scout"}""";

        var method = type.GetMethod("SearchNuGetForTestingAsync");
        var task = (Task?)method?.Invoke(view, new object?[] { searchTerm, CancellationToken.None });
        if (task is null)
            return """{"found":false,"error":"SearchNuGetForTestingAsync not found"}""";

        await task;
        var resultProperty = task.GetType().GetProperty("Result");
        var results = (resultProperty?.GetValue(task) as System.Collections.IEnumerable)?.Cast<object>()
            .Select(item =>
            {
                var itemType = item.GetType();
                return new
                {
                    id = itemType.GetProperty("Id")?.GetValue(item) as string,
                    version = itemType.GetProperty("Version")?.GetValue(item) as string,
                    description = itemType.GetProperty("Description")?.GetValue(item) as string
                };
            }).ToArray() ?? Array.Empty<object>();

        return JsonSerializer.Serialize(new { found = true, results });
    }

    [DevFlowAction("ide-addin-nuget-install",
        Description = "Download, extract, and register a NuGet-packaged AddIn via the active AddIn Scout view's NuGet tab. Args: [packageId, version]. Returns {success, installDirectory, addInFiles, error} JSON.")]
    public static async Task<string> InstallAddInFromNuGet(string packageId, string version)
    {
        var view = SD.MainThread.InvokeIfRequired(() => SD.Workbench.ActiveViewContent);
        var type = view?.GetType();
        if (type?.FullName != "UnoDevelop.AddIns.Misc.AddInScout.AddInScoutViewContent")
            return """{"success":false,"error":"Active view is not the AddIn Scout"}""";

        var method = type.GetMethod("InstallFromNuGetAsync");
        var task = (Task?)method?.Invoke(view, new object?[] { packageId, version, CancellationToken.None });
        if (task is null)
            return """{"success":false,"error":"InstallFromNuGetAsync not found"}""";

        await task;
        var resultProperty = task.GetType().GetProperty("Result");
        var result = resultProperty?.GetValue(task);
        var resultType = result?.GetType();

        return JsonSerializer.Serialize(new
        {
            success = resultType?.GetProperty("Success")?.GetValue(result) as bool? ?? false,
            installDirectory = resultType?.GetProperty("InstallDirectory")?.GetValue(result) as string,
            addInFiles = (resultType?.GetProperty("AddInFiles")?.GetValue(result) as System.Collections.IEnumerable)?.Cast<string>().ToArray() ?? Array.Empty<string>(),
            // Named "installError" (not "error") - InvokeAsync treats any "error" JSON property,
            // even a null one, as a fatal probe failure regardless of "success".
            installError = resultType?.GetProperty("Error")?.GetValue(result) as string
        });
    }

    [DevFlowAction("ide-addin-nuget-uninstall",
        Description = "Unregister and delete a package-installed AddIn by name or primary identity, via the active AddIn Scout view's NuGet tab. Preinstalled AddIns cannot be uninstalled this way. Returns {success} JSON.")]
    public static string UninstallAddInFromNuGet(string nameOrIdentity)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var view = SD.Workbench.ActiveViewContent;
            var type = view?.GetType();
            if (type?.FullName != "UnoDevelop.AddIns.Misc.AddInScout.AddInScoutViewContent")
                return """{"success":false,"error":"Active view is not the AddIn Scout"}""";

            var method = type.GetMethod("UninstallByName");
            var result = method?.Invoke(view, new object[] { nameOrIdentity }) as bool?;
            return JsonSerializer.Serialize(new { success = result ?? false });
        });
    }

    [DevFlowAction("ide-resolve-resource-key",
        Description = "Resolve a resource key to its value by searching all .resx files under a directory (contract-first slice of OpenDevelop's Hornung.ResourceToolkit ResourceResolverService - directory-scoped key lookup, not full editor-caret AST resolution). Args: [directory, keyName]. Returns {found, file, value, comment} JSON.")]
    public static string ResolveResourceKey(string directory, string keyName)
    {
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
            return """{"found":false,"error":"Directory not found"}""";

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.resx", System.IO.SearchOption.AllDirectories))
        {
            LeXtudio.OpenDevelop.ResourceFiles.ResourceEntry? match;
            try
            {
                match = LeXtudio.OpenDevelop.ResourceFiles.ResourceFileReader.Read(file)
                    .FirstOrDefault(entry => string.Equals(entry.Name, keyName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                continue;
            }

            if (match is not null)
            {
                return JsonSerializer.Serialize(new
                {
                    found = true,
                    file,
                    value = match.Value,
                    comment = match.Comment
                });
            }
        }

        return """{"found":false}""";
    }

    [DevFlowAction("ide-resolve-resource-at-cursor",
        Description = "Resolve the resource-key string literal at a caret position in a C# or VB file (full editor-caret AST resolution, adapted from OpenDevelop's Hornung.ResourceToolkit Bcl/ICSharpCodeCore Roslyn resolvers). Args: [filePath, offset]. Returns {found, key, kind, value, comment?, resxFile?} JSON.")]
    public static string ResolveResourceAtCursor(string filePath, int offset)
    {
        if (!System.IO.File.Exists(filePath))
            return """{"found":false,"error":"File not found"}""";

        var language = ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.LanguageFromFileName(filePath);
        if (language is null)
            return """{"found":false,"error":"Unsupported file type (expected .cs or .vb)"}""";

        var fileContent = System.IO.File.ReadAllText(filePath);
        var reference = ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.FindResourceKeyAtCursor(language, fileContent, offset);
        if (reference is null)
            return """{"found":false}""";

        if (reference.Kind == ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.ResourceReferenceKind.CoreResourceService)
        {
            try
            {
                var value = SD.ResourceService.GetString(reference.Key);
                return JsonSerializer.Serialize(new
                {
                    found = true,
                    key = reference.Key,
                    kind = "CoreResourceService",
                    resolved = true,
                    value
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    found = true,
                    key = reference.Key,
                    kind = "CoreResourceService",
                    resolved = false,
                    resolveError = ex.Message
                });
            }
        }

        // BclResourceManager: resolve via .resx lookup under the containing project (falls back
        // to the file's own directory if it isn't part of an open project), reusing the same
        // directory-scoped lookup as ide-resolve-resource-key.
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        var project = projectService.CurrentSolution?.Projects?
            .FirstOrDefault(p => p.FileName is not null
                && filePath.StartsWith(System.IO.Path.GetDirectoryName(p.FileName.ToString()) ?? "@@none@@", StringComparison.OrdinalIgnoreCase));
        var searchDirectory = project?.FileName is not null
            ? System.IO.Path.GetDirectoryName(project.FileName.ToString())
            : System.IO.Path.GetDirectoryName(filePath);

        if (string.IsNullOrEmpty(searchDirectory) || !System.IO.Directory.Exists(searchDirectory))
        {
            return JsonSerializer.Serialize(new { found = true, key = reference.Key, kind = "BclResourceManager", resolved = false });
        }

        foreach (var resxFile in System.IO.Directory.EnumerateFiles(searchDirectory, "*.resx", System.IO.SearchOption.AllDirectories))
        {
            LeXtudio.OpenDevelop.ResourceFiles.ResourceEntry? match;
            try
            {
                match = LeXtudio.OpenDevelop.ResourceFiles.ResourceFileReader.Read(resxFile)
                    .FirstOrDefault(entry => string.Equals(entry.Name, reference.Key, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                continue;
            }

            if (match is not null)
            {
                return JsonSerializer.Serialize(new
                {
                    found = true,
                    key = reference.Key,
                    kind = "BclResourceManager",
                    resolved = true,
                    value = match.Value,
                    comment = match.Comment,
                    resxFile
                });
            }
        }

        return JsonSerializer.Serialize(new { found = true, key = reference.Key, kind = "BclResourceManager", resolved = false });
    }

    [DevFlowAction("ide-find-unused-resource-keys",
        Description = "Find .resx keys under a directory that have no BclResourceManager-pattern reference in any .cs/.vb file under the same directory (adapted from OpenDevelop's ResourceRefactoringService.FindUnusedKeys / UnusedResourceKeysViewContent - directory-scoped, not full-solution scope tracking). Args: [directory]. Returns {found, unused:[{resxFile,key}]} JSON.")]
    public static string FindUnusedResourceKeys(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
            return """{"found":false,"error":"Directory not found"}""";

        var referencedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in System.IO.Directory.EnumerateFiles(directory, "*.*", System.IO.SearchOption.AllDirectories)
            .Where(f => ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.LanguageFromFileName(f) is not null))
        {
            var language = ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.LanguageFromFileName(sourceFile)!;
            string content;
            try
            {
                content = System.IO.File.ReadAllText(sourceFile);
            }
            catch
            {
                continue;
            }

            foreach (var occurrence in ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.FindAllResourceReferences(language, content))
            {
                if (occurrence.Kind == ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.ResourceReferenceKind.BclResourceManager)
                    referencedKeys.Add(occurrence.Key);
            }
        }

        var unused = new List<object>();
        foreach (var resxFile in System.IO.Directory.EnumerateFiles(directory, "*.resx", System.IO.SearchOption.AllDirectories))
        {
            IReadOnlyList<LeXtudio.OpenDevelop.ResourceFiles.ResourceEntry> entries;
            try
            {
                entries = LeXtudio.OpenDevelop.ResourceFiles.ResourceFileReader.Read(resxFile);
            }
            catch
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.IsEditable && entry.Type.Equals("string", StringComparison.OrdinalIgnoreCase)
                    && !referencedKeys.Contains(entry.Name))
                {
                    unused.Add(new { resxFile, key = entry.Name });
                }
            }
        }

        return JsonSerializer.Serialize(new { found = true, unused });
    }

    [DevFlowAction("ide-rename-resource-key",
        Description = "Rename a .resx key and rewrite all BclResourceManager-pattern string-literal references to it in .cs files under a directory (adapted from OpenDevelop's ResourceRefactoringService.Rename). Args: [directory, oldKey, newKey]. Returns {success, resxFile?, updatedFiles:[...], error?} JSON.")]
    public static string RenameResourceKey(string directory, string oldKey, string newKey)
    {
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
            return """{"success":false,"error":"Directory not found"}""";
        if (string.IsNullOrWhiteSpace(newKey))
            return """{"success":false,"error":"New key must not be empty"}""";

        string? resxFile = null;
        foreach (var candidate in System.IO.Directory.EnumerateFiles(directory, "*.resx", System.IO.SearchOption.AllDirectories))
        {
            IReadOnlyList<LeXtudio.OpenDevelop.ResourceFiles.ResourceEntry> entries;
            try
            {
                entries = LeXtudio.OpenDevelop.ResourceFiles.ResourceFileReader.Read(candidate);
            }
            catch
            {
                continue;
            }

            if (entries.Any(e => string.Equals(e.Name, newKey, StringComparison.OrdinalIgnoreCase)))
                return JsonSerializer.Serialize(new { success = false, error = $"Key '{newKey}' already exists in {candidate}" });

            var match = entries.FirstOrDefault(e => string.Equals(e.Name, oldKey, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.Name = newKey;
                LeXtudio.OpenDevelop.ResourceFiles.ResourceFileReader.SaveResX(candidate, entries);
                resxFile = candidate;
                break;
            }
        }

        if (resxFile is null)
            return JsonSerializer.Serialize(new { success = false, error = $"Key '{oldKey}' not found in any .resx under {directory}" });

        var updatedFiles = new List<string>();
        foreach (var sourceFile in System.IO.Directory.EnumerateFiles(directory, "*.*", System.IO.SearchOption.AllDirectories)
            .Where(f => ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.LanguageFromFileName(f) is not null))
        {
            var language = ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.LanguageFromFileName(sourceFile)!;
            string content;
            try
            {
                content = System.IO.File.ReadAllText(sourceFile);
            }
            catch
            {
                continue;
            }

            var occurrences = ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.FindAllResourceReferences(language, content)
                .Where(o => o.Kind == ICSharpCode.SharpDevelop.LanguageServices.Roslyn.ResourceReferenceResolver.ResourceReferenceKind.BclResourceManager
                    && string.Equals(o.Key, oldKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.Offset)
                .ToList();
            if (occurrences.Count == 0)
                continue;

            var updated = content;
            foreach (var occurrence in occurrences)
            {
                // occurrence.Offset/Length span the whole string-literal token including quotes;
                // replace only the inner text so escaping/quote style is preserved.
                var literalText = updated.Substring(occurrence.Offset, occurrence.Length);
                var replaced = literalText.Replace(oldKey, newKey, StringComparison.OrdinalIgnoreCase);
                updated = updated[..occurrence.Offset] + replaced + updated[(occurrence.Offset + occurrence.Length)..];
            }

            System.IO.File.WriteAllText(sourceFile, updated);
            updatedFiles.Add(sourceFile);
        }

        return JsonSerializer.Serialize(new { success = true, resxFile, updatedFiles });
    }

    [DevFlowAction("ide-open-android-sdk-manager",
        Description = "Open the Android SDK Manager tool view. Optional args: [sdkRoot]. Returns {opened} JSON.")]
    public static string OpenAndroidSdkManager(string? sdkRoot = null)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
            var type = FindAddInType("ICSharpCode.AndroidSdkManager", "ICSharpCode.AndroidSdkManager.AndroidSdkManagerViewContent");
            if (type is null)
                return """{"opened":false,"error":"AndroidSdkManager assembly/type not found"}""";

            var view = Activator.CreateInstance(type) as IViewContent;
            if (view is null)
                return """{"opened":false,"error":"Could not construct AndroidSdkManagerViewContent"}""";

            if (!string.IsNullOrEmpty(sdkRoot))
            {
                var sdkPathBoxField = type.GetField("_sdkPathBox", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sdkPathBoxField?.GetValue(view) is TextBox sdkPathBox)
                    sdkPathBox.Text = sdkRoot;
            }

            workbench.ShowView(view, true);
            return """{"opened":true}""";
        });
    }

    [DevFlowAction("ide-android-sdk-refresh",
        Description = "Refresh the active Android SDK Manager view's package list (runs the real `sdkmanager --list --verbose`). Returns {success} JSON.")]
    public static async Task<string> RefreshAndroidSdkManager()
    {
        var view = await SD.MainThread.InvokeAsync(() => SD.Workbench.ActiveViewContent);
        var type = view?.GetType();
        if (type?.FullName != "ICSharpCode.AndroidSdkManager.AndroidSdkManagerViewContent")
            return """{"success":false,"error":"Active view is not the Android SDK Manager"}""";

        var method = type.GetMethod("RefreshAsync");
        if (method?.Invoke(view, null) is Task task)
            await task;
        return """{"success":true}""";
    }

    [DevFlowAction("ide-android-sdk-list",
        Description = "List the packages currently shown in the active Android SDK Manager view. Returns {found, packages:[{id,versionText,statusText,isInstalled,hasUpdate}]} JSON.")]
    public static string ListAndroidSdkPackages()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var view = SD.Workbench.ActiveViewContent;
            var type = view?.GetType();
            if (type?.FullName != "ICSharpCode.AndroidSdkManager.AndroidSdkManagerViewContent")
                return """{"found":false}""";

            var method = type.GetMethod("GetPackagesForTesting");
            var packages = (method?.Invoke(view, null) as System.Collections.IEnumerable)?.Cast<object>()
                .Select(package =>
                {
                    var packageType = package.GetType();
                    return new
                    {
                        id = packageType.GetProperty("Id")?.GetValue(package) as string,
                        versionText = packageType.GetProperty("VersionText")?.GetValue(package) as string,
                        statusText = packageType.GetProperty("StatusText")?.GetValue(package) as string,
                        isInstalled = packageType.GetProperty("IsInstalled")?.GetValue(package) as bool?,
                        hasUpdate = packageType.GetProperty("HasUpdate")?.GetValue(package) as bool?
                    };
                }).ToArray() ?? Array.Empty<object>();

            return JsonSerializer.Serialize(new { found = true, packages });
        });
    }

    [DevFlowAction("ide-resource-entries",
        Description = "List the resource entries of the active .resx/.resources viewer. Returns {found, canEdit, entries:[{name,type,displaySummary,isEditable}]} JSON.")]
    public static string GetResourceEntries()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var view = SD.Workbench.ActiveViewContent as UnoDevelop.Workbench.ResourceViewerViewContent;
            if (view is null)
                return """{"found":false}""";

            return JsonSerializer.Serialize(new
            {
                found = true,
                canEdit = !view.IsReadOnly,
                entries = view.Entries.Select(entry => new
                {
                    name = entry.Name,
                    type = entry.Type,
                    displaySummary = entry.DisplaySummary,
                    isEditable = entry.IsEditable
                }).ToArray()
            });
        });
    }

    [DevFlowAction("ide-xaml-designer-tap-element",
        Description = "Simulate a Tapped gesture on a rendered design-surface element (same path as a real click) - selects the element AND brings its document's tab back to the active/highlighted state, even if a different tab was active. Args: [typeName, index]. Returns {tapped, activeDocumentFile} JSON.")]
    public static string TapXamlDesignerElement(string typeName, int index = 0)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            // Not FindActiveXamlDesigner(): this action's whole point is reactivating a
            // currently-INACTIVE XAML designer tab, so it must find the designer regardless of
            // which window is active right now. XamlDesignerViewContent is a SECONDARY view
            // content (attached to the .xaml file's primary text-editor view), not a top-level
            // entry of its own in ViewContentCollection.
            var designer = SD.Workbench.ViewContentCollection
                .SelectMany(view => view.SecondaryViewContents)
                .FirstOrDefault(view => view.GetType().FullName == "ICSharpCode.XamlDesigner.XamlDesignerViewContent");
            var tapped = designer?.GetType().GetMethod("SimulateElementTapForTesting")
                ?.Invoke(designer, new object[] { typeName, index }) as bool? ?? false;

            // SD.Workbench.ActiveViewContent is only updated by the app explicitly
            // opening/activating a view via code - it is NOT wired up to the docking UI's own
            // LayoutDocument.IsActive, which is what actually drives a tab's active/highlighted
            // color (see LayoutDocumentPaneControl). Read that directly via MainPage's private
            // per-view-content LayoutDocument map to verify the tab itself, not just app-level
            // bookkeeping that clicking a real tab doesn't update either.
            var documentsField = typeof(MainPage).GetField("_documents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var documents = documentsField?.GetValue(MainPage.Current) as System.Collections.IDictionary;
            string? activeDocumentFile = null;
            if (documents is not null)
            {
                foreach (System.Collections.DictionaryEntry entry in documents)
                {
                    var isActive = entry.Value?.GetType().GetProperty("IsActive")?.GetValue(entry.Value) as bool? ?? false;
                    if (isActive)
                    {
                        activeDocumentFile = (entry.Key as IViewContent)?.PrimaryFileName?.ToString();
                        break;
                    }
                }
            }

            return JsonSerializer.Serialize(new { tapped, activeDocumentFile });
        });
    }

    [DevFlowAction("ide-xaml-designer-select",
        Description = "Select a rendered XAML element by runtime type name.")]
    public static string SelectXamlDesignerElement(string typeName, int index = 0)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var designer = FindActiveXamlDesigner();
            var selected = designer?.GetType().GetMethod("SelectElementByType")
                ?.Invoke(designer, new object[] { typeName, index }) as bool? ?? false;
            return JsonSerializer.Serialize(new { success = selected, typeName, index });
        });
    }

    [DevFlowAction("ide-xaml-designer-add",
        Description = "Add a XAML toolbox snippet to the selected design container.")]
    public static string AddXamlDesignerElement(string xaml)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var designer = FindActiveXamlDesigner();
            var added = designer?.GetType().GetMethod("AddToolboxItem")
                ?.Invoke(designer, new object[] { xaml }) as bool? ?? false;
            return JsonSerializer.Serialize(new { success = added, xaml });
        });
    }

    [DevFlowAction("ide-xaml-source-insert",
        Description = "Insert a XAML toolbox snippet at the active source editor caret.")]
    public static string InsertXamlSourceElement(string xaml)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var active = SD.Workbench.ActiveWorkbenchWindow?.ActiveViewContent;
            var inserted = active is not null && MainPage.InsertXamlToolboxSnippet(active, xaml);
            var editor = active?.GetService(typeof(ITextEditor)) as ITextEditor;
            return JsonSerializer.Serialize(new
            {
                success = inserted,
                xaml,
                containsSnippet = editor?.Document.Text.Contains(xaml, StringComparison.Ordinal) == true
            });
        });
    }

    [DevFlowAction("ide-xaml-designer-resize",
        Description = "Resize the selected design element by width/height deltas.")]
    public static string ResizeXamlDesignerElement(double widthDelta, double heightDelta)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var designer = FindActiveXamlDesigner();
            var resized = designer?.GetType().GetMethod("ResizeSelection")
                ?.Invoke(designer, new object[] { widthDelta, heightDelta }) as bool? ?? false;
            return JsonSerializer.Serialize(new { success = resized, widthDelta, heightDelta });
        });
    }

    [DevFlowAction("ide-xaml-designer-pads",
        Description = "Return XAML Toolbox and Properties pad state.")]
    public static string GetXamlDesignerPads()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var pads = SD.Workbench.PadContentCollection;
            var toolbox = pads.FirstOrDefault(pad =>
                pad.ClassName.EndsWith(".ToolboxPad", StringComparison.Ordinal));
            var properties = pads.FirstOrDefault(pad =>
                pad.ClassName.EndsWith(".PropertiesPad", StringComparison.Ordinal));
            toolbox?.CreatePad();
            properties?.CreatePad();
            var toolboxControl = toolbox?.PadContent?.Control;
            var propertiesControl = properties?.PadContent?.Control;
            var toolboxItems = toolboxControl?.GetType().GetMethod("GetSnapshot")
                ?.Invoke(toolboxControl, null) as System.Collections.IEnumerable;
            var toolboxGroups = toolboxControl?.GetType().GetMethod("GetGroupSnapshot")
                ?.Invoke(toolboxControl, null) as System.Collections.IEnumerable;
            var propertySnapshot = propertiesControl?.GetType().GetMethod("GetSnapshot")
                ?.Invoke(propertiesControl, null);
            return JsonSerializer.Serialize(new
            {
                toolboxFound = toolbox is not null,
                toolboxHasProvider = toolboxControl?.GetType().GetProperty("HasProvider")
                    ?.GetValue(toolboxControl) as bool? ?? false,
                toolboxItems = toolboxItems?.Cast<object>().ToArray() ?? Array.Empty<object>(),
                toolboxGroups = toolboxGroups?.Cast<object>().ToArray() ?? Array.Empty<object>(),
                propertiesFound = properties is not null,
                propertySnapshot
            });
        });
    }

    [DevFlowAction("ide-xaml-toolbox-group",
        Description = "Expand or collapse a named XAML Toolbox group.")]
    public static string SetXamlToolboxGroup(string groupName, bool expanded)
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var toolbox = SD.Workbench.PadContentCollection.FirstOrDefault(pad =>
                pad.ClassName.EndsWith(".ToolboxPad", StringComparison.Ordinal));
            toolbox?.CreatePad();
            var control = toolbox?.PadContent?.Control;
            var success = control?.GetType().GetMethod("SetGroupExpanded")
                ?.Invoke(control, new object[] { groupName, expanded }) as bool? ?? false;
            return JsonSerializer.Serialize(new { success, groupName, expanded });
        });
    }

    [DevFlowAction("ide-xaml-outline", Description = "Return the shared Outline pad state.")]
    public static string GetXamlOutline()
    {
        return SD.MainThread.InvokeIfRequired(() =>
        {
            var outline = SD.Workbench.PadContentCollection.FirstOrDefault(pad =>
                pad.ClassName.EndsWith(".OutlinePad", StringComparison.Ordinal));
            outline?.CreatePad();
            var control = outline?.PadContent?.Control;
            var items = control?.GetType().GetMethod("GetSnapshot")
                ?.Invoke(control, null) as System.Collections.IEnumerable;
            return JsonSerializer.Serialize(new
            {
                outlineFound = outline is not null,
                outlineHasProvider = control?.GetType().GetProperty("HasProvider")
                    ?.GetValue(control) as bool? ?? false,
                providerError = control?.GetType().GetProperty("ProviderError")?.GetValue(control),
                items = items?.Cast<object>().ToArray() ?? Array.Empty<object>()
            });
        });
    }

    private static IViewContent? FindActiveXamlDesigner()
        => SD.Workbench.ActiveWorkbenchWindow?.ViewContents.FirstOrDefault(view =>
            view.GetType().FullName == "ICSharpCode.XamlDesigner.XamlDesignerViewContent");

    [DevFlowAction("ide-parser-status", Description = "Check whether a file has a registered LSP service.")]
    public static string GetParserStatus(string fileName)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(fileName);
            if (string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                AutoRegisterXamlLsp();

            var service = LspServiceManager.GetService(fileName);
            return JsonSerializer.Serialize(new
            {
                hasService = service != null,
                language = service?.GetType().Name?.Contains("Lsp") == true ? "LSP" : null,
                serviceType = service?.GetType().Name
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { hasService = false, error = ex.Message });
        }
    }

    static void AutoRegisterXamlLsp()
    {
        var root = FindRepositoryRoot();
        if (root == null) return;

        var axsgRoot = System.IO.Path.Combine(root, "externals", "OpenDevelop", "externals", "vscode-wpf", "external", "wxsg", "external", "XamlToCSharpGenerator");
        var lsProject = System.IO.Path.Combine(axsgRoot, "src",
            "XamlToCSharpGenerator.LanguageServer",
            "XamlToCSharpGenerator.LanguageServer.csproj");

        if (System.IO.File.Exists(lsProject))
        {
            LspServiceManager.RegisterExtension(".xaml",
                new LspServerLaunchSpec("xaml", "dotnet",
                    "run", "--project", lsProject, "--"));
        }
    }

    static string? FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "externals", "OpenDevelop", "externals", "vscode-wpf", "external", "wxsg", "external", "XamlToCSharpGenerator")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src", "Main", "Base")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [DevFlowAction("ide-complete", Description = "Trigger code completion at a given position in a file. Args: [filePath, offset]. Returns completion items JSON.")]
    public static async Task<string> Complete(string filePath, int offset)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(filePath);
            if (string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                AutoRegisterXamlLsp();

            var service = LspServiceManager.GetService(filePath);
            var documentId = new DocumentId(ICSharpCode.Core.FileName.Create(filePath));
            var text = System.IO.File.ReadAllText(filePath);
            CompletionResult result;

            if (service is not null)
            {
                await service.UpsertDocumentAsync(documentId, text, CancellationToken.None);
                result = await service.GetCompletionsAsync(documentId, offset, CancellationToken.None);
            }
            else
            {
                // .cs/.vb don't go through LspServiceManager (that's for external-LSP languages
                // like TypeScript/XAML) - they're served directly by CSharpVBLanguageService via
                // LanguageServiceRegistry. UpsertDocumentAsync bootstraps an ad-hoc single-file
                // project automatically if this file was never opened/tracked before.
                var registry = ServiceSingleton.ServiceProvider.GetService(typeof(LanguageServiceRegistry)) as LanguageServiceRegistry;
                if (registry is null || !registry.TryGetService(filePath, out var languageService)
                    || languageService is not CSharpVBLanguageService roslynService)
                {
                    return """{"triggered":false,"reason":"No language service"}""";
                }

                await roslynService.UpsertDocumentAsync(documentId, text, CancellationToken.None);
                result = await roslynService.GetCompletionsAsync(documentId, offset, CancellationToken.None);
            }

            return JsonSerializer.Serialize(new
            {
                triggered = true,
                itemCount = result.Items.Count,
                items = result.Items.Select(i => new { label = i.DisplayText }).Take(20).ToList()
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { triggered = false, error = ex.Message });
        }
    }

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

    [DevFlowAction("ide-list-project-items",
        Description = "List the display items Solution Explorer's tree builder resolves for a loaded project (name, projectCount). Diagnostic for Solution Explorer file-visibility issues. Args: [projectPath]. Returns {found, count, items:[{displayPath, physicalPath}]} JSON.")]
    public static string ListProjectItems(string projectPath)
    {
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        var project = projectService.CurrentSolution?.Projects
            .FirstOrDefault(p => string.Equals(p.FileName?.ToString(), projectPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.FileName?.ToString()?.TrimStart('/'), projectPath.TrimStart('/'), StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Name, projectPath, StringComparison.OrdinalIgnoreCase));
        if (project is null)
            return JsonSerializer.Serialize(new { found = false, count = 0, items = Array.Empty<object>() });

        var realProjectPath = project.FileName!.ToString();
        var fromProject = ICSharpCode.SharpDevelop.Services.ProjectDisplayItems.GetProjectDisplayItems(project);
        var fromDisk = UnoDevelop.Services.UnoProjectService.GetProjectDisplayItems(realProjectPath);
        return JsonSerializer.Serialize(new
        {
            found = true,
            realProjectPath,
            fromProjectCount = fromProject.Count,
            fromDiskCount = fromDisk.Count,
            fromProjectItems = fromProject.Select(i => new { i.DisplayPath, i.PhysicalPath }).ToArray(),
            onlyOnDisk = fromDisk.Where(d => !fromProject.Any(p => string.Equals(p.PhysicalPath, d.PhysicalPath, StringComparison.OrdinalIgnoreCase)))
                .Select(i => new { i.DisplayPath, i.PhysicalPath }).ToArray()
        });
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
