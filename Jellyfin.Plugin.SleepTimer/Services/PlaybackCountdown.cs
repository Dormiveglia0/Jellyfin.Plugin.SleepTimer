namespace Jellyfin.Plugin.SleepTimer.Services;

/// <summary>
/// Tracks remaining playback time while excluding periods when playback is paused.
/// </summary>
internal sealed class PlaybackCountdown
{
    private readonly object _syncRoot = new();
    private TimeSpan _remaining;
    private DateTimeOffset _lastObservedAtUtc;
    private bool _isPaused;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackCountdown"/> class.
    /// </summary>
    /// <param name="duration">Initial playback duration.</param>
    /// <param name="startedAtUtc">UTC time at which the timer was created.</param>
    /// <param name="isPlaybackActive">Whether playback is currently running.</param>
    public PlaybackCountdown(
        TimeSpan duration,
        DateTimeOffset startedAtUtc,
        bool isPlaybackActive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        _remaining = duration;
        _lastObservedAtUtc = startedAtUtc;
        _isPaused = !isPlaybackActive;
    }

    /// <summary>
    /// Advances the clock to a new observation time.
    /// </summary>
    /// <param name="observedAtUtc">Current UTC time.</param>
    /// <param name="isPlaybackActive">Whether playback is currently running.</param>
    /// <returns>The updated countdown snapshot.</returns>
    public CountdownSnapshot Advance(
        DateTimeOffset observedAtUtc,
        bool isPlaybackActive)
    {
        lock (_syncRoot)
        {
            if (observedAtUtc > _lastObservedAtUtc)
            {
                var elapsed = observedAtUtc - _lastObservedAtUtc;
                if (!_isPaused)
                {
                    _remaining = _remaining > elapsed
                        ? _remaining - elapsed
                        : TimeSpan.Zero;
                }

                _lastObservedAtUtc = observedAtUtc;
            }

            _isPaused = !isPlaybackActive;
            return CreateSnapshot(_lastObservedAtUtc);
        }
    }

    /// <summary>
    /// Reads the current countdown state without advancing it.
    /// </summary>
    /// <returns>The current countdown snapshot.</returns>
    public CountdownSnapshot Read()
    {
        lock (_syncRoot)
        {
            return CreateSnapshot(_lastObservedAtUtc);
        }
    }

    private CountdownSnapshot CreateSnapshot(DateTimeOffset observedAtUtc)
    {
        return new CountdownSnapshot(
            _remaining,
            _isPaused,
            observedAtUtc.Add(_remaining));
    }
}

/// <summary>
/// Immutable view of a playback countdown.
/// </summary>
/// <param name="Remaining">Remaining playback time.</param>
/// <param name="IsPaused">Whether the countdown is paused.</param>
/// <param name="ProjectedEndsAtUtc">Projected end time if playback continues.</param>
internal readonly record struct CountdownSnapshot(
    TimeSpan Remaining,
    bool IsPaused,
    DateTimeOffset ProjectedEndsAtUtc)
{
    /// <summary>
    /// Gets a value indicating whether the countdown has expired.
    /// </summary>
    public bool IsExpired => Remaining <= TimeSpan.Zero;

    /// <summary>
    /// Gets the remaining whole seconds, rounded up for display.
    /// </summary>
    public int RemainingSeconds =>
        Math.Max(0, (int)Math.Ceiling(Remaining.TotalSeconds));
}
