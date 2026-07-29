using System;
using System.Threading;
using XamlLanguageServer.Uno.Workspace;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Framework;
using XamlToCSharpGenerator.LanguageService.Framework.Uno;
using XamlToCSharpGenerator.LanguageService.Workspace;
using XamlToCSharpGenerator.LanguageServer.Protocol;
using XamlToCSharpGenerator.LanguageServer.Server;

// Redirect trace output that might corrupt the LSP stdio stream.
Console.OutputEncoding = System.Text.Encoding.UTF8;

var workspaceRoot = ParseArg(args, "--workspace");
Console.Error.WriteLine($"[UNO-LS] Starting. workspaceRoot={workspaceRoot ?? "(null)"}");

// This host serves exactly one framework, so it registers only Uno's provider
// and names it explicitly via FrameworkId - no per-document detection ever
// runs, matching how XamlLanguageServer.Wpf mounts WpfFrameworkProfile.Instance
// directly. See IXamlLanguageFrameworkProvider's doc comment for why a
// dedicated host should not implement (or pay for) detection heuristics.
var frameworkRegistry = new XamlLanguageFrameworkRegistryBuilder()
    .Add(UnoLanguageFrameworkProvider.Instance)
    .Build(FrameworkProfileIds.Uno);
var options = new XamlLanguageServiceOptions(workspaceRoot, FrameworkId: FrameworkProfileIds.Uno);

// Tier 1: built from the target project's own obj/project.assets.json (a NuGet
// *restore* artifact, not a full build) rather than a single fixed framework
// package like WPF's Microsoft.WindowsDesktop.App.Ref - Uno/WinUI has no such
// package, since the real assembly set is per-project (Uno.WinUI version,
// runtime backend, Toolkit, fonts, ...). See UnoFastCompilationProvider's doc
// comment. Null (Tier 1 skipped, straight to Tier 2 once ready) if the project
// has never been restored, or no workspace root was given at all.
var fastSnapshot = workspaceRoot is not null
    ? UnoFastCompilationProvider.TryBuildFastSnapshot(workspaceRoot)
    : null;
if (fastSnapshot is not null)
{
    Console.Error.WriteLine("[UNO-LS] Tier-1 fast snapshot built from project.assets.json.");
}
else
{
    Console.Error.WriteLine("[UNO-LS] No Tier-1 fast snapshot available (project not yet restored, or no workspace) - starting at Tier 2 only.");
}

var tieredProvider = new TieredCompilationProvider(
    fullProvider: new MsBuildCompilationProvider(),
    fastSnapshot: fastSnapshot);

using var engine = new XamlLanguageServiceEngine(tieredProvider, frameworkRegistry);
using var server = new AxsgLanguageServer(
    new LspMessageReader(Console.OpenStandardInput()),
    new LspMessageWriter(Console.OpenStandardOutput()),
    engine,
    options);

// When Tier 1 was available, a request for a document that hasn't changed
// version since a Tier-1-served analysis would otherwise keep returning that
// stale result even after Tier 2 becomes ready (the analysis cache is keyed on
// document version, not tier) - see TieredCompilationProvider's own doc
// comment. Invalidating on prewarm completion closes that gap, matching
// XamlLanguageServer.Wpf's identical wiring.
tieredProvider.OnPrewarmCompleted = () =>
{
    engine.InvalidateAllOpenDocumentCaches();
};

if (workspaceRoot is not null)
{
    var projectFile = TieredCompilationProvider.FindFirstProjectFile(workspaceRoot);
    if (projectFile is not null)
    {
        Console.Error.WriteLine($"[UNO-LS] Starting background prewarm for {projectFile}");
        _ = tieredProvider.PrewarmAsync(projectFile, workspaceRoot);
    }
    else
    {
        Console.Error.WriteLine("[UNO-LS] No supported project file (.csproj/.vbproj/.fsproj) found in workspace - prewarm skipped.");
    }
}

var exitCode = await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
Environment.ExitCode = exitCode;

static string? ParseArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.Ordinal))
            return args[i + 1];
    }
    return null;
}
