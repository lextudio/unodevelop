namespace ICSharpCode.SharpDevelop.Workbench;

public sealed partial class WorkbenchPlatformBridge
{
    public string PlatformTag => GetPlatformTag();

    private static partial string GetPlatformTag();
}
