#nullable disable

#pragma warning disable CS1591, SA1300

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.IMDb
{
    public class IMDbItemProvider : IRemoteMetadataProvider<Series, SeriesInfo>,
        IRemoteMetadataProvider<Movie, MovieInfo>, IRemoteMetadataProvider<Trailer, TrailerInfo>, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IMDbProvider _imdbProvider;

        public IMDbItemProvider(
            IHttpClientFactory httpClientFactory,
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IServerConfigurationManager configurationManager)
        {
            _httpClientFactory = httpClientFactory;
            _libraryManager = libraryManager;
            _imdbProvider = new IMDbProvider(
                _httpClientFactory,
                fileSystem,
                configurationManager);

            _jsonOptions = new JsonSerializerOptions(JsonDefaults.Options);
        }

        public string Name => "The Internet Movie Database";

        // After primary option
        public int Order => 2;

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TrailerInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, cancellationToken);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, cancellationToken);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, cancellationToken);
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            return GetSearchResultsInternal(searchInfo, cancellationToken);
        }

        private async Task<IEnumerable<RemoteSearchResult>> GetSearchResultsInternal(ItemLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            // This is a bit hacky?
            var episodeSearchInfo = searchInfo as EpisodeInfo;
            var indexNumberEnd = episodeSearchInfo?.IndexNumberEnd;
            var language = searchInfo.MetadataLanguage;

            var imdbId = searchInfo.GetProviderId(MetadataProvider.Imdb);

            if (string.IsNullOrWhiteSpace(imdbId))
            {
                var type = searchInfo switch
                {
                    MovieInfo => "movie",
                    EpisodeInfo => "tvEpisode",
                    SeriesInfo => "tvSeries",
                    _ => null
                };

                var name = searchInfo.Name;
                var year = searchInfo.Year;

                var baseUrl = Plugin.Instance.Configuration.ApiBaseUrl;
                var apiKey = Plugin.Instance.Configuration.ApiKey;
                var urlQuery = new StringBuilder("&language=").Append(language);

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var parsedName = _libraryManager.ParseName(name);
                    var yearInName = parsedName.Year;
                    name = parsedName.Name;
                    year ??= yearInName;
                }

                urlQuery.Append("&title=").Append(WebUtility.UrlEncode(name));

                if (year.HasValue)
                {
                    urlQuery.Append("&year=").Append(year);
                }

                if (type != null)
                {
                    urlQuery.Append("&type=").Append(type);
                }

                var url = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}/imdb/search?apikey={1}{2}",
                        baseUrl, apiKey, urlQuery.ToString());

                var searchResults = await _httpClientFactory.CreateClient(NamedClient.Default).GetFromJsonAsync<SearchResult[]>(url, _jsonOptions, cancellationToken).ConfigureAwait(false);

                var resultCount = searchResults.Length;
                var results = new RemoteSearchResult[resultCount];
                for (var i = 0; i < resultCount; i++)
                {
                    results[i] = ResultToMetadataResult(searchResults[i], searchInfo, indexNumberEnd);
                }

                return results;
            }
            else
            {
                var item = await _imdbProvider.GetRootObject(imdbId, language, cancellationToken).ConfigureAwait(false);

                var result = new RemoteSearchResult
                {
                    IndexNumber = searchInfo.IndexNumber,
                    Name = item.title,
                    ParentIndexNumber = searchInfo.ParentIndexNumber,
                    SearchProviderName = Name,
                    IndexNumberEnd = indexNumberEnd,
                    ProductionYear = item.year,
                    ImageUrl = item.posterUrl,
                };
                result.SetProviderId(MetadataProvider.Imdb, item.imdbId);

                return [result];
            }
        }

        public Task<MetadataResult<Trailer>> GetMetadata(TrailerInfo info, CancellationToken cancellationToken)
        {
            return GetResult<Trailer>(info, cancellationToken);
        }

        public Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            return GetResult<Series>(info, cancellationToken);
        }

        public Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            return GetResult<Movie>(info, cancellationToken);
        }

        private RemoteSearchResult ResultToMetadataResult(SearchResult result, ItemLookupInfo searchInfo, int? indexNumberEnd)
        {
            var item = new RemoteSearchResult
            {
                IndexNumber = searchInfo.IndexNumber,
                Name = result.title,
                ParentIndexNumber = searchInfo.ParentIndexNumber,
                SearchProviderName = Name,
                IndexNumberEnd = indexNumberEnd,
                ProductionYear = result.year,
                ImageUrl = result.posterUrl
            };

            item.SetProviderId(MetadataProvider.Imdb, result.imdbId);

            return item;
        }

        private async Task<MetadataResult<T>> GetResult<T>(ItemLookupInfo info, CancellationToken cancellationToken)
            where T : BaseItem, new()
        {
            var result = new MetadataResult<T>
            {
                Item = new T(),
                QueriedById = true
            };

            var imdbId = info.GetProviderId(MetadataProvider.Imdb);
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                imdbId = await GetImdbId(info, cancellationToken).ConfigureAwait(false);
                result.QueriedById = false;
            }

            if (!string.IsNullOrEmpty(imdbId))
            {
                result.Item.SetProviderId(MetadataProvider.Imdb, imdbId);
                result.HasMetadata = true;

                await _imdbProvider.Fetch(result, imdbId, info.MetadataLanguage, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        private async Task<string> GetImdbId(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            var results = await GetSearchResultsInternal(info, cancellationToken).ConfigureAwait(false);
            var first = results.FirstOrDefault();
            return first?.GetProviderId(MetadataProvider.Imdb);
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }

        private sealed class SearchResult
        {
            public string imdbId { get; set; }

            public string title { get; set; }

            public string type { get; set; }

            public float rating { get; set; }

            public string posterUrl { get; set; }

            public int year { get; set; }
        }
    }
}
