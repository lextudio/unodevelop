using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.CodeCoverage;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.AddIns.Analysis.CodeCoverage;

public sealed class CodeCoverageService
{
    private const string OutputCategoryName = "Code Coverage";
    private readonly CoverletCoverageRunner _runner = new();
    private CancellationTokenSource? _runCancellation;

    public static CodeCoverageService Instance { get; } = new();

    public CodeCoverageSession CurrentSession { get; private set; } = CodeCoverageSession.Empty;
    public bool IsRunning { get; private set; }
    public event EventHandler? SessionChanged;

    public async Task RunAllTestsWithCoverageAsync()
    {
        if (IsRunning)
            return;

        var projects = GetOpenTestProjects();
        if (projects.Count == 0)
        {
            var lines = new[] { "No Microsoft.Testing.Platform test projects were found in the open solution." };
            SetSession(new CodeCoverageSession("No test projects found", Array.Empty<CodeCoverageResults>(), lines));
            AppendOutputLines(lines, clear: true, activateCategory: true);
            return;
        }

        IsRunning = true;
        _runCancellation = new CancellationTokenSource();
        var startLines = new[] { $"Running coverage for {projects.Count} test project(s)." };
        SetSession(new CodeCoverageSession("Running coverage...", Array.Empty<CodeCoverageResults>(), startLines));
        AppendOutputLines(startLines, clear: true, activateCategory: true);

        try
        {
            var session = await _runner.RunAsync(projects, _runCancellation.Token);
            SetSession(session);
            AppendOutputLines(session.LogLines, clear: false, activateCategory: true);
        }
        catch (OperationCanceledException)
        {
            var lines = new[] { "Coverage run canceled." };
            SetSession(new CodeCoverageSession("Coverage run canceled", Array.Empty<CodeCoverageResults>(), lines));
            AppendOutputLines(lines, clear: false, activateCategory: true);
        }
        catch (Exception ex)
        {
            var lines = new[] { "Coverage run failed.", ex.ToString() };
            SetSession(new CodeCoverageSession("Coverage run failed", Array.Empty<CodeCoverageResults>(), lines));
            AppendOutputLines(lines, clear: false, activateCategory: true);
        }
        finally
        {
            _runCancellation = null;
            IsRunning = false;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Stop()
    {
        _runCancellation?.Cancel();
    }

    public void LoadCoverageFile(string fileName)
    {
        var reader = new CodeCoverageResultsReader();
        reader.AddResultsFile(fileName);
        var results = reader.GetResults().ToList();
        var log = new List<string> { "Loaded " + fileName };
        log.AddRange(reader.GetMissingResultsFiles().Select(item => "Missing: " + item));
        SetSession(new CodeCoverageSession(Path.GetFileName(fileName), results, log));
        AppendOutputLines(log, clear: true, activateCategory: true);
    }

    private static IOutputCategory? PrepareOutputCategory(bool clear, bool activateCategory)
    {
        var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(IOutputPad)) as IOutputPad;
        if (outputPad is null)
            return null;

        var category = outputPad.GetOrCreateCategory(OutputCategoryName);
        if (clear)
            category.Clear();

        if (activateCategory)
            outputPad.CurrentCategory = category;

        return category;
    }

    private static void AppendOutputLines(IEnumerable<string> lines, bool clear, bool activateCategory)
    {
        var category = PrepareOutputCategory(clear, activateCategory);
        if (category is null)
            return;

        foreach (var line in lines)
            category.AppendLine(line);
    }

    private static IReadOnlyList<IProject> GetOpenTestProjects()
    {
        var projects = SD.ProjectService.CurrentSolution?.Projects;
        if (projects is null)
            return Array.Empty<IProject>();

        return projects.Where(IsMtpTestProject).ToList();
    }

    private static bool IsMtpTestProject(IProject project)
    {
        if (project is MSBuildBasedProject msbuildProject)
        {
            if (IsTrue(msbuildProject.GetEvaluatedProperty("IsTestingPlatformApplication")))
                return true;
            if (IsTrue(msbuildProject.GetEvaluatedProperty("IsTestProject")))
                return true;
        }

        var fileName = project.FileName?.ToString();
        if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            return false;

        var text = File.ReadAllText(fileName);
        return text.Contains("Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
            || text.Contains("xunit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("NUnit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MSTest", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TUnit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrue(string? value)
        => bool.TryParse(value, out var result) && result;

    private void SetSession(CodeCoverageSession session)
    {
        CurrentSession = session;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
