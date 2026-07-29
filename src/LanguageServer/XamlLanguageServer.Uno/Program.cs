using System;
using System.Threading;
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

// Tier 1 is intentionally absent for now (fastSnapshot: null): unlike WPF's
// Microsoft.WindowsDesktop.App.Ref, Uno/WinUI has no single reference-assembly
// NuGet package that resolves without a prior build (see
// UnoLanguageFrameworkProvider's doc comment on why a dedicated Uno Tier-1
// provider is deferred). TieredCompilationProvider still prewarms the full
// compilation eagerly at startup rather than lazily on first keystroke, so
// requests before prewarm completes fall straight through to Tier 2.
var tieredProvider = new TieredCompilationProvider(
    fullProvider: new MsBuildCompilationProvider(),
    fastSnapshot: null);

using var engine = new XamlLanguageServiceEngine(tieredProvider, frameworkRegistry);
using var server = new AxsgLanguageServer(
    new LspMessageReader(Console.OpenStandardInput()),
    new LspMessageWriter(Console.OpenStandardOutput()),
    engine,
    options);

// No fast snapshot means every request already goes to Tier 2 (see
// TieredCompilationProvider.GetCompilationAsync), so there is nothing for
// open documents to have cached from a Tier 1 that never ran - unlike WPF/
// Avalonia's hosts, this callback has no stale-cache gap to close. It still
// exists so a future Uno Tier-1 provider can be added here without touching
// anything else.
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
