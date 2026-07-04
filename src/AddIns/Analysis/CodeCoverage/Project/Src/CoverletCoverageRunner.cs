using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.CodeCoverage;
using ICSharpCode.SharpDevelop.Project;

namespace UnoDevelop.AddIns.Analysis.CodeCoverage;

internal sealed class CoverletCoverageRunner
{
    public async Task<CodeCoverageSession> RunAsync(IReadOnlyList<IProject> projects, CancellationToken cancellationToken)
    {
        var log = new List<string>();
        var resultFiles = new List<string>();
        var coverletDll = GetCoverletDll();
        log.Add("Coverlet: " + coverletDll);

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = project.FileName?.ToString();
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
            {
                log.Add("Skipping project without a project file: " + project.Name);
                continue;
            }

            log.Add("Building " + project.Name);
            var build = await RunProcessAsync("dotnet", new[] { "build", projectPath, "-tl:off" }, Path.GetDirectoryName(projectPath), cancellationToken);
            log.AddRange(build.OutputLines);
            if (build.ExitCode != 0)
            {
                log.Add("Build failed for " + project.Name);
                continue;
            }

            var outputAssembly = GetOutputAssembly(project);
            if (string.IsNullOrEmpty(outputAssembly) || !File.Exists(outputAssembly))
            {
                log.Add("Could not locate output assembly for " + project.Name);
                continue;
            }

            var outputDirectory = Path.GetDirectoryName(outputAssembly)!;
            var coverageRoot = Path.Combine(Path.GetTempPath(), "UnoDevelopCoverage", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(coverageRoot);
            // Coverlet appends the format extension itself, so pass the report path without it.
            var reportBase = Path.Combine(coverageRoot, project.Name);
            var reportFile = reportBase + ".opencover.xml";

            log.Add("Running MTP test assembly with Coverlet: " + outputAssembly);
            var run = await RunProcessAsync(
                "dotnet",
                new[]
                {
                    coverletDll,
                    outputDirectory,
                    "--target", "dotnet",
                    "--targetargs", "exec \"" + outputAssembly + "\"",
                    "--output", reportBase,
                    "--format", "opencover",
                    "--exclude-by-attribute", "Xunit.FactAttribute",
                    "--exclude-by-attribute", "Xunit.TheoryAttribute",
                    "--exclude-by-attribute", "NUnit.Framework.TestAttribute",
                    "--exclude-by-attribute", "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
                    "--exclude-by-attribute", "TUnit.Core.TestAttribute",
                    "--exclude", "[xunit.*]*",
                    "--exclude", "[nunit.*]*",
                    "--exclude", "[Microsoft.TestPlatform.*]*",
                    "--exclude", "[Microsoft.VisualStudio.TestPlatform.*]*",
                    "--exclude", "[Microsoft.Testing.*]*",
                    "--exclude", "[testhost*]*"
                },
                outputDirectory,
                cancellationToken);
            log.AddRange(run.OutputLines);
            if (run.ExitCode != 0)
                log.Add("MTP coverage run failed for " + project.Name + " with exit code " + run.ExitCode);

            if (File.Exists(reportFile))
                resultFiles.Add(reportFile);
            else
                log.Add("Coverage report was not created: " + reportFile);
        }

        var reader = new CodeCoverageResultsReader();
        foreach (var file in resultFiles)
            reader.AddResultsFile(file);

        var results = reader.GetResults().ToList();
        log.AddRange(reader.GetMissingResultsFiles().Select(item => "Missing coverage report: " + item));
        return new CodeCoverageSession("Coverlet MTP coverage", results, log);
    }

    private static string GetCoverletDll()
    {
        var baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        var coverletRoot = Path.Combine(baseDirectory, "Coverlet");
        var fileName = Directory.Exists(coverletRoot)
            ? Directory.EnumerateFiles(coverletRoot, "coverlet.console.dll", SearchOption.AllDirectories).FirstOrDefault()
            : null;

        if (fileName is not null)
            return fileName;

        throw new FileNotFoundException("coverlet.console.dll was not found. Ensure the CodeCoverage addin copied the coverlet.console tool files.", Path.Combine(coverletRoot, "coverlet.console.dll"));
    }

    private static string? GetOutputAssembly(IProject project)
    {
        var output = project.OutputAssemblyFullPath;
        if (!string.IsNullOrEmpty(output) && File.Exists(output))
            return output;

        var projectPath = project.FileName?.ToString();
        if (string.IsNullOrEmpty(projectPath))
            return null;

        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDirectory))
            return null;

        return Directory.EnumerateFiles(Path.Combine(projectDirectory, "bin"), projectName + ".dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var lines = new List<string> { "> " + FormatCommandLine(fileName, arguments) };
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        lock (lines)
            return new ProcessResult(process.ExitCode, lines.ToArray());
    }

    private static string FormatCommandLine(string fileName, IEnumerable<string> arguments)
        => string.Join(" ", new[] { fileName }.Concat(arguments).Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string value)
        => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private sealed record ProcessResult(int ExitCode, IReadOnlyList<string> OutputLines);
}
