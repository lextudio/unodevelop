using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace UnoDevelop.Services;

internal static class ToolbarVisuals
{
    // Holds the colored + faded SVG sources for a toolbar icon image, so disabled state swaps
    // the drawn source instead of overlaying a wash.
    private sealed class IconSources
    {
        public string Icon = "";
        public ImageSource? Colored;
        public SvgImageSource? Faded;
    }

    public static Image CreateToolbarIcon(string icon)
    {
        var colored = new SvgImageSource(new Uri($"ms-appx:///Icons/{icon}.svg"));
        return new Image
        {
            Width = 16,
            Height = 16,
            Source = colored,
            Tag = new IconSources { Icon = icon, Colored = colored }
        };
    }

    // Keep toolbar controls visually flat at rest while preserving themed hover/pressed feedback.
    public static void ApplyFlatToolbarChrome(ButtonBase button)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundDisabled", "ButtonBorderBrush", "ButtonBorderBrushDisabled" })
            button.Resources[key] = transparent;
    }

    // Swap icon to a faded-color SVG when disabled to match the old WPF toolbar feel.
    public static void WireDisabledWash(ButtonBase button)
    {
        if (button.Content is not Image img || img.Tag is not IconSources state)
            return;

        void Apply()
        {
            if (button.IsEnabled)
            {
                img.Source = state.Colored;
                return;
            }

            state.Faded ??= TryLoadDisabled(state.Icon);
            img.Source = state.Faded ?? state.Colored;
        }

        button.IsEnabledChanged += (_, _) => Apply();
        Apply();
    }

    private const string DisabledIconFolder = "sd-disabled-icons";

    private static SvgImageSource? TryLoadDisabled(string icon)
    {
        var uri = EnsureDisabledVariant(icon);
        return uri is null ? null : new SvgImageSource(uri);
    }

    // Uno's SvgImageSource cannot load data:/in-memory streams, so cache a faded copy under
    // LocalFolder and load it through an ms-appdata URI.
    private static Uri? EnsureDisabledVariant(string icon)
    {
        try
        {
            var localRoot = global::Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            var dir = System.IO.Path.Combine(localRoot, DisabledIconFolder);
            var outPath = System.IO.Path.Combine(dir, $"{icon}.svg");

            if (!System.IO.File.Exists(outPath))
            {
                var srcPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Icons", $"{icon}.svg");
                if (!System.IO.File.Exists(srcPath))
                    return null;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(outPath, ToFadedSvg(System.IO.File.ReadAllText(srcPath)));
            }

            return new Uri($"ms-appdata:///local/{DisabledIconFolder}/{icon}.svg");
        }
        catch
        {
            return null;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex SvgOpenTag =
        new(@"<svg\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ToFadedSvg(string svg) =>
        SvgOpenTag.Replace(svg, "<svg opacity=\"0.35\"", 1);
}
