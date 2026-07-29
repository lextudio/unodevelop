using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.LanguageServices.Lsp;
using NUnit.Framework;

#nullable enable

namespace UnoDevelop.Core.Tests
{
    /// <summary>
    /// End-to-end coverage for the real <c>wpf-xaml-ls</c> process this repo builds
    /// (externals/OpenDevelop/externals/vscode-wpf/src/XamlLanguageServer.Wpf).
    ///
    /// <para>
    /// This does NOT go through <see cref="LspServerRegistry.CreateDefault"/> - its root-detection
    /// (<c>FindOpenDevelopRoot</c>) assumes "externals/vscode-wpf" and "src/Main/Base" are direct
    /// siblings, true when that Base-layer code is compiled and run as OpenDevelop itself, but not
    /// when compiled into UnoDevelop: OpenDevelop is nested one level deeper here
    /// (externals/OpenDevelop/externals/vscode-wpf), so walking up from a UnoDevelop.Core.Tests
    /// process never finds a directory satisfying both conditions. This was discovered while
    /// adding this test, not fixed here (UnoDevelop's own ServiceBootstrapper overwrites the
    /// ".xaml" spec with its own Uno host regardless, so it's currently harmless in practice for
    /// UnoDevelop - but CreateDefault()'s WPF spec would silently resolve to nothing useful if this
    /// Base-layer code were ever exercised, unwrapped, from UnoDevelop's own process). Flagged in
    /// docs/opendevelop-sync.md; a real fix belongs to whoever owns that shared root-detection
    /// logic, not to this Uno-focused verification pass.
    /// </para>
    /// </summary>
    [Category("Integration")]
    public sealed class WpfLanguageServerIntegrationTests
    {
        [Test]
        [Timeout(60000)]
        public async Task WpfLanguageServer_StartsAndAnswersCompletionRequest_WithoutCrashing()
        {
            var dllPath = FindWpfLanguageServerDll();
            Assert.That(dllPath, Is.Not.Null.And.Matches(".+"),
                "wpf-xaml-ls.dll not found under XamlLanguageServer.Wpf/bin - build it first: dotnet build " +
                "externals/OpenDevelop/externals/vscode-wpf/src/XamlLanguageServer.Wpf/XamlLanguageServer.Wpf.csproj " +
                "(this also requires the nested XamlToCSharpGenerator submodule to be checked out on a branch " +
                "with the WPF-specific model types wxsg needs, e.g. 'wpf', not 'tiered-completion' - see " +
                "docs/opendevelop-sync.md's XamlBinding entry on the branch fragmentation).");

            var repositoryRoot = Path.GetFullPath(Path.Combine(dllPath!, "..", "..", "..", "..", "..", ".."));
            var spec = new LspServerLaunchSpec(
                "xaml",
                "dotnet",
                repositoryRoot,
                "exec",
                dllPath!,
                "--workspace",
                repositoryRoot);

            var service = new LspLanguageService(spec, "file:///tmp");
            try
            {
                var documentId = new DocumentId("/tmp/MainWindow.xaml");
                const string xaml = "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";

                await service.UpsertDocumentAsync(documentId, xaml, CancellationToken.None);

                // No project/prewarm means Tier 2 alone answers this - it should come back
                // empty rather than hang or throw, proving the process is alive and speaking
                // real LSP frames, not that it has type-aware completions available yet.
                var completions = await service.GetCompletionsAsync(documentId, xaml.Length, CancellationToken.None);
                Assert.That(completions, Is.Not.Null);

                var quickInfo = await service.GetQuickInfoAsync(documentId, 1, CancellationToken.None);
                Assert.That(quickInfo, Is.Null.Or.Not.Null); // exercised without throwing/hanging is the assertion
            }
            finally
            {
                await service.DisposeAsync();
            }
        }

        private static string? FindWpfLanguageServerDll()
        {
            foreach (var candidate in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var directory = string.IsNullOrEmpty(candidate) ? null : new DirectoryInfo(candidate);
                while (directory != null)
                {
                    var binRoot = Path.Combine(
                        directory.FullName, "externals", "OpenDevelop", "externals", "vscode-wpf",
                        "src", "XamlLanguageServer.Wpf", "bin");
                    if (Directory.Exists(binRoot))
                    {
                        var dll = new[] { "Release", "Debug" }
                            .Select(configuration => Path.Combine(binRoot, configuration))
                            .Where(Directory.Exists)
                            .SelectMany(configurationDirectory => Directory.GetFiles(configurationDirectory, "wpf-xaml-ls.dll", SearchOption.AllDirectories))
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .FirstOrDefault();
                        if (dll != null)
                            return dll;
                    }
                    directory = directory.Parent;
                }
            }
            return null;
        }
    }
}
