using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Templates;
using NUnit.Framework;

namespace UnoDevelop.Core.Tests
{
    public sealed class TemplateDiscoveryServiceTests
    {
        [Test]
        public async Task GetInstalledTemplatesAsync_FindsBuiltInDotNetSdkTemplates()
        {
            // Real discovery against whatever .NET SDK templates are installed on this machine
            // (same precedent as NuGetPackageSearchServiceTests hitting real nuget.org) — proves
            // the Bootstrapper wiring actually works, not just that it compiles.
            using var service = new TemplateDiscoveryService();

            var templates = await service.GetInstalledTemplatesAsync(CancellationToken.None);

            // Which templates are installed varies by machine (this one has MAUI/Avalonia/Uno/etc.
            // template packages, not necessarily the base SDK's "classlib"/"console" — those ship
            // from the SDK install location, which Bootstrapper's default settings don't scan; a
            // refinement for a later slice, see docs/template-system.md). What's guaranteed and
            // worth asserting here is that discovery itself works and returns well-formed, sorted
            // results — proving the engine wiring, not a specific machine's template inventory.
            Assert.That(templates, Is.Not.Empty);
            Assert.That(templates, Is.All.TypeOf<TemplateSummary>());
            Assert.That(templates, Has.All.Matches<TemplateSummary>(t => !string.IsNullOrEmpty(t.ShortName) && !string.IsNullOrEmpty(t.Name)));
            Assert.That(templates, Is.Ordered.By(nameof(TemplateSummary.Name)));
        }
    }
}
