using System.IO;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.VisualStudio.TextTemplating;

namespace UnoDevelop.TextTemplating;

public class TextTemplatingFileGenerator
{
    public void Generate(FileProjectItem file, IProject project)
    {
        var host = new ProjectFileTemplatingHost(file, project);
        var defaultOutputName = Path.ChangeExtension(file.FileName.ToString(), ".cs");

        var ns = host.GetFileNamespace(defaultOutputName);
        if (ns is not null)
        {
            // Session is an explicit interface implementation on TemplateGenerator
            // (ITextTemplatingSessionHost), not a plain property.
            var sessionHost = (ITextTemplatingSessionHost)host;
            sessionHost.Session ??= sessionHost.CreateSession();
            sessionHost.Session["NamespaceHint"] = ns;
        }

        host.ProcessTemplate(file.FileName.ToString(), defaultOutputName);

        TextTemplatingService.ShowTemplateHostErrors(host.Errors);
    }
}
