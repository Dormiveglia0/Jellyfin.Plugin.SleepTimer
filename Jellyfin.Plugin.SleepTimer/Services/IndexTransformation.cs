using Jellyfin.Plugin.SleepTimer.Models;

namespace Jellyfin.Plugin.SleepTimer.Services;

/// <summary>
/// Callback used by the optional File Transformation plugin.
/// </summary>
public static class IndexTransformation
{
    /// <summary>
    /// Adds the Sleep Timer client loader to Jellyfin Web.
    /// </summary>
    /// <param name="request">File transformation request.</param>
    /// <returns>Transformed index HTML.</returns>
    public static string Transform(TransformationRequest request)
    {
        return WebInjectionService.ApplyInjection(request.Contents ?? string.Empty);
    }
}
