using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace ICSharpCode.SharpDevelop.NuGet
{
    /// <summary>
    /// Searches configured NuGet feeds (docs/nuget-manager.md slice 3) via
    /// <c>NuGet.Protocol</c>'s <see cref="PackageSearchResource"/> — the real NuGet.Client search
    /// API, the same one <c>dotnet package search</c>/VS use, not a hand-rolled HTTP client
    /// against the NuGet v3 API.
    /// </summary>
    public sealed class NuGetPackageSearchService
    {
        readonly ILogger _logger;

        public NuGetPackageSearchService(ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Searches every given source and returns a deduplicated (by package id, first source
        /// wins — sources are expected in priority order, see
        /// <see cref="NuGetPackageSourceCatalog.LoadEnabledSources"/>), alphabetically sorted list.
        /// A source that fails to respond (network error, misconfigured URL, ...) is skipped with
        /// a logged warning rather than failing the whole search — one bad feed shouldn't block
        /// results from the others.
        /// </summary>
        public async Task<IReadOnlyList<NuGetSearchResult>> SearchAsync(
            IReadOnlyList<PackageSource> sources,
            string searchTerm,
            bool includePrerelease,
            int take,
            CancellationToken cancellationToken)
        {
            if (sources is null)
                throw new ArgumentNullException(nameof(sources));
            if (searchTerm is null)
                throw new ArgumentNullException(nameof(searchTerm));

            var filter = new SearchFilter(includePrerelease);
            var resultsById = new Dictionary<string, NuGetSearchResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IEnumerable<IPackageSearchMetadata> metadata;
                try
                {
                    var repository = Repository.Factory.GetCoreV3(source);
                    var searchResource = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
                    if (searchResource is null)
                        continue;

                    metadata = await searchResource.SearchAsync(searchTerm, filter, skip: 0, take: take, _logger, cancellationToken);
                }
                catch (Exception ex)
                {
                    LoggingService.Warn($"NuGet search against '{source.Name}' ({source.SourceUri}) failed: {ex.Message}");
                    continue;
                }

                foreach (var result in metadata)
                {
                    if (resultsById.ContainsKey(result.Identity.Id))
                        continue;

                    resultsById[result.Identity.Id] = new NuGetSearchResult(
                        result.Identity.Id,
                        result.Identity.Version.ToNormalizedString(),
                        result.Description,
                        result.DownloadCount,
                        result.IconUrl?.ToString(),
                        source.Name);
                }
            }

            return resultsById.Values
                .OrderBy(result => result.Id, StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToArray();
        }
    }
}
