using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.SleepTimer.Configuration;
using Jellyfin.Plugin.SleepTimer.Models;
using Jellyfin.Plugin.SleepTimer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SleepTimer.Api;

/// <summary>
/// API used by the injected Jellyfin Web controls.
/// </summary>
[ApiController]
[Authorize]
[Route("SleepTimer")]
public sealed class SleepTimerController : ControllerBase
{
    private const int AbsoluteMaximumMinutes = 1440;
    private readonly ISleepTimerService _sleepTimerService;
    private readonly ILogger<SleepTimerController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SleepTimerController"/> class.
    /// </summary>
    /// <param name="sleepTimerService">Timer service.</param>
    /// <param name="logger">Controller logger.</param>
    public SleepTimerController(
        ISleepTimerService sleepTimerService,
        ILogger<SleepTimerController> logger)
    {
        _sleepTimerService = sleepTimerService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the embedded browser client.
    /// </summary>
    /// <returns>JavaScript source.</returns>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public ActionResult GetClientScript()
    {
        const string resourceName = "Jellyfin.Plugin.SleepTimer.Web.client.js";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream is null
            ? NotFound()
            : File(stream, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Returns the theme-aware browser client stylesheet.
    /// </summary>
    /// <returns>CSS source.</returns>
    [HttpGet("client.css")]
    [AllowAnonymous]
    [Produces("text/css")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public ActionResult GetClientStyles()
    {
        const string resourceName = "Jellyfin.Plugin.SleepTimer.Web.client.css";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream is null
            ? NotFound()
            : File(stream, "text/css; charset=utf-8");
    }

    /// <summary>
    /// Gets safe player configuration.
    /// </summary>
    /// <returns>Client options.</returns>
    [HttpGet("options")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetOptions()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var maximumMinutes = GetMaximumMinutes(configuration);
        var presets = ParsePresets(configuration.PresetMinutes, maximumMinutes);

        return Ok(new
        {
            presetMinutes = presets,
            defaultAction = configuration.DefaultAction.ToString().ToLowerInvariant(),
            maximumMinutes,
            allowCustomDuration = configuration.AllowCustomDuration
        });
    }

    /// <summary>
    /// Creates or replaces the current device timer.
    /// </summary>
    /// <param name="request">Timer request.</param>
    /// <returns>New timer status.</returns>
    [HttpPost("timer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<SleepTimerStatusResponse> StartTimer(
        [FromBody] StartTimerRequest request)
    {
        var context = ResolveRequestContext(request.DeviceId);
        if (context is null)
        {
            return BadRequest("Unable to identify the authenticated user and device.");
        }

        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var maximumMinutes = GetMaximumMinutes(configuration);
        if (request.DurationMinutes < 1 || request.DurationMinutes > maximumMinutes)
        {
            return BadRequest(
                $"Duration must be between 1 and {maximumMinutes} minutes.");
        }

        var action = ParseAction(request.Action, configuration.DefaultAction);
        var response = _sleepTimerService.StartTimer(
            context.Value.UserId,
            context.Value.DeviceId,
            request.DurationMinutes,
            action);

        return Ok(response);
    }

    /// <summary>
    /// Cancels the current device timer.
    /// </summary>
    /// <param name="deviceId">Client device identifier.</param>
    /// <returns>Cancellation result.</returns>
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult CancelTimer([FromQuery] string? deviceId)
    {
        var context = ResolveRequestContext(deviceId);
        if (context is null)
        {
            return BadRequest("Unable to identify the authenticated user and device.");
        }

        var cancelled = _sleepTimerService.CancelTimer(
            context.Value.UserId,
            context.Value.DeviceId);
        return Ok(new { cancelled });
    }

    /// <summary>
    /// Gets the current device timer.
    /// </summary>
    /// <param name="deviceId">Client device identifier.</param>
    /// <returns>Current status.</returns>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<SleepTimerStatusResponse> GetStatus(
        [FromQuery] string? deviceId)
    {
        var context = ResolveRequestContext(deviceId);
        if (context is null)
        {
            return BadRequest("Unable to identify the authenticated user and device.");
        }

        return Ok(
            _sleepTimerService.GetStatus(
                context.Value.UserId,
                context.Value.DeviceId));
    }

    private static int GetMaximumMinutes(PluginConfiguration configuration)
    {
        return Math.Clamp(configuration.MaximumMinutes, 1, AbsoluteMaximumMinutes);
    }

    private static int[] ParsePresets(string? value, int maximumMinutes)
    {
        var presets = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(candidate =>
                int.TryParse(candidate, out var minutes) ? minutes : 0)
            .Where(minutes => minutes >= 1 && minutes <= maximumMinutes)
            .Distinct()
            .Order()
            .Take(12)
            .ToArray();

        return presets.Length > 0 ? presets : [15, 30, 60, 120];
    }

    private static SleepTimerAction ParseAction(
        string? value,
        SleepTimerAction defaultAction)
    {
        return Enum.TryParse<SleepTimerAction>(
            value,
            ignoreCase: true,
            out var parsedAction)
            ? parsedAction
            : defaultAction;
    }

    private RequestContext? ResolveRequestContext(string? requestedDeviceId)
    {
        var userIdValue =
            User.FindFirst("Jellyfin-UserId")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            _logger.LogWarning("Sleep Timer request did not contain a valid user claim");
            return null;
        }

        var deviceId =
            User.FindFirst("Jellyfin-DeviceId")?.Value ??
            requestedDeviceId;

        deviceId = deviceId?.Trim();
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 256)
        {
            _logger.LogWarning(
                "Sleep Timer request from user {UserId} did not contain a valid device identifier",
                userId);
            return null;
        }

        return new RequestContext(userId, deviceId);
    }

    private readonly record struct RequestContext(Guid UserId, string DeviceId);
}
