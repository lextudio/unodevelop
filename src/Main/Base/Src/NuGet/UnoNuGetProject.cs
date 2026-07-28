using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using ICSharpCode.SharpDevelop.Project;

namespace ICSharpCode.SharpDevelop.NuGet
{
    /// <summary>
    /// A real <see cref="NuGetProject"/> backed by this codebase's own project model
    /// (<see cref="IProject"/>) rather than MonoDevelop's — see externals/OpenDevelop/doc/technotes/nuget-manager.md slice 1.
    /// Constructed from an already-extracted, testable snapshot of installed packages (same
    /// pattern as <c>LanguageServiceProjectSnapshot</c>) rather than taking a live
    /// <see cref="IProject"/> directly, so it can be unit tested without a full MSBuild-evaluated
    /// project.
    /// </summary>
    public sealed class UnoNuGetProject : NuGetProject
    {
        readonly string _projectFileName;
        readonly List<PackageReference> _installedPackages;

        public UnoNuGetProject(string projectFileName, NuGetFramework targetFramework, IReadOnlyList<PackageReference> installedPackages)
        {
            if (projectFileName is null)
                throw new ArgumentNullException(nameof(projectFileName));
            if (installedPackages is null)
                throw new ArgumentNullException(nameof(installedPackages));

            var uniqueName = Path.GetFileNameWithoutExtension(projectFileName);
            _projectFileName = projectFileName;
            InternalMetadata[NuGetProjectMetadataKeys.Name] = uniqueName;
            InternalMetadata[NuGetProjectMetadataKeys.UniqueName] = uniqueName;
            InternalMetadata[NuGetProjectMetadataKeys.FullPath] = Path.GetDirectoryName(projectFileName) ?? string.Empty;
            InternalMetadata[NuGetProjectMetadataKeys.TargetFramework] = targetFramework;
            _installedPackages = installedPackages.ToList();
        }

        public override Task<IEnumerable<PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IEnumerable<PackageReference>>(_installedPackages);
        }

        public override Task<bool> InstallPackageAsync(
            PackageIdentity packageIdentity, DownloadResourceResult downloadResourceResult, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            if (packageIdentity is null)
                throw new ArgumentNullException(nameof(packageIdentity));

            token.ThrowIfCancellationRequested();

            var editor = new SdkStylePackageReferenceEditor(_projectFileName);
            var changed = editor.AddOrUpdate(packageIdentity.Id, packageIdentity.Version);
            if (changed) {
                _installedPackages.RemoveAll(package =>
                    string.Equals(package.PackageIdentity.Id, packageIdentity.Id, StringComparison.OrdinalIgnoreCase));
                _installedPackages.Add(new PackageReference(packageIdentity, GetMetadata<NuGetFramework>(NuGetProjectMetadataKeys.TargetFramework)));
            }

            return Task.FromResult(changed);
        }

        public override Task<bool> UninstallPackageAsync(
            PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            if (packageIdentity is null)
                throw new ArgumentNullException(nameof(packageIdentity));

            token.ThrowIfCancellationRequested();

            var editor = new SdkStylePackageReferenceEditor(_projectFileName);
            var changed = editor.Remove(packageIdentity.Id);
            if (changed) {
                _installedPackages.RemoveAll(package =>
                    string.Equals(package.PackageIdentity.Id, packageIdentity.Id, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult(changed);
        }

        /// <summary>
        /// Reads a project's already-evaluated <c>PackageReference</c> items — the same data
        /// this codebase's dependency bridge (project-system.md) extracts for the Solution
        /// Explorer tree — rather than re-evaluating MSBuild.
        /// </summary>
        public static UnoNuGetProject FromProject(IProject project)
        {
            if (project is null)
                throw new ArgumentNullException(nameof(project));

            var projectFileName = project.FileName.ToString();
            var msbuildProject = project as MSBuildBasedProject;
            var targetFrameworkMoniker = msbuildProject?.GetEvaluatedProperty("TargetFramework");
            var targetFramework = string.IsNullOrWhiteSpace(targetFrameworkMoniker)
                ? NuGetFramework.AnyFramework
                : NuGetFramework.Parse(targetFrameworkMoniker);

            var installedPackages = project.GetItemsOfType(ItemType.PackageReference)
                .Select(item => ToPackageReference(item, targetFramework))
                .Where(reference => reference is not null)
                .Select(reference => reference!)
                .ToArray();

            return new UnoNuGetProject(projectFileName, targetFramework, installedPackages);
        }

        static PackageReference? ToPackageReference(ProjectItem item, NuGetFramework targetFramework)
        {
            var id = item.Include;
            var versionString = item.GetEvaluatedMetadata("Version");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(versionString)
                || !VersionRange.TryParse(versionString, out var versionRange) || versionRange.MinVersion is null)
            {
                return null;
            }

            var identity = new PackageIdentity(id, versionRange.MinVersion);
            return new PackageReference(identity, targetFramework);
        }
    }
}
