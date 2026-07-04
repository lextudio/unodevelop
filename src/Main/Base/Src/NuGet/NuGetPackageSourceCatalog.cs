using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Configuration;

namespace ICSharpCode.SharpDevelop.NuGet
{
    /// <summary>
    /// Resolves configured NuGet package sources the same way <c>dotnet</c>/VS do — by walking up
    /// from a project/solution directory for <c>nuget.config</c> files, falling back to the
    /// machine-wide/user config. This is the piece slice 1 (docs/nuget-manager.md) deliberately
    /// left out; slice 3 (search) needs it to know which feeds to query.
    /// </summary>
    public static class NuGetPackageSourceCatalog
    {
        /// <summary>
        /// Enabled package sources for the given project/solution directory, in the priority
        /// order <see cref="PackageSourceProvider"/> reports them (closest/most specific
        /// <c>nuget.config</c> first).
        /// </summary>
        public static IReadOnlyList<PackageSource> LoadEnabledSources(string projectOrSolutionDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectOrSolutionDirectory))
                throw new ArgumentException("A directory is required.", nameof(projectOrSolutionDirectory));

            var settings = Settings.LoadDefaultSettings(projectOrSolutionDirectory);
            var provider = new PackageSourceProvider(settings);
            return provider.LoadPackageSources()
                .Where(source => source.IsEnabled)
                .ToArray();
        }
    }
}
