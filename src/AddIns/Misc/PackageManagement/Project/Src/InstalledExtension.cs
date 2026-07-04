using System.Collections.Generic;
using System.Text.Json;

namespace UnoDevelop.AddIns;

internal enum ExtensionPackageKind
{
    VisualStudio,
    VSCode
}

internal sealed class InstalledExtension
{
    public required string Id { get; init; }
    public string Version { get; init; } = "0.0.0";
    public string Publisher { get; init; } = "Unknown";
    public string DisplayName { get; init; } = "";
    public string ExtractedPath { get; set; } = "";
    public ExtensionPackageKind PackageKind { get; init; }

    public int GrammarCount { get; init; }
    public int ThemeCount { get; init; }
    public int ServerCount { get; init; }

    public string KindLabel => PackageKind == ExtensionPackageKind.VSCode ? "VS Code" : "Visual Studio";

    // Uno's XAML doesn't support Binding.StringFormat, so the dialog binds to these instead.
    public string GrammarCountLabel => $"{GrammarCount} grammar(s)";
    public string ThemeCountLabel => $"{ThemeCount} theme(s)";
    public string ServerCountLabel => $"{ServerCount} server(s)";
}

internal static class ExtensionRegistry
{
    private static readonly string ExtensionsDir;

    static ExtensionRegistry()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnoDevelop");
        ExtensionsDir = Path.Combine(configDir, "Extensions");
        Directory.CreateDirectory(ExtensionsDir);
    }

    public static string GetExtensionsDirectory() => ExtensionsDir;

    public static List<InstalledExtension> GetAll()
    {
        var result = new List<InstalledExtension>();
        if (!Directory.Exists(ExtensionsDir))
            return result;

        foreach (var dir in Directory.GetDirectories(ExtensionsDir))
        {
            var metaFile = Path.Combine(dir, ".metadata.json");
            if (!File.Exists(metaFile))
                continue;

            try
            {
                var json = File.ReadAllText(metaFile);
                var ext = JsonSerializer.Deserialize<InstalledExtension>(json);
                if (ext is not null)
                    result.Add(ext);
            }
            catch { }
        }

        return result;
    }

    public static void Register(InstalledExtension ext)
    {
        var dir = Path.Combine(ExtensionsDir, SanitizeId(ext.Id));
        Directory.CreateDirectory(dir);

        var metaFile = Path.Combine(dir, ".metadata.json");
        var json = JsonSerializer.Serialize(ext);
        File.WriteAllText(metaFile, json);

        ext.ExtractedPath = dir;
    }

    public static void Unregister(string id)
    {
        var dir = Path.Combine(ExtensionsDir, SanitizeId(id));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static string SanitizeId(string id) =>
        string.Concat(id.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
