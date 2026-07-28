using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.NuGet;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NUnit.Framework;

namespace UnoDevelop.Core.Tests
{
    public sealed class UnoNuGetProjectTests
    {
        [Test]
        public async Task GetInstalledPackagesAsync_ReturnsPackagesPassedToConstructor()
        {
            var targetFramework = NuGetFramework.Parse("net10.0");
            var packages = new[]
            {
                new NuGet.Packaging.PackageReference(new PackageIdentity("Newtonsoft.Json", NuGetVersion.Parse("13.0.3")), targetFramework),
                new NuGet.Packaging.PackageReference(new PackageIdentity("NUnit", NuGetVersion.Parse("3.14.0")), targetFramework)
            };

            var project = new UnoNuGetProject(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Test.csproj"), targetFramework, packages);

            var installed = (await project.GetInstalledPackagesAsync(CancellationToken.None)).ToArray();

            Assert.That(installed.Select(p => p.PackageIdentity.Id), Is.EquivalentTo(new[] { "Newtonsoft.Json", "NUnit" }));
        }

        [Test]
        public void Metadata_ExposesProjectNameAndTargetFramework()
        {
            var targetFramework = NuGetFramework.Parse("net10.0");
            var project = new UnoNuGetProject(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Test.csproj"), targetFramework, System.Array.Empty<NuGet.Packaging.PackageReference>());

            Assert.That(project.GetMetadata<string>(NuGet.ProjectManagement.NuGetProjectMetadataKeys.Name), Is.EqualTo("Test"));
            Assert.That(project.GetMetadata<NuGetFramework>(NuGet.ProjectManagement.NuGetProjectMetadataKeys.TargetFramework), Is.EqualTo(targetFramework));
        }

        [Test]
        public async Task InstallPackageAsync_AddsPackageReference()
        {
            var targetFramework = NuGetFramework.Parse("net10.0");
            var projectFileName = CreateEmptyProjectFile();
            var project = new UnoNuGetProject(projectFileName, targetFramework, System.Array.Empty<NuGet.Packaging.PackageReference>());

            var changed = await project.InstallPackageAsync(
                    new PackageIdentity("Newtonsoft.Json", NuGetVersion.Parse("13.0.3")),
                    null!,
                    null!,
                    CancellationToken.None);

            Assert.That(changed, Is.True);
            var projectText = File.ReadAllText(projectFileName);
            Assert.That(projectText, Does.Contain("PackageReference"));
            Assert.That(projectText, Does.Contain("Newtonsoft.Json"));
            Assert.That(projectText, Does.Contain("13.0.3"));
        }

        [Test]
        public async Task InstallPackageAsync_UpdatesExistingPackageReference()
        {
            var targetFramework = NuGetFramework.Parse("net10.0");
            var projectFileName = CreateProjectFile("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
                  </ItemGroup>
                </Project>
                """);
            var project = new UnoNuGetProject(projectFileName, targetFramework, System.Array.Empty<NuGet.Packaging.PackageReference>());

            var changed = await project.InstallPackageAsync(
                new PackageIdentity("Newtonsoft.Json", NuGetVersion.Parse("13.0.3")),
                null!,
                null!,
                CancellationToken.None);

            Assert.That(changed, Is.True);
            var projectText = File.ReadAllText(projectFileName);
            Assert.That(projectText, Does.Contain("13.0.3"));
            Assert.That(projectText, Does.Not.Contain("12.0.1"));
            Assert.That(projectText.Split("PackageReference").Length - 1, Is.EqualTo(1));
        }

        [Test]
        public async Task UninstallPackageAsync_RemovesPackageReference()
        {
            var targetFramework = NuGetFramework.Parse("net10.0");
            var projectFileName = CreateProjectFile("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                </Project>
                """);
            var project = new UnoNuGetProject(projectFileName, targetFramework, new[]
            {
                new NuGet.Packaging.PackageReference(new PackageIdentity("Newtonsoft.Json", NuGetVersion.Parse("13.0.3")), targetFramework)
            });

            var changed = await project.UninstallPackageAsync(
                new PackageIdentity("Newtonsoft.Json", NuGetVersion.Parse("13.0.3")),
                null!,
                CancellationToken.None);

            Assert.That(changed, Is.True);
            var projectText = File.ReadAllText(projectFileName);
            Assert.That(projectText, Does.Not.Contain("Newtonsoft.Json"));
            var installed = (await project.GetInstalledPackagesAsync(CancellationToken.None)).ToArray();
            Assert.That(installed, Is.Empty);
        }

        [Test]
        public async Task PackageOperationService_AddsPackageReferenceAndRunsRestore()
        {
            var projectFileName = CreateEmptyProjectFile();
            var service = new NuGetProjectPackageOperationService((fileName, cancellationToken) =>
                Task.FromResult(new NuGetProjectPackageOperationService.RestoreResult(0, "restore ok: " + Path.GetFileName(fileName), string.Empty)));

            var result = await service.AddPackageReferenceAsync(
                projectFileName,
                "Newtonsoft.Json",
                NuGetVersion.Parse("13.0.3"),
                restore: true,
                CancellationToken.None);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.RestoreRequested, Is.True);
            Assert.That(result.RestoreSucceeded, Is.True);
            Assert.That(result.RestoreOutput, Does.Contain("restore ok"));
            Assert.That(File.ReadAllText(projectFileName), Does.Contain("Newtonsoft.Json"));
        }

        [Test]
        public async Task PackageOperationService_DoesNotRunRestoreWhenProjectDidNotChange()
        {
            var projectFileName = CreateProjectFile("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                </Project>
                """);
            var restoreCalls = 0;
            var service = new NuGetProjectPackageOperationService((fileName, cancellationToken) =>
            {
                restoreCalls++;
                return Task.FromResult(new NuGetProjectPackageOperationService.RestoreResult(0, string.Empty, string.Empty));
            });

            var result = await service.AddPackageReferenceAsync(
                projectFileName,
                "Newtonsoft.Json",
                NuGetVersion.Parse("13.0.3"),
                restore: true,
                CancellationToken.None);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.RestoreRequested, Is.False);
            Assert.That(restoreCalls, Is.Zero);
        }

        [Test]
        public void SdkStylePackageReferenceEditor_ReadsPackageReferencesFromProjectFile()
        {
            var projectFileName = CreateProjectFile("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                    <PackageReference Include="NUnit">
                      <Version>4.3.2</Version>
                    </PackageReference>
                  </ItemGroup>
                </Project>
                """);

            var packages = new SdkStylePackageReferenceEditor(projectFileName)
                .GetPackageReferences()
                .ToArray();

            Assert.That(packages.Select(package => package.Id), Is.EqualTo(new[] { "Newtonsoft.Json", "NUnit" }));
            Assert.That(packages.Select(package => package.Version), Is.EqualTo(new[] { "13.0.3", "4.3.2" }));
        }

        static string CreateEmptyProjectFile()
        {
            return CreateProjectFile("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
        }

        static string CreateProjectFile(string content)
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopNuGetTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            var projectFileName = Path.Combine(directory, "Test.csproj");
            File.WriteAllText(projectFileName, content);
            return projectFileName;
        }
    }
}
