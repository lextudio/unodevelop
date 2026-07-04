using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Templates;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using NUnit.Framework;

namespace UnoDevelop.Core.Tests
{
    public sealed class TemplateInstantiationTests
    {
        /// <summary>
        /// Instantiates a fixture template and verifies the generated file exists with
        /// correct name-substitution applied (docs/template-system.md slice 2).
        /// </summary>
        [Test]
        public async Task InstantiateAsync_WithFixtureTemplate_GeneratesOutputFile()
        {
            var (service, fixtureDir, outputDir) = CreateServiceWithFixture();
            using (service)
            {
                var templates = await service.GetInstalledTemplatesAsync(CancellationToken.None);
                var fixtureTemplate = templates.FirstOrDefault(t => t.Identity == "UnoDevelop.TestFixture");
                Assert.That(fixtureTemplate, Is.Not.Null, "Fixture template should be discoverable after install");

                const string className = "MyGeneratedClass";
                var result = await service.InstantiateAsync(
                    fixtureTemplate,
                    className,
                    outputDir,
                    parameters: new Dictionary<string, string> { ["ClassName"] = className },
                    CancellationToken.None);

                Assert.That(result.Success, Is.True, result.ErrorMessage ?? "Instantiation failed");

                var generatedFile = Path.Combine(outputDir, className + ".cs");
                Assert.That(generatedFile, Does.Exist);

                var content = File.ReadAllText(generatedFile);
                Assert.That(content, Does.Contain(className));
                Assert.That(content, Does.Not.Contain("ReplaceMe"));
            }

            CleanupDirectories(fixtureDir, outputDir);
        }

        /// <summary>
        /// Dry-runs the fixture template and returns the expected outputs without generating
        /// any files on disk.
        /// </summary>
        [Test]
        public async Task GetCreationEffectsAsync_WithFixtureTemplate_ReturnsExpectedOutputs()
        {
            var (service, fixtureDir, outputDir) = CreateServiceWithFixture();
            using (service)
            {
                var templates = await service.GetInstalledTemplatesAsync(CancellationToken.None);
                var fixtureTemplate = templates.FirstOrDefault(t => t.Identity == "UnoDevelop.TestFixture");
                Assert.That(fixtureTemplate, Is.Not.Null);

                const string className = "DryRunClass";
                var result = await service.GetCreationEffectsAsync(
                    fixtureTemplate,
                    className,
                    outputDir,
                    parameters: new Dictionary<string, string> { ["ClassName"] = className },
                    CancellationToken.None);

                Assert.That(result.Success, Is.True, result.ErrorMessage ?? "Dry-run failed");
                Assert.That(result.PrimaryOutputPaths, Is.Not.Empty);
                Assert.That(result.PrimaryOutputPaths.First(), Does.EndWith(className + ".cs"));

                // No files should actually exist on disk after a dry run
                Assert.That(Directory.Exists(outputDir), Is.False);
            }

            CleanupDirectories(fixtureDir, outputDir);
        }

        static (TemplateDiscoveryService Service, string FixtureDir, string OutputDir) CreateServiceWithFixture()
        {
            var hostId = "unodevelop-test-" + Guid.NewGuid().ToString("N");
            var host = new DefaultTemplateEngineHost(
                hostIdentifier: hostId,
                version: "1.0.0",
                defaults: new Dictionary<string, string>());

            var fixtureDir = Path.Combine(Path.GetTempPath(), "UnoDevelop", "Templates", "Fixture-" + Guid.NewGuid().ToString("N"));
            var outputDir = Path.Combine(Path.GetTempPath(), "UnoDevelop", "Output", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(fixtureDir, ".template.config"));
            File.WriteAllText(Path.Combine(fixtureDir, ".template.config", "template.json"), /* language=json */ @"{
  ""$schema"": ""http://json.schemastore.org/template"",
  ""author"": ""UnoDevelop"",
  ""classifications"": [""Test""],
  ""name"": ""UnoDevelop Test Fixture"",
  ""identity"": ""UnoDevelop.TestFixture"",
  ""shortName"": ""unodevelop-test"",
  ""tags"": { ""language"": ""text"", ""type"": ""item"" },
  ""sourceName"": ""ReplaceMe"",
  ""primaryOutputs"": [
    { ""path"": ""ReplaceMe.cs"" }
  ],
  ""symbols"": {
    ""ClassName"": {
      ""type"": ""parameter"",
      ""replaces"": ""ReplaceMe"",
      ""defaultValue"": ""ReplaceMe""
    }
  }
}");
            File.WriteAllText(Path.Combine(fixtureDir, "ReplaceMe.cs"), @"// Auto-generated by ReplaceMe
class ReplaceMe { }");

            var service = new TemplateDiscoveryService(host);
            var installOk = service.InstallTemplatePackageAsync(fixtureDir, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(installOk, Is.True, "Fixture template package should install successfully");

            return (service, fixtureDir, outputDir);
        }

        static void CleanupDirectories(string fixtureDir, string outputDir)
        {
            if (Directory.Exists(fixtureDir))
                Directory.Delete(fixtureDir, recursive: true);
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
