using System.Linq;
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
        public void InstallPackageAsync_ThrowsNotSupported_SliceOneIsReadOnly()
        {
            var targetFramework = NuGetFramework.Parse("net10.0");
            var project = new UnoNuGetProject(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Test.csproj"), targetFramework, System.Array.Empty<NuGet.Packaging.PackageReference>());

            Assert.ThrowsAsync<System.NotSupportedException>(() =>
                project.InstallPackageAsync(
                    new PackageIdentity("Newtonsoft.Json", NuGetVersion.Parse("13.0.3")),
                    null!,
                    null!,
                    CancellationToken.None));
        }
    }
}
