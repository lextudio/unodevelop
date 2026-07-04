using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.UnitTesting;

public sealed class TestService : ITestService
{
    private readonly DotNetTestRunner _runner = new();
    private List<TestInfo> _cachedTests = new();
    private Dictionary<string, TestResultInfo> _lastResults = new();
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private static readonly string _dbgLog = "/tmp/ut-debug.log";

    public TestService()
    {
        _runner.OutputLine += AppendOutputLine;
    }

    public bool IsRunning { get; private set; }

    public event Action? TestRunStarted;
    public event Action<TestResultInfo>? TestResultUpdated;
    public event Action? TestRunCompleted;

    public IReadOnlyList<TestInfo> GetTests(IProgressMonitor? progressMonitor = null)
    {
        lock (_lock)
        {
            if (_cachedTests.Count > 0)
                return _cachedTests.ToList();

            var projectService = ServiceSingleton.ServiceProvider.GetService(typeof(IProjectService)) as IProjectService;
            var solution = projectService?.CurrentSolution;
            if (solution?.Projects is null)
                return Array.Empty<TestInfo>();

            Dbg($"GetTests: {solution.Projects.Count} project(s) in solution");
            var output = PrepareOutputCategory(clear: true);

            var testProjects = solution.Projects
                .Where(TestProjectDetector.IsTestProject)
                .ToList();
            var total = testProjects.Count;
            var completed = 0;

            if (progressMonitor is not null)
                progressMonitor.TaskName = $"Refreshing tests for solution '{solution.Name}'...";

            output?.AppendLine($"Refreshing tests for solution '{solution.Name}'...");
            var all = new List<TestInfo>();
            foreach (var project in testProjects)
            {
                Dbg($"GetTests: discovering in {project.Name}");
                if (progressMonitor is not null)
                {
                    progressMonitor.TaskName = $"Discovering tests in {project.Name}...";
                    progressMonitor.Progress = (double)completed / total;
                }
                output?.AppendLine($"Discovering tests in {project.Name}...");
                var tests = DiscoverTestsForProject(project);
                Dbg($"GetTests: {project.Name} -> {tests.Count} test(s)");
                output?.AppendLine($"Discovered {tests.Count} test(s) in {project.Name}.");
                all.AddRange(tests);
                completed++;
            }

            _cachedTests = all;
            if (progressMonitor is not null)
            {
                progressMonitor.TaskName = $"Test refresh completed: {all.Count} test(s).";
                progressMonitor.Progress = 1;
            }
            output?.AppendLine($"Test refresh completed: {all.Count} test(s).");
            return all.ToList();
        }
    }

    public IReadOnlyDictionary<string, TestResultInfo> GetLastResults()
    {
        lock (_lock)
        {
            return new Dictionary<string, TestResultInfo>(_lastResults);
        }
    }

    public void RefreshTests()
    {
        lock (_lock)
        {
            _cachedTests.Clear();
        }
    }

