using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UnoDevelop.AddIns;

internal sealed class VsixFileCategories
{
    public List<string> GrammarFiles { get; init; } = new();
    public List<string> ThemeFiles { get; init; } = new();
    public List<string> SnippetFiles { get; init; } = new();
    public List<string> LanguageConfigFiles { get; init; } = new();
    public List<string> ServerFiles { get; init; } = new();

    public int FileCount =>
        GrammarFiles.Count + ThemeFiles.Count + SnippetFiles.Count +
        LanguageConfigFiles.Count + ServerFiles.Count;
}

internal sealed class VsixPackageInfo
{
    public required string Id { get; init; }
    public string Version { get; init; } = "0.0.0";
    public string Publisher { get; init; } = "Unknown";
    public string DisplayName { get; init; } = "";
    public ExtensionPackageKind PackageKind { get; init; }
    public VsixFileCategories Files { get; init; } = new();
    public List<string> AllFiles { get; init; } = new();

    public string KindLabel => PackageKind switch
    {
        ExtensionPackageKind.VSCode => "VS Code",
        ExtensionPackageKind.VisualStudio => "Visual Studio",
        _ => "Unknown"
    };

    public int GrammarCount => Files.GrammarFiles.Count;
    public int ThemeCount => Files.ThemeFiles.Count;
    public int ServerCount => Files.ServerFiles.Count;
}

internal static partial class VsixPackageParser
{
    private const string ManifestV1Ns = "http://schemas.microsoft.com/developer/vsx-schema/2011";
    private const string ManifestV2Ns = "http://schemas.microsoft.com/developer/vsx-schema/2013";

    private const string VsCodeManifestAsset = "Microsoft.VisualStudio.Code.Manifest";
    private const string DefaultVsCodeManifestPath = "extension/package.json";
    private const string ContentTypesFileName = "[Content_Types].xml";

    private const string GrammarAssetType = "Microsoft.VisualStudio.TextMate.Grammar";
    private const string ThemeAssetType = "Microsoft.VisualStudio.TextMate.Theme";
    private const string LspAssemblyAssetType = "Microsoft.VisualStudio.LanguageServer.Protocol.Assembly";
    private const string LspNodeAssetType = "Microsoft.VisualStudio.LanguageServer.Protocol.Node";

