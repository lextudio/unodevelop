using System;
using System.CodeDom.Compiler;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project;
using Mono.TextTemplating;

namespace UnoDevelop.TextTemplating;

public class TextTemplatingFilePreprocessor
{
    public void Preprocess(FileProjectItem file, IProject project)
    {
        // ProjectFileTemplatingHost (via Mono.TextTemplating.TemplateGenerator) isn't
        // IDisposable — no AppDomain/process to tear down, it runs fully in-process.
        var host = new ProjectFileTemplatingHost(file, project);

        string outputFile = null;
        Preprocess(host, file, ref outputFile);
        TextTemplatingService.ShowTemplateHostErrors(host.Errors);
    }

    static void Preprocess(TemplateGenerator host, FileProjectItem file, ref string outputFile)
    {
        string content;
        var inputPath = file.FileName.ToString();
        try
        {
            content = File.ReadAllText(inputPath);
        }
        catch (IOException ex)
        {
            host.Errors.Add(new CompilerError
            {
                ErrorText = "Could not read input file '" + inputPath + "':\n" + ex.Message
            });
            return;
        }

        var pt = ParsedTemplate.FromText(content, host);
        if (pt.Errors.HasErrors)
        {
            host.Errors.AddRange(pt.Errors);
            return;
        }

        var settings = TemplatingEngine.GetSettings(host, pt);
        if (pt.Errors.HasErrors)
        {
            host.Errors.AddRange(pt.Errors);
            return;
        }

        outputFile = Path.ChangeExtension(inputPath, settings.Provider.FileExtension);
        settings.Name = settings.Provider.CreateValidIdentifier(Path.GetFileNameWithoutExtension(inputPath));
        settings.Namespace = GetFileNamespace(file);
        settings.IncludePreprocessingHelpers = string.IsNullOrEmpty(settings.Inherits);
        settings.IsPreprocessed = true;

        var ccu = TemplatingEngine.GenerateCompileUnit(host, content, pt, settings);
        host.Errors.AddRange(pt.Errors);
        if (pt.Errors.HasErrors)
            return;

        try
        {
            using var writer = new StreamWriter(outputFile, false, System.Text.Encoding.UTF8);
            settings.Provider.GenerateCodeFromCompileUnit(ccu, writer, new CodeGeneratorOptions());
        }
        catch (IOException ex)
        {
            host.Errors.Add(new CompilerError
            {
                ErrorText = "Could not write output file '" + outputFile + "':\n" + ex.Message
            });
        }
    }

    static string GetFileNamespace(FileProjectItem file)
    {
        var ns = file.GetEvaluatedMetadata("CustomToolNamespace");
        if (!string.IsNullOrEmpty(ns))
            return ns;

        var project = file.Project;
        var rootNs = project?.RootNamespace;
        if (string.IsNullOrEmpty(rootNs))
            return "T4Template";

        var relative = FileUtility.GetRelativePath(
            project.Directory.ToString(),
            file.FileName.ToString());
        var dir = Path.GetDirectoryName(relative);
        if (string.IsNullOrEmpty(dir))
            return rootNs;

        return rootNs + "." + dir.Replace(Path.DirectorySeparatorChar, '.');
    }
}
