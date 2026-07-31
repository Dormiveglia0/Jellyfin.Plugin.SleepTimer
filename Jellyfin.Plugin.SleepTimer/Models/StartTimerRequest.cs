using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SleepTimer.Models;

/// <summary>
/// Request to create or replace a sleep timer.
/// </summary>
public sealed class StartTimerRequest
{
    /// <summary>
    /// Gets or sets the timer duration in whole minutes.
    /// </summary>
    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the expiration action as "pause" or "stop".
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>
    /// Gets or sets the client device identifier.
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}
