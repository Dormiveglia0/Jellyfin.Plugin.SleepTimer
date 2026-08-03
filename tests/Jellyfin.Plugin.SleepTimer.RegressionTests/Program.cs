using Jellyfin.Plugin.SleepTimer.Services;

var startedAt = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

var runningClock = new PlaybackCountdown(
    TimeSpan.FromMinutes(10),
    startedAt,
    isPlaybackActive: true);

var paused = runningClock.Advance(startedAt.AddSeconds(5), isPlaybackActive: false);
AssertEqual(595, paused.RemainingSeconds, "Elapsed playback must reduce the timer.");
AssertEqual(true, paused.IsPaused, "Pause must freeze the countdown.");

var stillPaused = runningClock.Advance(
    startedAt.AddSeconds(35),
    isPlaybackActive: false);
AssertEqual(595, stillPaused.RemainingSeconds, "Paused wall time must not reduce the timer.");

var resumed = runningClock.Advance(startedAt.AddSeconds(35), isPlaybackActive: true);
AssertEqual(595, resumed.RemainingSeconds, "Resuming must not consume paused time.");
AssertEqual(false, resumed.IsPaused, "Resume must restart the countdown.");

var afterResume = runningClock.Advance(
    startedAt.AddSeconds(40),
    isPlaybackActive: true);
AssertEqual(590, afterResume.RemainingSeconds, "Playback after resume must reduce the timer.");

var initiallyPausedClock = new PlaybackCountdown(
    TimeSpan.FromMinutes(1),
    startedAt,
    isPlaybackActive: false);
var initiallyPaused = initiallyPausedClock.Advance(
    startedAt.AddSeconds(30),
    isPlaybackActive: false);
AssertEqual(60, initiallyPaused.RemainingSeconds, "A timer started while paused must stay frozen.");

var rewind = runningClock.Advance(startedAt.AddSeconds(20), isPlaybackActive: true);
AssertEqual(590, rewind.RemainingSeconds, "A backwards clock adjustment must not add or remove time.");

var expiringClock = new PlaybackCountdown(
    TimeSpan.FromSeconds(2),
    startedAt,
    isPlaybackActive: true);
var expired = expiringClock.Advance(startedAt.AddSeconds(3), isPlaybackActive: true);
AssertEqual(true, expired.IsExpired, "Active playback must eventually expire the timer.");
AssertEqual(0, expired.RemainingSeconds, "Expired timers must clamp to zero.");

var pluginAssembly = typeof(PlaybackCountdown).Assembly;
var embeddedClient = ReadResource(
    pluginAssembly,
    "Jellyfin.Plugin.SleepTimer.Web.client.js");
var embeddedStyles = ReadResource(
    pluginAssembly,
    "Jellyfin.Plugin.SleepTimer.Web.client.css");
AssertContains(
    "clientBuildVersion = '1.3.2.0'",
    embeddedClient,
    "The packaged browser client must carry the release version.");
AssertContains(
    "return text('minutePreset', { value: minutes });",
    embeddedClient,
    "The packaged browser client must use minute-only presets.");
AssertNotContains(
    "runClientFailsafe",
    embeddedClient,
    "The packaged client must not use wall-clock expiration.");
AssertContains(
    "justify-content: center !important;",
    embeddedStyles,
    "The packaged stylesheet must center preset labels.");
AssertContains(
    "#sleepTimerPluginMinutes:focus",
    embeddedStyles,
    "The packaged stylesheet must override theme focus artifacts.");

Console.WriteLine("Playback countdown regression checks passed.");

static string ReadResource(System.Reflection.Assembly assembly, string resourceName)
{
    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Missing embedded resource {resourceName}.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static void AssertContains(string expected, string actual, string message)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Missing: {expected}");
    }
}

static void AssertNotContains(string unexpected, string actual, string message)
{
    if (actual.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Unexpected: {unexpected}");
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!actual.Equals(expected))
    {
        throw new InvalidOperationException(
            $"{message} Expected {expected}, received {actual}.");
    }
}