    // Grammar file extensions recognized by TextMate/VS Code
    private static readonly HashSet<string> GrammarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmlanguage",    // XML plist (.tmLanguage)
        ".tmlanguage.json", // JSON plist
        ".tmgrammar",     // Alternate name
        ".tmgrammar.json",
    };

    // Directories inside VS Code VSIX that commonly hold server binaries
    private static readonly string[] VsCodeServerDirs =
        ["server/", "languageServer/", "language-server/", "servers/", ".server/"];

    public static VsixPackageInfo Parse(string vsixPath)
    {
        using var zip = ZipFile.OpenRead(vsixPath);
        var manifestEntry = FindManifestEntry(zip);

        // VS Code detection: has extension/package.json (declared in vsixmanifest or at default path)
        var vsCodeManifestPath = FindVsCodeManifestPath(zip, manifestEntry);
        if (vsCodeManifestPath is not null)
            return ParseVsCode(zip, vsCodeManifestPath, manifestEntry);

        // Visual Studio detection: has extension.vsixmanifest (and no VS Code manifest asset)
        if (manifestEntry is not null)
            return ParseVisualStudio(zip, manifestEntry);

        // Fallback: scan for grammar/theme files by known extensions
        return ParseUnknown(zip);
    }

    /// <summary>
    /// Find the vsixmanifest entry. In VS for Windows it is always
    /// "extension.vsixmanifest". In VS Code VSIX there is typically
    /// "[Content_Types].xml" but no extension.vsixmanifest.
    /// </summary>
    private static ZipArchiveEntry? FindManifestEntry(ZipArchive zip)
    {
        return zip.Entries
            .FirstOrDefault(e =>
                string.Equals(e.Name, "extension.vsixmanifest", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.IndexOf('/', StringComparison.Ordinal) == -1);
    }

    /// <summary>
    /// Determine the VS Code package.json path.
    /// Priority:
    ///   1) extension.vsixmanifest → Microsoft.VisualStudio.Code.Manifest asset path
    ///   2) extension/package.json (well-known default)
    ///   3) Any entry ending with "/package.json" under an "extension/" prefix
    /// </summary>
    private static string? FindVsCodeManifestPath(ZipArchive zip, ZipArchiveEntry? manifestEntry)
    {
        if (manifestEntry is not null)
        {
            using var stream = manifestEntry.Open();
            var doc = XDocument.Load(stream);
            foreach (var ns in new[] { ManifestV1Ns, ManifestV2Ns })
            {
                var xns = XNamespace.Get(ns);
                var path = doc
                    .Descendants(xns + "Asset")
                    .FirstOrDefault(a =>
                        string.Equals(a.Attribute("Type")?.Value, VsCodeManifestAsset, StringComparison.OrdinalIgnoreCase))
                    ?.Attribute("Path")?.Value;

                if (path is not null)
                    return path.Replace('\\', '/');
            }
        }

        if (zip.GetEntry(DefaultVsCodeManifestPath) is not null)
            return DefaultVsCodeManifestPath;

        return zip.Entries
            .Where(e => !e.FullName.Contains('\\'))
            .Select(e => e.FullName)
            .FirstOrDefault(p =>
                p.StartsWith("extension/", StringComparison.OrdinalIgnoreCase) &&
                p.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase));
    }

    // ── VS Code parser ──────────────────────────────────────────────

    private static VsixPackageInfo ParseVsCode(ZipArchive zip, string manifestPath,
                                                ZipArchiveEntry? manifestEntry)
    {
        var entry = zip.GetEntry(manifestPath)
            ?? throw new InvalidOperationException($"VS Code manifest '{manifestPath}' not found");

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var publisher = root.TryGetProperty("publisher", out var p)
            ? p.GetString()
            : root.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object
                ? a.TryGetProperty("name", out var an) ? an.GetString() : null
                : null;

        var id = publisher is not null ? $"{publisher}.{name}" : name ?? "unknown";
        var displayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() : name;
        var version = root.TryGetProperty("version", out var v) ? v.GetString() : "0.0.0";
        var mainEntry = root.TryGetProperty("main", out var m) ? m.GetString() : null;

        // All entries under "extension/" (or root of the package if different)
        var packageDir = NormalizePackageDir(manifestPath);

        var categories = new VsixFileCategories();

        if (TryGetContributes(root, out var contributes))
        {
            CollectVsCodeGrammars(contributes, packageDir, categories);
            CollectVsCodeThemes(contributes, packageDir, categories);
            CollectVsCodeSnippets(contributes, packageDir, categories);
            CollectVsCodeLanguageConfigs(contributes, packageDir, categories);
        }

        // LSP server: check package.json "main" → scan server dirs
        CollectVsCodeServers(zip, packageDir, mainEntry, categories);

        var allFiles = zip.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToList();

        return new VsixPackageInfo
        {
            Id = id,
            Version = version,
            Publisher = publisher ?? "Unknown",
            DisplayName = displayName ?? name ?? id,
            PackageKind = ExtensionPackageKind.VSCode,
            Files = categories,
            AllFiles = allFiles
        };
    }

    private static void CollectVsCodeGrammars(JsonElement contributes,
                                               string packageDir,
                                               VsixFileCategories cats)
    {
        if (!contributes.TryGetProperty("grammars", out var grammars) ||
            grammars.ValueKind != JsonValueKind.Array)
            return;

        foreach (var g in grammars.EnumerateArray())
        {
            var path = g.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(path))
                cats.GrammarFiles.Add(ResolvePath(packageDir, path));
        }
    }

    private static void CollectVsCodeThemes(JsonElement contributes,
                                             string packageDir,
                                             VsixFileCategories cats)
    {
        if (!contributes.TryGetProperty("themes", out var themes) ||
            themes.ValueKind != JsonValueKind.Array)
            return;

        foreach (var t in themes.EnumerateArray())
        {
            var path = t.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(path))
                cats.ThemeFiles.Add(ResolvePath(packageDir, path));
        }
    }

    private static void CollectVsCodeSnippets(JsonElement contributes,
                                               string packageDir,
                                               VsixFileCategories cats)
    {
        if (!contributes.TryGetProperty("snippets", out var snippets) ||
            snippets.ValueKind != JsonValueKind.Array)
            return;

        foreach (var s in snippets.EnumerateArray())
        {
            var path = s.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(path))
                cats.SnippetFiles.Add(ResolvePath(packageDir, path));
        }
    }

    private static void CollectVsCodeLanguageConfigs(JsonElement contributes,
                                                      string packageDir,
                                                      VsixFileCategories cats)
    {
        if (!contributes.TryGetProperty("languages", out var languages) ||
            languages.ValueKind != JsonValueKind.Array)
            return;

        foreach (var lang in languages.EnumerateArray())
        {
            var path = lang.TryGetProperty("configuration", out var cfgEl) ? cfgEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(path))
                cats.LanguageConfigFiles.Add(ResolvePath(packageDir, path));
        }
    }

    private static void CollectVsCodeServers(ZipArchive zip,
                                              string packageDir,
                                              string? mainEntry,
                                              VsixFileCategories cats)
    {
        // Scan known server directories
        foreach (var dir in VsCodeServerDirs)
        {
            var fullPrefix = string.IsNullOrEmpty(packageDir)
                ? dir
                : $"{packageDir}/{dir}";

            var serverEntries = zip.Entries
                .Where(e => !e.FullName.EndsWith('/') &&
                             e.FullName.StartsWith(fullPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (serverEntries.Count > 0)
            {
                cats.ServerFiles.AddRange(serverEntries.Select(e => e.FullName));
                return; // found a server directory, stop
            }
        }

        // If no known server dir, check if main entry points to a server directory
        if (!string.IsNullOrWhiteSpace(mainEntry))
        {
            var mainDir = Path.GetDirectoryName(mainEntry)?.Replace('\\', '/') ?? "";
            var fullMainDir = string.IsNullOrEmpty(packageDir)
                ? mainDir
                : $"{packageDir}/{mainDir}";

            if (!string.IsNullOrWhiteSpace(fullMainDir))
            {
                // Check if there is a server subdirectory adjacent to main entry
                var adjacentServer = zip.Entries
                    .Where(e => !e.FullName.EndsWith('/') &&
                                e.FullName.StartsWith(fullMainDir, StringComparison.OrdinalIgnoreCase) &&
                                !e.FullName.Equals(mainEntry, StringComparison.OrdinalIgnoreCase) &&
                                !IsWellKnownVsCodeExtensionEntry(e.FullName, fullMainDir))
                    .ToList();

                if (adjacentServer.Count > 0 && adjacentServer.Count < 50)
                    cats.ServerFiles.AddRange(adjacentServer.Select(e => e.FullName));
            }
        }
    }

    /// <summary>
    /// Files adjacent to main entry that are NOT well-known non-server files.
    /// </summary>
    private static bool IsWellKnownVsCodeExtensionEntry(string path, string mainDir)
    {
        var name = Path.GetFileName(path);
        return name.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
    }

    // ── Visual Studio parser ────────────────────────────────────────

    private static VsixPackageInfo ParseVisualStudio(ZipArchive zip, ZipArchiveEntry manifestEntry)
    {
        using var stream = manifestEntry.Open();
        var doc = XDocument.Load(stream);

        // Try V2 namespace first, fall back to V1
        var ns = TryGetNamespace(doc, ManifestV2Ns) ?? XNamespace.Get(ManifestV1Ns);

        var identity = doc.Descendants(ns + "Identity").FirstOrDefault()
            ?? throw new InvalidOperationException("No <Identity> in vsixmanifest");

        var id = identity.Attribute("Id")?.Value ?? "unknown";
        var version = identity.Attribute("Version")?.Value ?? "0.0.0";
        var publisher = identity.Attribute("Publisher")?.Value ?? "Unknown";
        var displayName = doc.Descendants(ns + "DisplayName").FirstOrDefault()?.Value ?? id;

        var categories = new VsixFileCategories();

        // Read all assets from manifest
        foreach (var asset in doc.Descendants(ns + "Asset"))
        {
            var type = asset.Attribute("Type")?.Value;
            var path = asset.Attribute("Path")?.Value?.Replace('\\', '/');

            if (string.IsNullOrWhiteSpace(path))
                continue;

            switch (type)
            {
                case GrammarAssetType:
                    categories.GrammarFiles.Add(path);
                    break;
                case ThemeAssetType:
                    categories.ThemeFiles.Add(path);
                    break;
                case LspAssemblyAssetType:
                case LspNodeAssetType:
                    categories.ServerFiles.Add(path);
                    break;
            }
        }

        // Parse .pkgdef for additional TextMate repositories and LSP registrations
        foreach (var pkgdefEntry in zip.Entries
                     .Where(e => e.Name.EndsWith(".pkgdef", StringComparison.OrdinalIgnoreCase)))
        {
            using var ps = pkgdefEntry.Open();
            using var pr = new StreamReader(ps);
            var content = pr.ReadToEnd();

            var grammarDir = ParseGrammarDirectoryFromPkgdef(content);
            if (grammarDir is not null)
            {
                var prefix = grammarDir.TrimEnd('/') + "/";
                var extraGrammars = zip.Entries
                    .Where(e => !e.FullName.EndsWith('/') &&
                                e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                IsGrammarFile(e.Name))
                    .Select(e => e.FullName)
                    .Except(categories.GrammarFiles, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                categories.GrammarFiles.AddRange(extraGrammars);
            }
        }

        var allFiles = zip.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToList();

        return new VsixPackageInfo
        {
            Id = id,
            Version = version,
            Publisher = publisher,
            DisplayName = displayName,
            PackageKind = ExtensionPackageKind.VisualStudio,
            Files = categories,
            AllFiles = allFiles
        };
    }

    private static XNamespace? TryGetNamespace(XDocument doc, string ns)
    {
        var xns = XNamespace.Get(ns);
        return doc.Root?.Name.Namespace == xns ? xns : null;
    }

    // ── Fallback parser ─────────────────────────────────────────────

    private static VsixPackageInfo ParseUnknown(ZipArchive zip)
    {
        var categories = new VsixFileCategories();

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                continue;

            var name = entry.Name;

            if (IsGrammarFile(name))
                categories.GrammarFiles.Add(entry.FullName);
            else if (IsThemeFile(name))
                categories.ThemeFiles.Add(entry.FullName);
            else if (IsSnippetFile(name))
                categories.SnippetFiles.Add(entry.FullName);
            else if (IsLanguageConfigFile(name))
                categories.LanguageConfigFiles.Add(entry.FullName);
        }

        var allFiles = zip.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .Select(e => e.FullName)
            .ToList();

        return new VsixPackageInfo
        {
            Id = "unknown",
            DisplayName = "Unknown Extension",
            PackageKind = ExtensionPackageKind.VisualStudio,
            Files = categories,
            AllFiles = allFiles
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static string NormalizePackageDir(string manifestPath)
    {
        var dir = Path.GetDirectoryName(manifestPath)?.Replace('\\', '/') ?? string.Empty;
        return dir.TrimEnd('/');
    }

    private static string ResolvePath(string packageDir, string relative)
    {
        var norm = relative.Replace('\\', '/');
        if (norm.StartsWith("./", StringComparison.Ordinal))
            norm = norm[2..];
        if (string.IsNullOrWhiteSpace(packageDir))
            return norm;
        return $"{packageDir}/{norm}".TrimStart('/');
    }

    private static bool IsGrammarFile(string filename)
    {
        var ext = Path.GetExtension(filename);
        if (GrammarExtensions.Contains(ext))
            return true;
        // Some VS Code extensions embed grammars in plain .json files
        // under known grammar directories. We do not include .json here
        // to avoid false positives; they will be captured via
        // contributes.grammars[].path in the normal path.
        return false;
    }

    private static bool IsThemeFile(string filename)
    {
        var name = filename.AsSpan();
        // VS: .pkgdef theme, .theme, .vstheme
        // VS Code: .json (color theme file)
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".theme", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".vstheme", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSnippetFile(string filename)
    {
        return filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLanguageConfigFile(string filename)
    {
        return filename.Equals("language-configuration.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetContributes(JsonElement root, out JsonElement contributes)
    {
        if (root.TryGetProperty("contributes", out contributes) &&
            contributes.ValueKind == JsonValueKind.Object)
            return true;
        contributes = default;
        return false;
    }

    private static string? ParseGrammarDirectoryFromPkgdef(string content)
    {
        var match = TextMateRepositoryRegex().Match(content);
        if (!match.Success)
            return null;

        var rawPath = match.Groups[1].Value;
        return rawPath
            .Replace("$PackageFolder$\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$PackageFolder$/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$PackageFolder$", "", StringComparison.OrdinalIgnoreCase)
            .Replace('\\', '/')
            .TrimStart('/');
    }

    [GeneratedRegex(@"\[\$RootKey\$\\TextMate\\Repositories\][^\[]*""[^""]*""\s*=\s*""([^""]+)""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TextMateRepositoryRegex();
}
