using System;
using System.IO;
using ICSharpCode.Core;

namespace UnoDevelop.Services;

internal sealed class UnoPropertyService : PropertyServiceImpl
{
    private readonly DirectoryName _configDirectory;
    private readonly DirectoryName _dataDirectory;

    private static Properties LoadPropertiesFromDisk()
    {
        var path = GetPropertiesFilePath();
        if (File.Exists(path))
        {
            try { return Properties.Load(FileName.Create(path)); }
            catch { }
        }
        return new Properties();
    }

    private static string GetPropertiesFilePath()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnoDevelop");
        return Path.Combine(root, "config", "UnoDevelop.properties.xml");
    }

    public UnoPropertyService()
        : base(LoadPropertiesFromDisk())
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnoDevelop");
        var config = Path.Combine(root, "config");
        var data = Path.Combine(root, "data");

        Directory.CreateDirectory(config);
        Directory.CreateDirectory(data);

        _configDirectory = new DirectoryName(config);
        _dataDirectory = new DirectoryName(data);

        SeedBundledData(data);
    }

    /// <summary>
    /// DataDirectory lives under LocalAppData, empty on first run - copy app-bundled data (e.g.
    /// data\layouts\*, shipped via Content items in SharpDevelop.csproj) alongside it so services
    /// like LayoutConfiguration can find their built-in defaults the same way SharpDevelop/
    /// OpenDevelop find theirs under their repo-relative data directory.
    /// </summary>
    private static void SeedBundledData(string dataDirectory)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(bundled))
            return;

        foreach (var sourceFile in Directory.EnumerateFiles(bundled, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(bundled, sourceFile);
            var destFile = Path.Combine(dataDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(sourceFile, destFile, overwrite: true);
        }
    }

    public override void Save()
    {
        var path = GetPropertiesFilePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            MainPropertiesContainer.Save(FileName.Create(path));
        }
        catch
        {
        }
    }

    public override DirectoryName ConfigDirectory => _configDirectory;

    public override DirectoryName DataDirectory => _dataDirectory;
}
