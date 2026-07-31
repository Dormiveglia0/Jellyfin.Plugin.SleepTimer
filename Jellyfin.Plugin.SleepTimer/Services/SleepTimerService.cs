using System.Collections.Concurrent;
using Jellyfin.Plugin.SleepTimer.Models;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SleepTimer.Services;

/// <summary>
/// Executes sleep timers independently of browser throttling.
/// </summary>
public sealed class SleepTimerService : BackgroundService, ISleepTimerService
{
    private readonly ConcurrentDictionary<TimerKey, ActiveTimer> _timers = new();
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SleepTimerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SleepTimerService"/> class.
    /// </summary>
    /// <param name="sessionManager">Jellyfin session manager.</param>
    /// <param name="logger">Service logger.</param>
    public SleepTimerService(
        ISessionManager sessionManager,
        ILogger<SleepTimerService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public SleepTimerStatusResponse StartTimer(
        Guid userId,
        string deviceId,
        int durationMinutes,
        SleepTimerAction action)
    {
        var timer = new ActiveTimer(
            Guid.NewGuid(),
            userId,
            deviceId,
            durationMinutes,
            action,
            DateTimeOffset.UtcNow.AddMinutes(durationMinutes));

        _timers[TimerKey.Create(userId, deviceId)] = timer;

        _logger.LogInformation(
            "Sleep timer {TimerId} started for user {UserId} on device {DeviceId}; duration {DurationMinutes} minutes, action {Action}",
            timer.Id,
            userId,
            deviceId,
            durationMinutes,
            action);

        return ToStatus(timer);
    }

    /// <inheritdoc />
    public bool CancelTimer(Guid userId, string deviceId)
    {
        if (!_timers.TryRemove(TimerKey.Create(userId, deviceId), out var timer))
        {
            return false;
        }

        _logger.LogInformation(
            "Sleep timer {TimerId} cancelled for user {UserId} on device {DeviceId}",
            timer.Id,
            userId,
            deviceId);
        return true;
    }

    /// <inheritdoc />
    public SleepTimerStatusResponse GetStatus(Guid userId, string deviceId)
    {
        return _timers.TryGetValue(TimerKey.Create(userId, deviceId), out var timer)
            ? ToStatus(timer)
            : new SleepTimerStatusResponse { IsActive = false };
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sleep timer background service started");

        using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await periodicTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TriggerExpiredTimersAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during server shutdown.
        }

        _logger.LogInformation("Sleep timer background service stopped");
    }

    private async Task TriggerExpiredTimersAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _timers)
        {
            if (pair.Value.EndsAtUtc > now ||
                !_timers.TryRemove(pair.Key, out var expiredTimer))
            {
                continue;
            }

            await ExecuteActionAsync(expiredTimer, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteActionAsync(
        ActiveTimer timer,
        CancellationToken cancellationToken)
    {
        var matchingSessions = _sessionManager.Sessions
            .Where(session =>
                session.UserId == timer.UserId &&
                string.Equals(session.DeviceId, timer.DeviceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingSessions.Count == 0)
        {
            _logger.LogWarning(
                "Sleep timer {TimerId} expired, but no matching session was found for user {UserId} on device {DeviceId}",
                timer.Id,
                timer.UserId,
                timer.DeviceId);
            return;
        }

        var command = timer.Action == SleepTimerAction.Stop
            ? PlaystateCommand.Stop
            : PlaystateCommand.Pause;

        foreach (var session in matchingSessions)
        {
            try
            {
                await _sessionManager.SendPlaystateCommand(
                    session.Id,
                    session.Id,
                    new PlaystateRequest { Command = command },
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Sleep timer {TimerId} sent {Command} to session {SessionId}",
                    timer.Id,
                    command,
                    session.Id);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Sleep timer {TimerId} could not send {Command} to session {SessionId}",
                    timer.Id,
                    command,
                    session.Id);
            }
        }
    }

    private static SleepTimerStatusResponse ToStatus(ActiveTimer timer)
    {
        var remaining = timer.EndsAtUtc - DateTimeOffset.UtcNow;

        return new SleepTimerStatusResponse
        {
            IsActive = true,
            TimerId = timer.Id,
            DurationMinutes = timer.DurationMinutes,
            EndsAtUtc = timer.EndsAtUtc,
            RemainingSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds)),
            Action = timer.Action.ToString().ToLowerInvariant()
        };
    }

    private readonly record struct TimerKey(Guid UserId, string NormalizedDeviceId)
    {
        public static TimerKey Create(Guid userId, string deviceId)
        {
            return new TimerKey(userId, deviceId.Trim().ToUpperInvariant());
        }
    }

    private sealed record ActiveTimer(
        Guid Id,
        Guid UserId,
        string DeviceId,
        int DurationMinutes,
        SleepTimerAction Action,
        DateTimeOffset EndsAtUtc);
}
