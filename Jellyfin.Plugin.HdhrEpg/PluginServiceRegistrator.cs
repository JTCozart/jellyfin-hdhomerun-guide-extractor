using Jellyfin.Plugin.HdhrEpg.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HdhrEpg;

/// <summary>
/// Registers the plugin's services into the host DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<HdhrEpgGenerator>();
        serviceCollection.AddSingleton<EpgStatus>();

        // Registered as itself (not just IHostedService) so the API controller can call
        // RefreshNowAsync on the exact same instance the background loop uses.
        serviceCollection.AddSingleton<EpgRefreshService>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<EpgRefreshService>());
    }
}
