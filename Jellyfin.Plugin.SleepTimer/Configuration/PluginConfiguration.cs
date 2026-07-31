using Jellyfin.Plugin.SleepTimer.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SleepTimer.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        PresetMinutes = "15,30,45,60,90,120";
        DefaultAction = SleepTimerAction.Pause;
        MaximumMinutes = 720;
        AllowCustomDuration = true;
    }

    /// <summary>
    /// Gets or sets the comma-separated timer presets shown in the player.
    /// </summary>
    public string PresetMinutes { get; set; }

    /// <summary>
    /// Gets or sets the default action used when a timer expires.
    /// </summary>
    public SleepTimerAction DefaultAction { get; set; }

    /// <summary>
    /// Gets or sets the maximum custom duration in minutes.
    /// </summary>
    public int MaximumMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether custom durations are available.
    /// </summary>
    public bool AllowCustomDuration { get; set; }
}
