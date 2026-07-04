using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ICSharpCode.Core;

public static partial class FileUtility
{
    private static readonly char[] Separators =
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
    };

    private static string _applicationRootPath = AppContext.BaseDirectory;

    public static string ApplicationRootPath
    {
        get => _applicationRootPath;
        set => _applicationRootPath = value;
    }

    public static string GetRelativePath(string baseDirectoryPath, string absPath)
    {
        if (string.IsNullOrEmpty(baseDirectoryPath))
        {
            return absPath;
        }

        baseDirectoryPath = NormalizePath(baseDirectoryPath);
        absPath = NormalizePath(absPath);

        var baseParts = baseDirectoryPath == "." ? Array.Empty<string>() : baseDirectoryPath.Split(Separators);
        var absParts = absPath == "." ? Array.Empty<string>() : absPath.Split(Separators);

        var i = 0;
        for (; i < Math.Min(baseParts.Length, absParts.Length); i++)
        {
            if (!baseParts[i].Equals(absParts[i], StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        if (i == 0 && (Path.IsPathRooted(baseDirectoryPath) || Path.IsPathRooted(absPath)))
        {
            return absPath;
        }

        if (i == baseParts.Length && i == absParts.Length)
        {
            return ".";
        }

        var builder = new StringBuilder();
        for (var j = i; j < baseParts.Length; j++)
        {
            builder.Append("..");
            builder.Append(Path.DirectorySeparatorChar);
        }

        builder.Append(string.Join(Path.DirectorySeparatorChar.ToString(), absParts, i, absParts.Length - i));
        if (builder.Length > 0 && builder[^1] == Path.DirectorySeparatorChar)
        {
            builder.Length -= 1;
        }

        return builder.ToString();
    }

    public static string GetAbsolutePath(string baseDirectoryPath, string relPath)
    {
        return NormalizePath(Path.Combine(baseDirectoryPath, relPath));
    }

    public static bool IsValidDirectoryEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        return name.IndexOfAny(invalidChars) < 0;
    }

    public static IEnumerable<FileName> LazySearchDirectory(string directory, string searchPattern, bool searchSubdirectories = true, bool ignoreHidden = true)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        var searchOption = searchSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, searchPattern, searchOption);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (ignoreHidden && IsHiddenFile(file))
            {
                continue;
            }

            var fileName = FileName.Create(file);
            if (fileName is not null)
            {
                yield return fileName;
            }
        }
    }

    private static bool IsHiddenFile(string path)
    {
        try
        {
            return (File.GetAttributes(path) & System.IO.FileAttributes.Hidden) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUrl(string path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));
        return path.IndexOf("://", StringComparison.Ordinal) > 0;
    }

    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        try
        {
            var _ = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
