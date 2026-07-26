// Ported verbatim from OpenDevelop's AndroidSdkManager (zero WPF dependency - plain POCO).

namespace ICSharpCode.AndroidSdkManager
{
    /// <summary>
    /// One row parsed from `sdkmanager --list --verbose` (installed/available/updates sections merged
    /// into a single record keyed by package Id, e.g. "platforms;android-26" or "build-tools;30.0.3").
    /// </summary>
    public sealed class SdkPackage
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string InstalledVersion { get; set; } = string.Empty;
        public string AvailableVersion { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
        public bool HasUpdate { get; set; }

        public string Size { get; set; } = string.Empty;

        public string VersionText => IsInstalled ? InstalledVersion : AvailableVersion;

        public string StatusText
        {
            get
            {
                if (HasUpdate)
                    return "Update available";
                if (IsInstalled)
                    return "Installed";
                return string.Empty;
            }
        }
    }
}
