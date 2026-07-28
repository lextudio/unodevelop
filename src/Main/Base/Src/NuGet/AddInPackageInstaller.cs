using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace ICSharpCode.SharpDevelop.NuGet;

/// <summary>
/// Downloads and extracts a NuGet package as an AddIn - the install/uninstall mechanics
/// OpenDevelop's AddInManager2 provides via its own ~90-file NuGet-based manager
/// (Project/Src/Model/NuGetPackageManager.cs et al). Reuses NuGetPackageSearchService (search)
/// and NuGetPackageSourceCatalog (feed resolution) already built for externals/OpenDevelop/doc/technotes/nuget-manager.md's
/// project-reference NuGet manager; this is the AddIn-specific counterpart - "install" here means
/// extract the package's .addin manifest(s) + assemblies into a folder and register via the real
/// upstream AddInManager (AddExternalAddIns/RemoveExternalAddIns), not add a project reference.
/// </summary>
public static class AddInPackageInstaller
{
    public sealed record InstallResult(bool Success, string? InstallDirectory, IReadOnlyList<string> AddInFiles, string? Error);

    /// <summary>
    /// Downloads <paramref name="packageId"/>/<paramref name="version"/> from the first source in
    /// <paramref name="sources"/> that has it, and extracts its non-metadata payload (skipping
    /// NuGet's own package plumbing: [Content_Types].xml, _rels/, package/, *.nuspec) into a new
    /// folder under <paramref name="userAddInPath"/>. Fails if the package contains no .addin
    /// manifest - this is specifically for AddIn packages, not arbitrary NuGet packages.
    /// </summary>
    public static async Task<InstallResult> InstallAsync(
        string packageId, string version, IReadOnlyList<PackageSource> sources, string userAddInPath, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
            return new InstallResult(false, null, Array.Empty<string>(), $"Invalid version '{version}'");

        var identity = new PackageIdentity(packageId, nugetVersion);

        foreach (var source in sources)
        {
            DownloadResourceResult? result;
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var downloadResource = await repository.GetResourceAsync<DownloadResource>(cancellationToken);
                if (downloadResource is null)
                    continue;

                using var cacheContext = new SourceCacheContext();
                var context = new PackageDownloadContext(cacheContext);
                result = await downloadResource.GetDownloadResourceResultAsync(
                    identity, context, GetTempDownloadFolder(), NullLogger.Instance, cancellationToken);
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"AddIn package download from '{source.Name}' failed: {ex.Message}");
                continue;
            }

            if (result is null || result.Status != DownloadResourceResultStatus.Available)
            {
                result?.Dispose();
                continue;
            }

            using (result)
            {
                return await ExtractAsync(packageId, nugetVersion, result, userAddInPath, cancellationToken);
            }
        }

        return new InstallResult(false, null, Array.Empty<string>(), $"Package '{packageId}' {version} not found on any configured source");
    }

    static async Task<InstallResult> ExtractAsync(string packageId, NuGetVersion version, DownloadResourceResult result, string userAddInPath, CancellationToken cancellationToken)
    {
        var targetDir = Path.Combine(userAddInPath, $"{packageId}.{version.ToNormalizedString()}");
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, recursive: true);
        Directory.CreateDirectory(targetDir);

        var addInFiles = new List<string>();
        var reader = result.PackageReader;
        foreach (var file in reader.GetFiles())
        {
            if (IsPackageMetadataFile(file))
                continue;

            var destination = Path.Combine(targetDir, file.Replace('/', Path.DirectorySeparatorChar));
            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDir))
                Directory.CreateDirectory(destinationDir);

            using var sourceStream = await reader.GetStreamAsync(file, cancellationToken);
            using var destStream = File.Create(destination);
            await sourceStream.CopyToAsync(destStream, cancellationToken);

            if (file.EndsWith(".addin", StringComparison.OrdinalIgnoreCase))
                addInFiles.Add(destination);
        }

        if (addInFiles.Count == 0)
        {
            Directory.Delete(targetDir, recursive: true);
            return new InstallResult(false, null, Array.Empty<string>(), $"Package '{packageId}' contains no .addin manifest");
        }

        return new InstallResult(true, targetDir, addInFiles, null);
    }

    static bool IsPackageMetadataFile(string file)
        => file.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("package/", StringComparison.OrdinalIgnoreCase)
            || file.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes an installed AddIn's extracted folder. Unregistering from AddInManager is the caller's responsibility.</summary>
    public static void Uninstall(string installDirectory)
    {
        if (Directory.Exists(installDirectory))
            Directory.Delete(installDirectory, recursive: true);
    }

    static string GetTempDownloadFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "UnoDevelopAddInDownloads");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
