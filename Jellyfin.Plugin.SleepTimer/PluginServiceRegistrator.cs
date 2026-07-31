using Jellyfin.Plugin.SleepTimer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SleepTimer;

/// <summary>
/// Registers plugin services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ISleepTimerService, SleepTimerService>();
        serviceCollection.AddHostedService(
            provider => (SleepTimerService)provider.GetRequiredService<ISleepTimerService>());
        serviceCollection.AddHostedService<WebInjectionService>();
    }
}
