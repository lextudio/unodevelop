using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.LanguageServices.Lsp;
using NUnit.Framework;

#nullable enable

namespace UnoDevelop.Core.Tests
{
    public sealed class LspLanguageServiceTests
    {
        [Test]
        public void CreateDefault_MapsTypeScriptAndJavaScriptExtensionsToSameCommand()
        {
            var registry = LspServerRegistry.CreateDefault();

            Assert.That(registry.TryGetLaunchSpec(".ts", out var ts), Is.True);
            Assert.That(registry.TryGetLaunchSpec(".tsx", out var tsx), Is.True);
            Assert.That(registry.TryGetLaunchSpec(".js", out var js), Is.True);
            Assert.That(registry.TryGetLaunchSpec(".jsx", out var jsx), Is.True);
            Assert.That(ts.Command, Is.EqualTo("typescript-language-server"));
            Assert.That(tsx.Command, Is.EqualTo(ts.Command));
            Assert.That(js.Command, Is.EqualTo(ts.Command));
            Assert.That(jsx.Command, Is.EqualTo(ts.Command));
        }

        [Test]
        public void CreateDefault_HasNoMappingForUnrelatedExtension()
        {
            var registry = LspServerRegistry.CreateDefault();

            Assert.That(registry.TryGetLaunchSpec(".rs", out _), Is.False);
        }

        [Test]
        public async Task GetCompletionsAsync_ReturnsEmpty_WhenServerCommandIsMissing()
        {
            // No process named this ever exists on PATH; the service must fall back to empty
            // results rather than throwing, matching the "no regression" fallback design
            // (docs/language-services.md §3.2).
            var spec = new LspServerLaunchSpec("nonexistent", "unodevelop-lsp-server-that-does-not-exist");
            var service = new LspLanguageService(spec, "file:///tmp");

            await service.UpsertDocumentAsync(new DocumentId("/tmp/a.ts"), "const x = 1;", CancellationToken.None);
            var completions = await service.GetCompletionsAsync(new DocumentId("/tmp/a.ts"), 0, CancellationToken.None);
            var diagnostics = await service.GetDiagnosticsAsync(new DocumentId("/tmp/a.ts"), CancellationToken.None);

            Assert.That(completions.Items, Is.Empty);
            Assert.That(diagnostics, Is.Empty);

            await service.DisposeAsync();
        }

        [Test]
        public async Task QuickInfoAndGoToDefinition_ReturnEmpty_WhenServerCommandIsMissing()
        {
            var spec = new LspServerLaunchSpec("nonexistent", "unodevelop-lsp-server-that-does-not-exist");
            var service = new LspLanguageService(spec, "file:///tmp");
            var documentId = new DocumentId("/tmp/a.ts");

            Assert.That(await service.GetQuickInfoAsync(documentId, 0, CancellationToken.None), Is.Null);
            Assert.That(await service.GoToDefinitionAsync(documentId, 0, CancellationToken.None), Is.Empty);

            await service.DisposeAsync();
        }

        [Test]
        public async Task FormatAsync_ReturnsEmpty_WhenServerCommandIsMissing()
        {
            var spec = new LspServerLaunchSpec("nonexistent", "unodevelop-lsp-server-that-does-not-exist");
            var service = new LspLanguageService(spec, "file:///tmp");

            Assert.That(await service.FormatAsync(new DocumentId("/tmp/a.ts"), null, CancellationToken.None), Is.Empty);

            await service.DisposeAsync();
        }

        [Test]
        public void CreateDefault_MapsPythonToPylsp()
        {
            var registry = LspServerRegistry.CreateDefault();

            Assert.That(registry.TryGetLaunchSpec(".py", out var python), Is.True);
            Assert.That(python.Command, Is.EqualTo("pylsp"));
        }

        [Test]
        public void CreateDefault_MapsFSharpToRepositoryTool()
        {
            var registry = LspServerRegistry.CreateDefault();

            Assert.That(registry.TryGetLaunchSpec(".fs", out var implementation), Is.True);
            Assert.That(registry.TryGetLaunchSpec(".fsi", out var signature), Is.True);
            Assert.That(implementation.Command, Is.EqualTo("dotnet"));
            Assert.That(implementation.Arguments, Is.EqualTo(new[] { "tool", "run", "fsautocomplete", "--" }));
            Assert.That(signature, Is.SameAs(implementation));
        }

        [Test]
        public async Task GetCodeActionsAsync_ReturnsEmpty_WhenServerCommandIsMissing()
        {
            var spec = new LspServerLaunchSpec("nonexistent", "unodevelop-lsp-server-that-does-not-exist");
            var service = new LspLanguageService(spec, "file:///tmp");
            var span = new TextSpan(new TextPosition(1, 1), new TextPosition(1, 1));

            Assert.That(await service.GetCodeActionsAsync(new DocumentId("/tmp/a.ts"), span, CancellationToken.None), Is.Empty);

            await service.DisposeAsync();
        }

        [Test]
        public async Task ApplyCodeActionAsync_ReturnsEmpty_ForUnknownActionId()
        {
            // No preceding GetCodeActionsAsync call ever populated this id - must not throw,
            // same "quietly do nothing" posture as an unknown/stale id after the server *did*
            // respond (docs/language-services.md §8.1).
            var spec = new LspServerLaunchSpec("nonexistent", "unodevelop-lsp-server-that-does-not-exist");
            var service = new LspLanguageService(spec, "file:///tmp");

            var edits = await service.ApplyCodeActionAsync(new DocumentId("/tmp/a.ts"), "0", CancellationToken.None);

            Assert.That(edits, Is.Empty);
            await service.DisposeAsync();
        }
    }
}
