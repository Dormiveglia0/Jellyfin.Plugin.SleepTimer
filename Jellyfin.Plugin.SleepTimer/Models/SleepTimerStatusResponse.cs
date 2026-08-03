using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SleepTimer.Models;

/// <summary>
/// Public state for one user and device timer.
/// </summary>
public sealed class SleepTimerStatusResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether a timer is active.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the timer identifier.
    /// </summary>
    [JsonPropertyName("timerId")]
    public Guid? TimerId { get; set; }

    /// <summary>
    /// Gets or sets the original duration in minutes.
    /// </summary>
    [JsonPropertyName("durationMinutes")]
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the UTC expiration timestamp.
    /// </summary>
    [JsonPropertyName("endsAtUtc")]
    public DateTimeOffset? EndsAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the remaining whole seconds.
    /// </summary>
    [JsonPropertyName("remainingSeconds")]
    public int RemainingSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether playback and the countdown are paused.
    /// </summary>
    [JsonPropertyName("isPaused")]
    public bool IsPaused { get; set; }

    /// <summary>
    /// Gets or sets the expiration action.
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }
}
