using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.Core;

namespace FSharpBinding;

public static class FSharpBindingStartup
{
    static bool initialized;

    public static void Initialize()
    {
        if (initialized)
            return;
        initialized = true;

        var assembly = typeof(FSharpBindingStartup).Assembly;
        const string resourceName = "FSharpBinding.Resources.FS-Mode.xshd";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            LoggingService.Warn("F#: syntax highlighting resource not found: " + resourceName);
            return;
        }

        using var reader = XmlReader.Create(stream);
        var xshd = HighlightingLoader.LoadXshd(reader);
        HighlightingManager.Instance.RegisterHighlighting(
            "F#",
            new[] { ".fs", ".fsi" },
            HighlightingLoader.Load(xshd, HighlightingManager.Instance));
    }
}
