namespace GenWave.MediaLibrary.YearLookup;

/// <summary>
/// Process-wide MusicBrainz pacing gate (SPEC F76.1): guarantees at least <see cref="MinInterval"/>
/// between the START of one request and the next, no matter how many callers race to acquire it.
///
/// Registered as a singleton (see <c>MediaLibraryServiceCollectionExtensions</c>), so every call
/// <see cref="MusicBrainzYearLookup"/> makes shares the same clock — a future second call site, or a
/// live bump to <c>Library:EnrichmentConcurrency</c>, can never race past the limit the way a
/// per-caller <c>Task.Delay</c> baked into one loop could. The gate lives here, in front of the HTTP
/// call itself, rather than in the caller that happens to drive it today.
///
/// <see cref="TimeProvider"/> is injected — never <see cref="TimeProvider.System"/> read directly —
/// so a spec can drive this gate with a fake clock and assert the 1 req/s ceiling deterministically,
/// without an actual multi-second sleep (STORY-200).
/// </summary>
public sealed class MusicBrainzRateLimiter(TimeProvider timeProvider)
{
    /// <summary>MusicBrainz's own etiquette floor: at most one request per second (SPEC F76.1).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    // A semaphore, not a plain lock, because the critical section awaits (Task.Delay) while held —
    // `lock` cannot straddle an await. This serializes "read the last start time, wait out any
    // remaining gap, stamp a new start time" as one atomic unit, so two callers racing to acquire
    // the gate can never both observe the gap as already elapsed and both proceed together.
    readonly SemaphoreSlim gate = new(1, 1);
    DateTimeOffset? lastRequestStartedAt;

    /// <summary>
    /// Waits, if necessary, until <see cref="MinInterval"/> has elapsed since the previous caller's
    /// request started, then reserves this instant as the new "last start" before returning — the
    /// caller is expected to begin its HTTP request immediately afterward.
    /// </summary>
    public async Task WaitTurnAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (lastRequestStartedAt is { } last)
            {
                var remaining = MinInterval - (timeProvider.GetUtcNow() - last);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, timeProvider, ct);
            }

            lastRequestStartedAt = timeProvider.GetUtcNow();
        }
        finally
        {
            gate.Release();
        }
    }
}
