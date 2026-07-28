using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.TextTemplating;

public sealed class GenerateT4Command : AbstractMenuCommand
{
    public override void Run()
    {
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        var project = projectService.CurrentProject;

        if (project is null)
            return;

        foreach (var file in GetT4Files(project).ToList())
        {
            T4TemplateRunner.RunIfApplicable(file, project);
        }
    }

    static IEnumerable<FileProjectItem> GetT4Files(IProject project)
    {
        foreach (var item in project.Items.CreateSnapshot())
        {
            if (item is FileProjectItem file)
            {
                var path = file.FileName.ToString();
                if (path.EndsWith(".tt", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".t4", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }
}

/// <summary>
/// Shared per-file dispatch, used by both the project-wide "Process T4 Templates..." command
/// above and the per-file Solution Explorer "Run Custom Tool" command
/// (<see cref="UnoDevelop.Commands.RunT4CustomToolSolutionExplorerCommand"/>). Goes through the
/// real <see cref="CustomToolsService"/> (externals/OpenDevelop/doc/technotes/t4-templating.md) — <c>FileProjectItem.CustomTool</c>
/// reads/writes the same <c>Generator</c> MSBuild metadata this used to check by hand.
/// </summary>
public static class T4TemplateRunner
{
    public static bool IsT4File(string fileName) =>
        fileName.EndsWith(".tt", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".t4", StringComparison.OrdinalIgnoreCase);

    public static void RunIfApplicable(FileProjectItem file, IProject project)
    {
        var customToolName = file.CustomTool;
        if (string.IsNullOrEmpty(customToolName) && IsT4File(file.FileName.ToString()))
        {
            // No explicit Generator/CustomTool metadata — .tt files default to the file
            // generator (matches MSBuild's own T4 targets' default behavior), without
            // persisting that default into the project file.
            customToolName = "TextTemplatingFileGenerator";
        }

        if (string.IsNullOrEmpty(customToolName))
            return;

        var customTool = CustomToolsService.GetCustomTool(customToolName);
        if (customTool is not null)
            CustomToolsService.RunCustomTool(file, customTool, showMessageBoxOnErrors: false);
    }
}

public sealed class TextTemplatingFileGeneratorCustomTool : ICustomTool
{
    public void GenerateCode(FileProjectItem item, CustomToolContext context) =>
        new TextTemplatingFileGenerator().Generate(item, context.Project);
}

public sealed class TextTemplatingFilePreprocessorCustomTool : ICustomTool
{
    public void GenerateCode(FileProjectItem item, CustomToolContext context) =>
        new TextTemplatingFilePreprocessor().Preprocess(item, context.Project);
}
