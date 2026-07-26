using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.NuGet
{
    /// <summary>
    /// Ties together <see cref="NuGetPackageSearchService"/>/<see cref="NuGetPackageSourceCatalog"/>
    /// (browsing) and <see cref="AddInPackageInstaller"/> (download+extract) with the real upstream
    /// <see cref="AddInManager"/> (registration) - the AddIn-installation counterpart of
    /// OpenDevelop's AddInManager2. A NuGet-packaged AddIn is just a package whose payload includes
    /// one or more .addin manifests; everything else (search, source resolution, extraction) is
    /// reused verbatim from the existing project-reference NuGet manager slices.
    /// </summary>
    public sealed class AddInPackageManagerService
    {
        readonly NuGetPackageSearchService _searchService;

        public AddInPackageManagerService(NuGetPackageSearchService? searchService = null)
        {
            _searchService = searchService ?? new NuGetPackageSearchService();
        }

        public Task<IReadOnlyList<NuGetSearchResult>> SearchAsync(
            string searchTerm, bool includePrerelease, int take, CancellationToken cancellationToken)
        {
            var sources = ResolveSources();
            return _searchService.SearchAsync(sources, searchTerm, includePrerelease, take, cancellationToken);
        }

        /// <summary>
        /// Configured NuGet sources for AddIn search/install, honoring
        /// <c>UNODEVELOP_ADDIN_NUGET_SOURCE</c> when set - an integration-test seam that points
        /// this at a local folder-based feed fixture instead of the machine's real configured
        /// feeds, keeping tests deterministic and network-independent while still exercising the
        /// real NuGet.Protocol search/download code against a real (just local) package source.
        /// </summary>
        static IReadOnlyList<global::NuGet.Configuration.PackageSource> ResolveSources()
        {
            var overrideSource = Environment.GetEnvironmentVariable("UNODEVELOP_ADDIN_NUGET_SOURCE");
            if (!string.IsNullOrEmpty(overrideSource))
                return new[] { new global::NuGet.Configuration.PackageSource(overrideSource, "TestOverride") };

            return NuGetPackageSourceCatalog.LoadEnabledSources(AddInManager.UserAddInPath ?? AppContext.BaseDirectory);
        }

        /// <summary>
        /// Downloads, extracts, and registers <paramref name="packageId"/>/<paramref name="version"/>
        /// as an external AddIn. Newly installed AddIns start disabled per
        /// <see cref="AddInManager.AddExternalAddIns"/>'s own semantics; enabled immediately here
        /// for consistency with AddInScout's immediate-feedback toggle, requiring only the same
        /// eventual restart that any newly loaded assembly would.
        /// </summary>
        public async Task<AddInPackageInstaller.InstallResult> InstallAsync(
            string packageId, string version, CancellationToken cancellationToken)
        {
            var sources = ResolveSources();
            var result = await AddInPackageInstaller.InstallAsync(packageId, version, sources, AddInManager.UserAddInPath, cancellationToken);
            if (!result.Success)
                return result;

            var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
            var loaded = result.AddInFiles
                .Select(file => AddIn.Load(addInTree, file))
                .ToList();

            AddInManager.AddExternalAddIns(loaded);
            foreach (var addIn in loaded)
                addIn.Enabled = true;

            var disabled = addInTree.AddIns.Where(candidate => !candidate.Enabled)
                .Select(candidate => candidate.Manifest?.PrimaryIdentity)
                .Where(identity => !string.IsNullOrEmpty(identity))
                .Select(identity => identity!)
                .ToList();
            var addInFiles = new List<string>();
            var loadedDisabled = new List<string>();
            AddInManager.LoadAddInConfiguration(addInFiles, loadedDisabled);
            AddInManager.SaveAddInConfiguration(addInFiles, disabled);

            return result;
        }

        /// <summary>
        /// Unregisters and deletes a previously package-installed AddIn. Only external AddIns
        /// under <see cref="AddInManager.UserAddInPath"/> can be uninstalled - preinstalled AddIns
        /// (shipped with the app) can only be disabled, matching upstream semantics.
        /// </summary>
        public bool Uninstall(string identityOrName)
        {
            var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
            var addIn = addInTree.AddIns.FirstOrDefault(candidate =>
                string.Equals(candidate.Manifest?.PrimaryIdentity, identityOrName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, identityOrName, StringComparison.OrdinalIgnoreCase));
            if (addIn is null || addIn.IsPreinstalled)
                return false;

            var installDirectory = Path.GetDirectoryName(addIn.FileName);

            // RemoveExternalAddIns only rips a live AddIn out of AddInTree immediately when it's
            // already disabled (upstream semantics: uninstalling an enabled/loaded AddIn normally
            // only takes effect after a restart). Disable it first so uninstall is visible in this
            // running session too, matching AddInScout's immediate-feedback enable/disable toggle.
            addIn.Enabled = false;
            AddInManager.RemoveExternalAddIns(new List<AddIn> { addIn });

            if (!string.IsNullOrEmpty(installDirectory) && Directory.Exists(installDirectory)
                && FileUtility.IsBaseDirectory(AddInManager.UserAddInPath, installDirectory))
            {
                AddInPackageInstaller.Uninstall(installDirectory);
            }

            return true;
        }
    }
}
