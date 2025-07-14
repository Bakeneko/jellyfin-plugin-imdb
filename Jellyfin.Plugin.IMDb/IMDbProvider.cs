#nullable disable

#pragma warning disable CS159, SA1300

using System;
using System.Globalization;
using System.IO;
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
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.IMDb
{
    /// <summary>Provider for IMDb service.</summary>
    public class IMDbProvider
    {
        private readonly IFileSystem _fileSystem;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>Initializes a new instance of the <see cref="IMDbProvider"/> class.</summary>
        /// <param name="httpClientFactory">HttpClientFactory to use for calls to IMDb service.</param>
        /// <param name="fileSystem">IFileSystem to use for store IMDb data.</param>
        /// <param name="configurationManager">IServerConfigurationManager to use.</param>
        public IMDbProvider(
            IHttpClientFactory httpClientFactory,
            IFileSystem fileSystem,
            IServerConfigurationManager configurationManager)
        {
            _httpClientFactory = httpClientFactory;
            _fileSystem = fileSystem;
            _configurationManager = configurationManager;

            _jsonOptions = new JsonSerializerOptions(JsonDefaults.Options);
        }

        /// <summary>Fetches data from IMDb service.</summary>
        /// <param name="itemResult">Metadata about media item.</param>
        /// <param name="imdbId">IMDb ID for media.</param>
        /// <param name="language">Media language.</param>
        /// <param name="country">Country of origin.</param>
        /// <param name="cancellationToken">CancellationToken to use for operation.</param>
        /// <typeparam name="T">The first generic type parameter.</typeparam>
        /// <returns>Returns a Task object that can be awaited.</returns>
        public async Task Fetch<T>(MetadataResult<T> itemResult, string imdbId, string language, string country, CancellationToken cancellationToken)
            where T : BaseItem
        {
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                throw new ArgumentNullException(nameof(imdbId));
            }

            var item = itemResult.Item;

            var result = await GetRootObject(imdbId, language, cancellationToken).ConfigureAwait(false);

            item.Name = result.title;
            item.OriginalTitle = result.originalTitle;

            if (!string.IsNullOrWhiteSpace(result.imdbId))
            {
                item.SetProviderId(MetadataProvider.Imdb, result.imdbId);
            }

            if (Plugin.Instance.Configuration.UseYear)
            {
                item.ProductionYear = result.year;
            }

            if (!string.IsNullOrEmpty(result.release)
                && DateTime.TryParse(result.release, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var release))
            {
                item.PremiereDate = release;
            }

            if (Plugin.Instance.Configuration.UseRating && result.rating >= 0)
            {
                item.CommunityRating = result.rating;
            }

            if (Plugin.Instance.Configuration.UseGenres)
            {
                item.Genres = result.genres;
            }

            if (Plugin.Instance.Configuration.UseKeywords)
            {
                item.Tags = result.keywords;
            }

            if (Plugin.Instance.Configuration.UsePlot)
            {
                item.Overview = result.synopsis;
            }
        }

        /// <summary>Gets data about an episode.</summary>
        /// <param name="itemResult">Metadata about episode.</param>
        /// <param name="episodeNumber">Episode number.</param>
        /// <param name="seasonNumber">Season number.</param>
        /// <param name="episodeImdbId">Episode ID.</param>
        /// <param name="seriesImdbId">Season ID.</param>
        /// <param name="language">Episode language.</param>
        /// <param name="country">Country of origin.</param>
        /// <param name="cancellationToken">CancellationToken to use for operation.</param>
        /// <typeparam name="T">The first generic type parameter.</typeparam>
        /// <returns>Whether operation was successful.</returns>
        public async Task<bool> FetchEpisodeData<T>(MetadataResult<T> itemResult, int episodeNumber, int seasonNumber, string episodeImdbId, string seriesImdbId, string language, string country, CancellationToken cancellationToken)
            where T : BaseItem
        {
            if (string.IsNullOrWhiteSpace(seriesImdbId))
            {
                throw new ArgumentNullException(nameof(seriesImdbId));
            }

            var item = itemResult.Item;

            var seriesResult = await GetRootObject(seriesImdbId, language, cancellationToken).ConfigureAwait(false);

            if (seriesResult?.episodes is null)
            {
                return false;
            }

            EpisodeRootObject result = null;

            // Search by imdb id if available
            if (!string.IsNullOrWhiteSpace(episodeImdbId))
            {
                foreach (var episode in seriesResult.episodes)
                {
                    if (string.Equals(episodeImdbId, episode.imdbId, StringComparison.OrdinalIgnoreCase))
                    {
                        result = episode;
                        break;
                    }
                }
            }

            // finally, search by numbers
            if (result is null)
            {
                foreach (var episode in seriesResult.episodes)
                {
                    if (episode.season == seasonNumber && episode.number == episodeNumber)
                    {
                        result = episode;
                        break;
                    }
                }
            }

            if (result is null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(result.imdbId))
            {
                item.SetProviderId(MetadataProvider.Imdb, result.imdbId);
            }

            item.Name = result.title;
            item.OriginalTitle = result.title;

            if (Plugin.Instance.Configuration.UseRating && result.rating >= 0)
            {
                item.CommunityRating = result.rating;
            }

            if (Plugin.Instance.Configuration.UseEpisodePlot)
            {
                item.Overview = result.synopsis;
            }

            return true;
        }

        internal async Task<RootObject> GetRootObject(string imdbId, string language, CancellationToken cancellationToken)
        {
            var path = await EnsureItemInfo(imdbId, language, cancellationToken).ConfigureAwait(false);
            var stream = AsyncFile.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<RootObject>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<string> EnsureItemInfo(string imdbId, string language, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                throw new ArgumentNullException(nameof(imdbId));
            }

            var imdbParam = imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? imdbId : "tt" + imdbId;

            var path = GetDataFilePath(imdbParam, language);

            var fileInfo = _fileSystem.GetFileSystemInfo(path);

            if (fileInfo.Exists)
            {
                // Use cache if fresh
                if ((DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalHours <= 1)
                {
                    return path;
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }

            var baseUrl = Plugin.Instance.Configuration.ApiBaseUrl;
            var apiKey = Plugin.Instance.Configuration.ApiKey;
            var urlQuery = new StringBuilder("&language=")
                .Append(language);
            urlQuery.Append("&episodes=true");

            var url = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}/imdb/title/{1}?apikey={2}{3}",
                    baseUrl, imdbId, apiKey, urlQuery.ToString());

            var rootObject = await _httpClientFactory.CreateClient(NamedClient.Default).GetFromJsonAsync<RootObject>(url, _jsonOptions, cancellationToken).ConfigureAwait(false);
            FileStream jsonFileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, IODefaults.FileStreamBufferSize, FileOptions.Asynchronous);
            await using (jsonFileStream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(jsonFileStream, rootObject, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }

            return path;
        }

        internal string GetDataFilePath(string imdbId, string language)
        {
            ArgumentException.ThrowIfNullOrEmpty(imdbId);

            var dataPath = Path.Combine(_configurationManager.ApplicationPaths.CachePath, "imdb");

            var filename = string.Format(CultureInfo.InvariantCulture, "{0}_{1}.json", imdbId, language);

            return Path.Combine(dataPath, filename);
        }

        internal sealed class RootObject
        {
            public string imdbId { get; set; }

            public string title { get; set; }

            public string originalTitle { get; set; }

            public string type { get; set; }

            public string synopsis { get; set; }

            public float? rating { get; set; }

            public string[] genres { get; set; }

            public string[] keywords { get; set; }

            public string posterUrl { get; set; }

            public int? runtime { get; set; }

            public int year { get; set; }

            public string release { get; set; }

            public int? seasons { get; set; }

            public EpisodeRootObject[] episodes { get; set; }
        }

        internal sealed class EpisodeRootObject
        {
            public string imdbId { get; set; }

            public string seriesImdbId { get; set; }

            public string title { get; set; }

            public string seriesTitle { get; set; }

            public int season { get; set; }

            public int number { get; set; }

            public string synopsis { get; set; }

            public float? rating { get; set; }

            public string posterUrl { get; set; }

            public string release { get; set; }

            public int? year { get; set; }
        }
    }
}