    public async Task RunTestsAsync(IReadOnlyList<string> fullyQualifiedNames)
    {
        if (IsRunning || fullyQualifiedNames.Count == 0)
            return;

        var tests = GetTests();
        var testsByKey = tests.ToDictionary(t => t.EffectiveKey, StringComparer.Ordinal);
        var byProject = fullyQualifiedNames
            .Select(key => testsByKey.TryGetValue(key, out var test) ? test : null)
            .Where(t => t is not null)
            .Where(t => !string.IsNullOrEmpty(t!.ProjectPath))
            .GroupBy(t => new
            {
                ProjectPath = t!.ProjectPath!,
                t.TargetFramework,
            })
            .ToList();

        if (byProject.Count == 0)
            return;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        TestRunStarted?.Invoke();

        try
        {
            foreach (var group in byProject)
            {
                if (_cts.IsCancellationRequested) break;

                var projectPath = group.Key.ProjectPath;
                var targetFramework = group.Key.TargetFramework;
                var testUids = group
                    .Select(t => t.Uid)
                    .Where(uid => !string.IsNullOrEmpty(uid))
                    .Select(uid => uid!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var keysByName = group
                    .GroupBy(t => t.FullyQualifiedName, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Select(t => t.EffectiveKey).First(), StringComparer.Ordinal);
                await RunTestsInProjectAsync(projectPath, targetFramework, testUids, keysByName, _cts.Token);
            }
        }
        finally
        {
            IsRunning = false;
            _cts = null;
            TestRunCompleted?.Invoke();
        }
    }

    public async Task RunAllTestsAsync()
    {
        var tests = GetTests();
        if (tests.Count == 0) return;
        await RunTestsAsync(tests.Select(t => t.EffectiveKey).ToList());
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private List<TestInfo> DiscoverTestsForProject(IProject project)
    {
        var result = new List<TestInfo>();
        var projectPath = project.FileName?.ToString();
        if (string.IsNullOrEmpty(projectPath))
            return result;

        var targetFrameworks = ResolveTargetFrameworks(project, projectPath);
        foreach (var targetFramework in targetFrameworks)
        {
            try
            {
                var tests = _runner.ListTestsAsync(projectPath, targetFramework, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                foreach (var test in tests)
                {
                    result.Add(new TestInfo(
                        test.DisplayName,
                        test.DisplayName,
                        project.Name,
                        projectPath,
                        targetFramework,
                        TestInfo.BuildKey(projectPath, targetFramework, test.DisplayName),
                        test.Uid,
                        test.TypeFullName,
                        test.MethodName,
                        test.ParameterCount));
                }
            }
            catch (Exception ex)
            {
                Dbg($"DiscoverTests failed for {project.Name} ({targetFramework ?? "(default target)"}): {ex.Message}");
            }
        }

        return result;
    }

    private async Task RunTestsInProjectAsync(
        string projectPath,
        string? targetFramework,
        IReadOnlyList<string> testUids,
        IReadOnlyDictionary<string, string> testKeysByName,
        CancellationToken ct)
    {
        Dbg($"Running tests in {projectPath}" + (string.IsNullOrWhiteSpace(targetFramework) ? string.Empty : $" ({targetFramework})"));
        PrepareOutputCategory(clear: false)?.AppendLine(
            $"Running {testKeysByName.Count} test(s) in {Path.GetFileName(projectPath)}"
            + (string.IsNullOrWhiteSpace(targetFramework) ? "..." : $" [{targetFramework}]..."));

        // Mark all requested tests as running (keyed by display name - testKeysByName enumerates
        // exactly the tests this group is about to run).
        foreach (var fqn in testKeysByName.Keys)
        {
            var key = testKeysByName[fqn];
            TestResultUpdated?.Invoke(new TestResultInfo(fqn, TestResultType.Running, null, null, targetFramework, key));
        }

        try
        {
            var results = await _runner.RunTestsAsync(projectPath, targetFramework, testUids, ct);
            foreach (var result in results)
            {
                var key = testKeysByName.TryGetValue(result.FullyQualifiedName, out var mapped)
                    ? mapped
                    : TestInfo.BuildKey(projectPath, targetFramework, result.FullyQualifiedName);
                var enriched = result with { TargetFramework = targetFramework, TestKey = key };
                lock (_lock)
                    _lastResults[enriched.EffectiveKey] = enriched;
                TestResultUpdated?.Invoke(enriched);
            }
        }
        catch (OperationCanceledException)
        {
            Dbg("MTP run canceled");
        }
        catch (Exception ex)
        {
            Dbg($"MTP run failed: {ex.Message}");
            PrepareOutputCategory(clear: false)?.AppendLine("ERROR: test run failed: " + ex.Message);
            foreach (var fqn in testKeysByName.Keys)
            {
                var key = testKeysByName[fqn];
                TestResultUpdated?.Invoke(new TestResultInfo(fqn, TestResultType.Failing, ex.Message, null, targetFramework, key));
            }
        }
    }

    private static IReadOnlyList<string?> ResolveTargetFrameworks(IProject project, string projectPath)
    {
        if (project is MSBuildBasedProject msbuildProject)
        {
            try
            {
                var frameworks = SplitTargetFrameworks(msbuildProject.GetEvaluatedProperty("TargetFrameworks"));
                if (frameworks.Count > 0)
                    return frameworks.Cast<string?>().ToArray();

                var targetFramework = msbuildProject.GetEvaluatedProperty("TargetFramework");
                if (!string.IsNullOrWhiteSpace(targetFramework))
                    return [targetFramework.Trim()];
            }
            catch
            {
                // Fallback to project-file scan below.
            }
        }

        return ResolveTargetFrameworksFromProjectFile(projectPath);
    }

    private static IReadOnlyList<string?> ResolveTargetFrameworksFromProjectFile(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath, LoadOptions.None);
            var frameworks = new List<string>();

            foreach (var targetFrameworks in document.Descendants().Where(element => element.Name.LocalName == "TargetFrameworks"))
                frameworks.AddRange(SplitTargetFrameworks(targetFrameworks.Value));

            if (frameworks.Count > 0)
                return frameworks.Cast<string?>().Distinct(StringComparer.Ordinal).ToArray();

            var targetFramework = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "TargetFramework")
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetFramework))
                return [targetFramework.Trim()];
        }
        catch
        {
            // Best-effort parser; ignore malformed/unreadable project files.
        }

        return [null];
    }

    private static IReadOnlyList<string> SplitTargetFrameworks(string? value)
        => (value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void Dbg(string msg)
    {
        try { File.AppendAllText(_dbgLog, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    private static IOutputCategory? PrepareOutputCategory(bool clear)
    {
        var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(IOutputPad)) as IOutputPad;
        if (outputPad is null) return null;
        var category = outputPad.GetOrCreateCategory("Tests");
        if (category is null)
            return null;

        if (clear)
            category.Clear();
        outputPad.CurrentCategory = category;
        return category;
    }

    private static void AppendOutputLine(string line)
    {
        PrepareOutputCategory(clear: false)?.AppendLine(line);
    }
}
