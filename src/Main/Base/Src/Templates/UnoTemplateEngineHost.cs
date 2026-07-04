using System.Collections.Generic;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;

namespace ICSharpCode.SharpDevelop.Templates
{
    /// <summary>
    /// Identifies UnoDevelop to Microsoft.TemplateEngine (docs/template-system.md §1) — the same
    /// engine `dotnet new`/modern Visual Studio use for file and project templates, not
    /// SharpDevelop's or MonoDevelop's own proprietary template formats.
    /// </summary>
    public static class UnoTemplateEngineHost
    {
        public const string HostIdentifier = "unodevelop";

        public static ITemplateEngineHost Create()
        {
            return new DefaultTemplateEngineHost(
                hostIdentifier: HostIdentifier,
                version: "1.0.0",
                defaults: new Dictionary<string, string>());
        }
    }
}
