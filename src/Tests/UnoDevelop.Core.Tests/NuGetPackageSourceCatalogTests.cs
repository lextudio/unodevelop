using System;
using System.IO;
using System.Linq;
using ICSharpCode.SharpDevelop.NuGet;
using NUnit.Framework;

namespace UnoDevelop.Core.Tests
{
    public sealed class NuGetPackageSourceCatalogTests
    {
        [Test]
        public void LoadEnabledSources_ReadsSourcesFromNuGetConfigInDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopNuGetTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "nuget.config"), """
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <packageSources>
                        <clear />
                        <add key="TestFeed" value="https://example.test/v3/index.json" />
                        <add key="DisabledFeed" value="https://example.test/disabled/index.json" />
                      </packageSources>
                      <disabledPackageSources>
                        <add key="DisabledFeed" value="true" />
                      </disabledPackageSources>
                    </configuration>
                    """);

                var sources = NuGetPackageSourceCatalog.LoadEnabledSources(directory);

                Assert.That(sources.Select(s => s.Name), Is.EquivalentTo(new[] { "TestFeed" }));
                Assert.That(sources.Single().SourceUri.ToString(), Is.EqualTo("https://example.test/v3/index.json"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void LoadEnabledSources_ThrowsForNullOrEmptyDirectory()
        {
            Assert.Throws<ArgumentException>(() => NuGetPackageSourceCatalog.LoadEnabledSources(""));
        }
    }
}
