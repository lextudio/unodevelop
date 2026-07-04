using System.IO;
using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.Core;

namespace UnoDevelop.TextTemplating;

public static class TextTemplatingStartup
{
    static bool initialized;

    public static void Initialize()
    {
        if (initialized)
            return;
        initialized = true;

        RegisterSyntaxHighlighting();
    }

    static void RegisterSyntaxHighlighting()
    {
        var assembly = typeof(TextTemplatingStartup).Assembly;
        var resourceName = "UnoDevelop.TextTemplating.Resources.TextTemplating.xshd";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            LoggingService.Warn("T4: syntax highlighting resource not found: " + resourceName);
            return;
        }

        using var reader = XmlReader.Create(stream);
        var xshd = HighlightingLoader.LoadXshd(reader);

        HighlightingManager.Instance.RegisterHighlighting(
            "T4",
            new[] { ".tt", ".t4", ".ttinclude" },
            HighlightingLoader.Load(xshd, HighlightingManager.Instance)
        );
    }
}
