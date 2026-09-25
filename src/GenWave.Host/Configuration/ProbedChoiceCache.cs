using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace GenWave.Host.Configuration;

/// <summary>
/// Caches each <see cref="IChoiceProbe"/>'s live choice list for 60 s — including failed attempts —
/// with a 2 s per-attempt timeout and single-flight de-duplication of concurrent callers (SPEC
/// F205.7b/c, STORY-479, PLAN T579). Host singleton; T580 registers it together with the concrete
/// LLM-model and TTS-voice probes, so this type alone makes no HTTP call.
///
/// <para>
/// One <see cref="Entry"/> per probe <see cref="IChoiceProbe.Name"/>, stamped with that probe's own
/// <see cref="IChoiceProbe.ScopeKey"/> at the time of the LAST attempt. Within 60 s of that attempt
/// the entry is returned unchanged, success or failure alike — a down backend costs at most one
/// timed-out call per minute, never one per caller. A ScopeKey that has changed since the cached
/// attempt is a miss even inside that window: a list fetched from a DIFFERENT server is not stale, it
/// is wrong, so the old <c>LastGood</c> is discarded rather than served (SPEC F205.7c, AC15) — see
/// <see cref="RecordAttempt"/>.
/// </para>
///
/// <para>
/// Concurrent callers past the freshness window share one in-flight refresh via a per-PROBE
/// <see cref="SemaphoreSlim"/> (double-checked against the cache, the <see cref="Catalog.CatalogProxyService"/>
/// idiom) — unlike that service's single GLOBAL gate, each probe here gets its own semaphore, so one
/// hung endpoint (LLM down) cannot delay the other's refresh (TTS voices).
/// </para>
/// </summary>
internal sealed class ProbedChoiceCache(TimeProvider timeProvider, ILogger<ProbedChoiceCache> logger)
{
    static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(60);
    static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(2);

    readonly object cacheGate = new();
    readonly Dictionary<string, Entry> entries = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> refreshGates = new();

    /// <summary>
    /// Resolves <paramref name="probe"/>'s current choice list: a fresh cached entry is served with
    /// no call at all (SPEC F205.7b); otherwise the refresh runs — de-duplicated against any other
    /// caller already refreshing the same probe — under a 2 s timeout. <paramref name="ct"/> is the
    /// CALLER's own cancellation; it is linked with, never replaced by, the internal attempt timeout,
    /// so cancelling it always surfaces as an <see cref="OperationCanceledException"/> here rather
    /// than being recorded as a failed attempt.
    /// </summary>
    public async Task<ProbedChoiceResult> GetAsync(IChoiceProbe probe, CancellationToken ct)
    {
        var scopeKey = probe.ScopeKey();

        if (TryServeFresh(probe.Name, scopeKey, out var fresh))
        {
            return fresh;
        }

        var gate = refreshGates.GetOrAdd(probe.Name, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check: another caller may have refreshed this probe while we waited for the gate.
            if (TryServeFresh(probe.Name, scopeKey, out fresh))
            {
                return fresh;
            }

            return await RefreshAsync(probe, scopeKey, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    bool TryServeFresh(string probeName, string scopeKey, [NotNullWhen(true)] out ProbedChoiceResult? result)
    {
        lock (cacheGate)
        {
            if (entries.TryGetValue(probeName, out var entry)
                && entry.ScopeKey == scopeKey
                && timeProvider.GetUtcNow() - entry.AttemptedAt < FreshFor)
            {
                result = entry.ToResult();
                return true;
            }
        }

        result = null;
        return false;
    }

    async Task<ProbedChoiceResult> RefreshAsync(IChoiceProbe probe, string scopeKey, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(AttemptTimeout, timeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        IReadOnlyList<SettingChoice>? freshChoices = null;
        try
        {
            // .WaitAsync(linkedCts.Token) on top of the token we already pass in: a probe that
            // ignores its own cancellation token (a bug in a THIRD-PARTY IChoiceProbe this cache has
            // no control over) must still return control to the caller at the 2 s/caller-cancel
            // boundary — otherwise it would hold this probe's single-flight gate open indefinitely,
            // starving every other caller of THIS probe, not just the misbehaving fetch itself.
            freshChoices = await probe.FetchAsync(linkedCts.Token).WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The CALLER cancelled — propagate, never record as a failed attempt.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Our own 2 s attempt timeout — a failed attempt.
            logger.LogWarning(ex, "Probe {ProbeName} attempt timed out ({ExceptionType})", probe.Name, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            // Any other probe fault (transport, malformed response, ...) — a failed attempt too.
            logger.LogWarning(ex, "Probe {ProbeName} attempt failed ({ExceptionType})", probe.Name, ex.GetType().Name);
        }

        return RecordAttempt(probe.Name, scopeKey, freshChoices);
    }

    ProbedChoiceResult RecordAttempt(string probeName, string scopeKey, IReadOnlyList<SettingChoice>? freshChoices)
    {
        lock (cacheGate)
        {
            entries.TryGetValue(probeName, out var existing);
            var carriedLastGood = existing is not null && existing.ScopeKey == scopeKey ? existing.LastGood : null;

            var entry = new Entry(scopeKey, freshChoices, freshChoices ?? carriedLastGood, timeProvider.GetUtcNow());
            entries[probeName] = entry;
            return entry.ToResult();
        }
    }

    /// <summary>One probe's last attempt: <see cref="FreshChoices"/> is non-null only when THAT
    /// attempt itself succeeded; <see cref="LastGood"/> is the best list known as of this attempt —
    /// this attempt's own on success, an earlier one carried forward on failure (SAME ScopeKey only),
    /// or null when nothing for this ScopeKey has ever succeeded.</summary>
    sealed record Entry(
        string ScopeKey,
        IReadOnlyList<SettingChoice>? FreshChoices,
        IReadOnlyList<SettingChoice>? LastGood,
        DateTimeOffset AttemptedAt)
    {
        public ProbedChoiceResult ToResult()
        {
            if (FreshChoices is { } choices)
            {
                return new ProbedChoiceResult.Fresh(choices);
            }

            if (LastGood is { } lastGood)
            {
                return new ProbedChoiceResult.Stale(lastGood);
            }

            return ProbedChoiceResult.Failed.Instance;
        }
    }
}
