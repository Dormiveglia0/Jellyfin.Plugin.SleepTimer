namespace Jellyfin.Plugin.SleepTimer.Models;

/// <summary>
/// Action to perform when a sleep timer expires.
/// </summary>
public enum SleepTimerAction
{
    /// <summary>
    /// Pause the active playback session.
    /// </summary>
    Pause,

    /// <summary>
    /// Stop playback and leave the video player.
    /// </summary>
    Stop
}
