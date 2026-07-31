using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SleepTimer.Models;

/// <summary>
/// Payload passed by the File Transformation plugin.
/// </summary>
public sealed class TransformationRequest
{
    /// <summary>
    /// Gets or sets the current contents of the requested web file.
    /// </summary>
    [JsonPropertyName("contents")]
    public string? Contents { get; set; }
}
