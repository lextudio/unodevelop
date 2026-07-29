using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlLanguageServer.Uno.Workspace;

/// <summary>
/// Uno-specific factory for the Tier-1 (fast) compilation snapshot used by
/// <see cref="TieredCompilationProvider"/>.
///
/// <para>
/// Unlike WPF's <c>Microsoft.WindowsDesktop.App.Ref</c> - a single, fixed
/// reference-assembly package that is the same for every WPF project - Uno/WinUI
/// has no equivalent: the actual assembly set (Uno.WinUI version, Skia/WebAssembly/
/// mobile runtime backend, Toolkit, fonts, third-party controls, ...) is
/// per-project. There is no well-known package this class could point at
/// regardless of which project is open.
/// </para>
///
/// <para>
/// Instead, this reads the target project's own <c>obj/project.assets.json</c> -
/// the output of a NuGet *restore*, not a full build. Every entry under
/// <c>targets/&lt;tfm&gt;</c> lists a <c>compile</c> section: the exact relative
/// paths (under one of <c>packageFolders</c>) of the assemblies that would be
/// referenced at compile time. Resolving those directly gives a real, correct-
/// for-this-project reference set without evaluating MSBuild or running a full
/// build - only a prior restore, which normal editor/IDE project-open flows
/// (and this LSP host's own eventual Tier-2 prewarm) already trigger.
/// </para>
///
/// <para>
/// This is deliberately independent of the target project's actual source: it
/// only needs the restore output, so it stays valid even while the user's own
/// code doesn't compile - exactly the "instant" property Tier 1 exists for.
/// </para>
/// </summary>
public static class UnoFastCompilationProvider
{
    /// <summary>
    /// Locates <c>obj/project.assets.json</c> for the first project file found under
    /// <paramref name="workspaceRoot"/> (via <see cref="TieredCompilationProvider.FindFirstProjectFile"/>),
    /// or <see langword="null"/> if no project file exists, or it has never been restored.
    /// </summary>
    public static string? FindProjectAssetsFile(string workspaceRoot)
    {
        var projectFile = TieredCompilationProvider.FindFirstProjectFile(workspaceRoot);
        if (projectFile is null)
        {
            return null;
        }

        var assetsFile = Path.Combine(Path.GetDirectoryName(projectFile) ?? workspaceRoot, "obj", "project.assets.json");
        return File.Exists(assetsFile) ? assetsFile : null;
    }

    /// <summary>
    /// Builds the Tier-1 snapshot by resolving every "compile" reference listed in
    /// <paramref name="projectAssetsFilePath"/> for its (first/only) target framework.
    /// Returns <see langword="null"/> if the file is missing, malformed, or resolves
    /// no usable assemblies.
    /// </summary>
    public static CompilationSnapshot? BuildFastSnapshot(string projectAssetsFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectAssetsFilePath) || !File.Exists(projectAssetsFilePath))
            {
                return null;
            }

            using var stream = File.OpenRead(projectAssetsFilePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (!root.TryGetProperty("packageFolders", out var packageFoldersElement))
            {
                return null;
            }

            var packageFolders = packageFoldersElement
                .EnumerateObject()
                .Select(static p => p.Name)
                .Where(Directory.Exists)
                .ToArray();
            if (packageFolders.Length == 0)
            {
                return null;
            }

            if (!root.TryGetProperty("targets", out var targetsElement))
            {
                return null;
            }

            // A restored project normally has exactly one target (its single TFM); if
            // more than one somehow exists, take the first rather than guessing which
            // one the caller wants - Tier 1 only needs *a* usable reference set.
            var targetProperty = targetsElement.EnumerateObject().FirstOrDefault();
            if (targetProperty.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var referencePaths = new List<string>();
            foreach (var libraryProperty in targetProperty.Value.EnumerateObject())
            {
                // Library keys look like "Uno.WinUI/6.5.153".
                var separatorIndex = libraryProperty.Name.LastIndexOf('/');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var packageId = libraryProperty.Name[..separatorIndex];
                var packageVersion = libraryProperty.Name[(separatorIndex + 1)..];

                if (!libraryProperty.Value.TryGetProperty("compile", out var compileElement) ||
                    compileElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var compileEntry in compileElement.EnumerateObject())
                {
                    // NuGet emits a placeholder entry ("_._") for packages that
                    // contribute no compile-time assembly - not a real file.
                    if (!compileEntry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var resolved = ResolvePackageAssetPath(packageFolders, packageId, packageVersion, compileEntry.Name);
                    if (resolved is not null)
                    {
                        referencePaths.Add(resolved);
                    }
                }
            }

            if (referencePaths.Count == 0)
            {
                return null;
            }

            var references = new List<MetadataReference>(referencePaths.Count);
            foreach (var path in referencePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch
                {
                    // Skip unreadable/invalid metadata files; the rest of the
                    // reference set is still useful.
                }
            }

            if (references.Count == 0)
            {
                return null;
            }

            var compilation = CSharpCompilation.Create(
                assemblyName: "UnoTier1",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                references: references);

            return new CompilationSnapshot(
                ProjectPath: null,
                Project: null,
                Compilation: compilation,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UNO-LS] Failed to build Uno Tier-1 fast snapshot: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Convenience helper combining discovery and snapshot construction.
    /// Returns <see langword="null"/> if the project has never been restored.
    /// </summary>
    public static CompilationSnapshot? TryBuildFastSnapshot(string workspaceRoot)
    {
        var assetsFile = FindProjectAssetsFile(workspaceRoot);
        return assetsFile is null ? null : BuildFastSnapshot(assetsFile);
    }

    private static string? ResolvePackageAssetPath(
        IReadOnlyList<string> packageFolders,
        string packageId,
        string packageVersion,
        string relativeAssetPath)
    {
        // NuGet's global-packages layout always lower-cases the package id segment,
        // regardless of the casing used in project.assets.json's library keys.
        var packageIdLower = packageId.ToLowerInvariant();
        foreach (var folder in packageFolders)
        {
            var candidate = Path.Combine(folder, packageIdLower, packageVersion, relativeAssetPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
