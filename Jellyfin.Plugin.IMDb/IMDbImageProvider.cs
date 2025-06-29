#nullable disable

#pragma warning disable CS1591

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.IMDb
{
    public class IMDbImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMDbProvider _imdbProvider;

        public IMDbImageProvider(
            IHttpClientFactory httpClientFactory,
            IFileSystem fileSystem,
            IServerConfigurationManager configurationManager)
        {
            _httpClientFactory = httpClientFactory;
            _imdbProvider = new IMDbProvider(_httpClientFactory, fileSystem, configurationManager);
        }

        public string Name => "The Internet Movie Database";

        // After other internet providers, because they're better
        // But before fallback providers like screengrab
        public int Order => 90;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var imdbId = item.GetProviderId(MetadataProvider.Imdb);
            if (string.IsNullOrWhiteSpace(imdbId))
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var language = item.GetPreferredMetadataLanguage();
            var rootObject = await _imdbProvider.GetRootObject(imdbId, language, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(rootObject.posterUrl))
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            // the poster url is sometimes higher quality than the poster api
            return new[]
            {
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = rootObject.posterUrl
                }
            };
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }

        public bool Supports(BaseItem item)
        {
            return item is Movie || item is Trailer || item is Episode;
        }
    }
}
