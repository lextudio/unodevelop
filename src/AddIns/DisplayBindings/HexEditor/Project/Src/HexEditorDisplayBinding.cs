using System;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.AddIns.DisplayBindings.HexEditor;

public sealed class HexEditorDisplayBinding : IDisplayBinding
{
    private static readonly HashSet<string> PreferredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin",
        ".dat",
        ".dll",
        ".exe",
        ".ico",
        ".cur",
        ".pdb",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".zip",
        ".nupkg",
        ".snk"
    };

    public bool CanCreateContentForFile(FileName fileName)
    {
        return PreferredExtensions.Contains(Path.GetExtension(fileName.ToString()));
    }

    public IViewContent CreateContentForFile(OpenedFile file)
    {
        return new HexEditorViewContent(file.FileName);
    }

    public bool IsPreferredBindingForFile(FileName fileName)
    {
        return CanCreateContentForFile(fileName);
    }

    public double AutoDetectFileContent(FileName fileName, Stream fileContent, string detectedMimeType)
    {
        if (CanCreateContentForFile(fileName))
        {
            return 1;
        }

        return LooksBinary(fileContent) ? 0.9 : 0;
    }

    private static bool LooksBinary(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        var original = stream.Position;
        try
        {
            stream.Position = 0;
            var length = (int)Math.Min(stream.Length, 4096);
            Span<byte> buffer = length <= 1024 ? stackalloc byte[length] : new byte[length];
            var read = stream.Read(buffer);
            return buffer[..read].Contains((byte)0);
        }
        finally
        {
            stream.Position = original;
        }
    }
}
