using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace UnoDevelop.AddIns;

internal static class ExtensionInstaller
{
    public static InstalledExtension Install(string vsixPath)
    {
        var info = VsixPackageParser.Parse(vsixPath);
        var extensionsDir = ExtensionRegistry.GetExtensionsDirectory();
        var targetDir = Path.Combine(extensionsDir, SanitizeId(info.Id));

        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, recursive: true);

        Directory.CreateDirectory(targetDir);

        using var zip = ZipFile.OpenRead(vsixPath);

        ExtractFiles(zip, targetDir, info.Files.GrammarFiles);
        ExtractFiles(zip, targetDir, info.Files.ThemeFiles);
        ExtractFiles(zip, targetDir, info.Files.SnippetFiles);
        ExtractFiles(zip, targetDir, info.Files.LanguageConfigFiles);
        ExtractFiles(zip, targetDir, info.Files.ServerFiles);

        var installed = new InstalledExtension
        {
            Id = info.Id,
            Version = info.Version,
            Publisher = info.Publisher,
            DisplayName = info.DisplayName,
            ExtractedPath = targetDir,
            PackageKind = info.PackageKind,
            GrammarCount = info.GrammarCount,
            ThemeCount = info.ThemeCount,
            ServerCount = info.ServerCount
        };

        ExtensionRegistry.Register(installed);
        return installed;
    }

    public static void Uninstall(string id)
    {
        ExtensionRegistry.Unregister(id);
    }

    private static void ExtractFiles(ZipArchive zip, string targetDir, List<string> filePaths)
    {
        foreach (var relativePath in filePaths)
        {
            var entry = zip.GetEntry(relativePath);
            if (entry is null)
                continue;

            var dest = Path.Combine(targetDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static string SanitizeId(string id) =>
        string.Concat(id.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
