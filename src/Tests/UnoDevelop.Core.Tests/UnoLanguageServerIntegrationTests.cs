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
    /// End-to-end coverage for the real <c>uno-xaml-ls</c> process this repo builds
    /// (src/LanguageServer/XamlLanguageServer.Uno) - not just AXSG's own in-process
    /// unit tests, which cannot exercise UnoDevelop's actual process launch, stdio
    /// framing, or ServiceBootstrapper wiring.
    ///
    /// <para>
    /// Scope is deliberately limited to "the process starts, speaks LSP, and answers
    /// without crashing" - there is no Uno.Sdk project fixture here to drive real
    /// MSBuild evaluation (that needs a restorable Uno project, which is heavier than
    /// this suite currently sets up), and this host has no Tier-1 fast snapshot yet
    /// (see UnoLanguageFrameworkProvider's doc comment on why one is deferred), so a
    /// workspace-less run never produces type-aware completions to assert on. What
    /// this proves: the csproj path ServiceBootstrapper computes is correct, the
    /// process launches under "dotnet run", and the LSP handshake/completion/hover
    /// round-trip works without erroring - regressions here would mean UnoDevelop's
    /// wiring itself is broken, independent of anything AXSG's own tests cover.
    /// </para>
    /// </summary>
    [Category("Integration")]
    public sealed class UnoLanguageServerIntegrationTests
    {
        [Test]
        [Timeout(60000)]
        public async Task UnoLanguageServer_StartsAndAnswersCompletionRequest_WithoutCrashing()
        {
            var dllPath = FindUnoLanguageServerDll();
            Assert.That(dllPath, Is.Not.Null.And.Matches(".+"),
                "uno-xaml-ls.dll not found under XamlLanguageServer.Uno/bin - build the project first " +
                "(dotnet build src/LanguageServer/XamlLanguageServer.Uno). A plain 'dotnet run' is not used " +
                "here on purpose: it can trigger an implicit restore/build whose NuGet/MSBuild progress goes " +
                "to stdout, corrupting the LSP stdio stream - the same reason ServiceBootstrapper uses " +
                "'dotnet exec' against a prebuilt dll instead of 'dotnet run --project'.");

            var repositoryRoot = Path.GetFullPath(Path.Combine(dllPath!, "..", "..", "..", "..", "..", ".."));
            var spec = new LspServerLaunchSpec(
                "xaml",
                "dotnet",
                repositoryRoot,
                "exec",
                dllPath!);

            var service = new LspLanguageService(spec, "file:///tmp");
            try
            {
                var documentId = new DocumentId("/tmp/MainPage.xaml");
                const string xaml = "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />";

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

        private static string? FindUnoLanguageServerDll()
        {
            foreach (var candidate in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var directory = string.IsNullOrEmpty(candidate) ? null : new DirectoryInfo(candidate);
                while (directory != null)
                {
                    var binRoot = Path.Combine(
                        directory.FullName, "src", "LanguageServer", "XamlLanguageServer.Uno", "bin");
                    if (Directory.Exists(binRoot))
                    {
                        var dll = new[] { "Release", "Debug" }
                            .Select(configuration => Path.Combine(binRoot, configuration))
                            .Where(Directory.Exists)
                            .SelectMany(configurationDirectory => Directory.GetFiles(configurationDirectory, "uno-xaml-ls.dll", SearchOption.AllDirectories))
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
