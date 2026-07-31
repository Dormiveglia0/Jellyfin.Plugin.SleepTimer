using Jellyfin.Plugin.SleepTimer.Models;

namespace Jellyfin.Plugin.SleepTimer.Services;

/// <summary>
/// Manages per-user, per-device sleep timers.
/// </summary>
public interface ISleepTimerService
{
    /// <summary>
    /// Creates or replaces a timer.
    /// </summary>
    /// <param name="userId">Authenticated user identifier.</param>
    /// <param name="deviceId">Client device identifier.</param>
    /// <param name="durationMinutes">Duration in minutes.</param>
    /// <param name="action">Expiration action.</param>
    /// <returns>The new timer state.</returns>
    SleepTimerStatusResponse StartTimer(
        Guid userId,
        string deviceId,
        int durationMinutes,
        SleepTimerAction action);

    /// <summary>
    /// Cancels an active timer.
    /// </summary>
    /// <param name="userId">Authenticated user identifier.</param>
    /// <param name="deviceId">Client device identifier.</param>
    /// <returns><see langword="true"/> when a timer was removed.</returns>
    bool CancelTimer(Guid userId, string deviceId);

    /// <summary>
    /// Gets the current timer state.
    /// </summary>
    /// <param name="userId">Authenticated user identifier.</param>
    /// <param name="deviceId">Client device identifier.</param>
    /// <returns>The timer state.</returns>
    SleepTimerStatusResponse GetStatus(Guid userId, string deviceId);
}
