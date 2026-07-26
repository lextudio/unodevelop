// Ported verbatim from OpenDevelop's AndroidDeviceManager (zero WPF dependency - plain POCOs).

namespace ICSharpCode.AndroidDeviceManager
{
    /// <summary>One row parsed from `avdmanager list avd`.</summary>
    public sealed class AvdInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string BasedOn { get; set; } = string.Empty;
        public string Skin { get; set; } = string.Empty;
        public string Sdcard { get; set; } = string.Empty;

        public string ConfigIniPath => System.IO.Path.Combine(Path ?? string.Empty, "config.ini");
    }

    /// <summary>One row parsed from `avdmanager list device`: a hardware profile like "pixel_3a" / "Pixel 3a".</summary>
    public sealed class DeviceDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    /// <summary>One row parsed from `sdkmanager --list` restricted to "system-images;..." packages.</summary>
    public sealed class SystemImageInfo
    {
        public string PackageId { get; set; } = string.Empty;
        public string ApiLevel { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Abi { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }
}
