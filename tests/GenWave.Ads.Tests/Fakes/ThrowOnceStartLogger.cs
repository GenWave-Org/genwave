using Microsoft.Extensions.Logging;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="ILogger{T}"/> double that throws once, from <see cref="AdSpotJobService.RunOneAsync"/>'s
/// own "Ad spot job starting" line — the first statement inside that method's own try block, so the
/// throw is what STORY-422's own start-line scenario needs: a failure RunOneAsync's own catch-all is
/// the very first thing to see (PLAN T441 ruling). Only the FIRST such call throws; every later one,
/// including a second job's own "starting" line, logs normally — proving the queue survives the job
/// whose own start line threw rather than merely never triggering a second job at all.
/// </summary>
public sealed class ThrowOnceStartLogger : ILogger<AdSpotJobService>
{
    int thrown;

    /// <summary>Whether the guarded throw has already fired — read after the fact by a spec that wants
    /// to state its own arrangement actually exercised the start line, rather than assuming it from a
    /// downstream effect alone.</summary>
    public bool Thrown => Volatile.Read(ref thrown) == 1;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (message.StartsWith(AdSpotJobService.JobStartingLogPrefix, StringComparison.Ordinal) &&
            Interlocked.Exchange(ref thrown, 1) == 0)
            throw new InvalidOperationException("simulated failure from the job's own start line");
    }
}
