using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.IMDb.Configuration
{
    /// <summary>
    /// Plugin configuration class for IMDb plugin.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {

        /// <summary>
        /// Api base url.
        /// </summary>
        public string ApiBaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Api key.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Collect plot.
        /// </summary>
        public bool UsePlot { get; set; } = true;

        /// <summary>
        /// Collect episode plot.
        /// </summary>
        public bool UseEpisodePlot { get; set; } = true;

        /// <summary>
        /// Collect production year.
        /// </summary>
        public bool UseYear { get; set; } = true;

        /// <summary>
        /// Collect genres.
        /// </summary>
        public bool UseGenres{ get; set; } = true;

        /// <summary>
        /// Collect keywords.
        /// </summary>
        public bool UseKeywords { get; set; } = true;

        /// <summary>
        /// Collect rating.
        /// </summary>
        public bool UseRating { get; set; } = true;
    }
}
