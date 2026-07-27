using System;
using System.IO;
using System.Linq;

namespace ICSharpCode.SharpDevelop.MSBuildHosting;

/// <summary>
/// Bootstraps in-process MSBuild (<see cref="Microsoft.Build.Evaluation.Project"/>/<see cref="Microsoft.Build.Evaluation.ProjectCollection"/>)
/// against the real installed .NET SDK's own MSBuild toolset, instead of the NuGet-restored
/// <c>Microsoft.Build</c> package copy that would otherwise get loaded from this app's own bin
/// directory. Without this, in-process project evaluation cannot find the .NET SDK's own SDK
/// resolver (<c>Microsoft.DotNet.MSBuildSdkResolver</c>) - so an SDK-style project's own SDK
/// import (<c>Sdk="Uno.Sdk"</c>, and transitively <c>Microsoft.NET.Sdk</c>) silently fails to
/// resolve, and every SDK-default item glob (<c>Compile</c>, <c>Page</c>, ...) never gets added to
/// the evaluated project - only items with an explicit &lt;Include&gt; written by hand in the
/// .csproj survive. That is the root cause of Solution Explorer showing a project's files only
/// after "Show All Files" is toggled: they were never "in the project" as far as UnoDevelop's own
/// in-process evaluation could tell, real MSBuild CLI builds (which spawn a real `dotnet build`
/// process and therefore never hit this path) notwithstanding.
///
/// <see cref="EnsureRegistered"/> MUST run before ANY type from a <c>Microsoft.Build.*</c>
/// assembly is touched anywhere in the process - once the CLR has resolved/loaded those
/// assemblies (from wherever the normal probing path finds them first), MSBuildLocator can no
/// longer redirect that resolution to the real SDK's copies. It is called as the very first
/// statement of <c>ServiceBootstrapper.Initialize()</c>, which itself runs as the first
/// meaningful thing App's constructor does, before anything else in the app has a chance to
/// construct a project/solution model.
/// </summary>
public static class MSBuildEnvironmentInitializer
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
            return;
        _registered = true;

        var latestSdk = FindLatestSdkDirectory();
        if (latestSdk is null)
            return;

        if (!Microsoft.Build.Locator.MSBuildLocator.IsRegistered)
            Microsoft.Build.Locator.MSBuildLocator.RegisterMSBuildPath(latestSdk);
    }

    /// <summary>The most recent .NET SDK's own toolset directory (e.g. .../dotnet/sdk/10.0.301).</summary>
    public static string? FindLatestSdkDirectory()
    {
        var dotnetRoot = GetDotnetRoot();
        if (dotnetRoot is null)
            return null;

        var sdksDir = Path.Combine(dotnetRoot, "sdk");
        if (!Directory.Exists(sdksDir))
            return null;

        return Directory.GetDirectories(sdksDir)
            .Where(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out _))
            .OrderByDescending(d =>
            {
                Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v);
                return v;
            })
            .FirstOrDefault();
    }

    private static string? GetDotnetRoot()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
            return Path.GetDirectoryName(hostPath);

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
            return dotnetRoot;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidate = Path.Combine(programFiles, "dotnet");
        if (Directory.Exists(candidate))
            return candidate;

        return null;
    }
}
