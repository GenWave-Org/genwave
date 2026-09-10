using Microsoft.Extensions.Logging;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="ILogger{T}"/> double that parks <see cref="AdSpotJobService.RunOneAsync"/>'s own "Ad
/// spot job starting" line for one chosen spot id until a spec calls <see cref="Release"/> — the
/// deterministic reproduction for a <c>CancelAsync</c> landing on that job while it is still inside its
/// own catch-all, at the very first statement that catch-all covers (STORY-422, PLAN T441 ruling). The
/// block happens synchronously inside <see cref="Log{TState}"/>, on whatever thread called it (the
/// service's own consumer thread, never the test thread), so <c>.GetAwaiter().GetResult()</c> here is
/// the race-reproduction mechanism itself, not a hot-path violation.
///
/// <para>
/// Also counts every entry logged at <see cref="LogLevel.Warning"/> or above — the discriminator
/// between a job whose own cancellation is handled silently by its own catch clause (zero warnings)
/// and one that instead escapes to <see cref="AdSpotJobService.ExecuteAsync"/>'s own outer catch,
/// which always logs one (PLAN T441 ruling).
/// </para>
/// </summary>
public sealed class GatedStartLogger : ILogger<AdSpotJobService>
{
    readonly string gatedOnSpotId;
    readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly object gate = new();
    int warningCount;

    public GatedStartLogger(long gatedOnSpotId) => this.gatedOnSpotId = $"spotId={gatedOnSpotId} ";

    /// <summary>Whether the gated job's own "starting" line has been logged and is parked, waiting on
    /// <see cref="Release"/> — a plain <see cref="Task.IsCompleted"/> read, safe from any thread. A
    /// spec polls this through a bounded wait rather than awaiting a raw <see cref="Task"/> directly, so
    /// a wording drift that stops this double matching <see cref="AdSpotJobService"/>'s own log line
    /// fails as a timed-out assertion, never an indefinite hang.</summary>
    public bool HasStarted => started.Task.IsCompleted;

    /// <summary>How many <see cref="LogLevel.Warning"/>-or-above entries have been logged so far —
    /// read under the same <see cref="gate"/> the write side locks, since the service's own consumer
    /// thread and a spec's own polling thread both touch this concurrently.</summary>
    public int WarningCount
    {
        get { lock (gate) return warningCount; }
    }

    /// <summary>Unparks the gated job. Safe to call more than once; only the first call matters.</summary>
    public void Release() => release.TrySetResult();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        if (message.StartsWith(AdSpotJobService.JobStartingLogPrefix, StringComparison.Ordinal) &&
            message.Contains(gatedOnSpotId, StringComparison.Ordinal))
        {
            started.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return;
        }

        if (logLevel >= LogLevel.Warning)
            lock (gate) warningCount++;
    }
}
