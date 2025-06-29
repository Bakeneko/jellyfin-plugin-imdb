using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.IMDb
{
    /// <summary>
    /// Register IMDb services.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {

        }
    }
}
