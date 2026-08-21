namespace GenWave.Tts;

/// <summary>
/// The F139.2 rolling 24h counter store (STORY-353, PLAN T330): how many LLM calls landed on each
/// (<see cref="LlmCallCause"/>, model, <see cref="LlmCallKind"/>) combination within the last 24 hours
/// — the seam PLAN T334 reads (via <see cref="Snapshot"/> and <see cref="DominantFailure"/>) to build
/// the <c>/api/llm-calls</c> counter summary and the red health tile's "dominant recent cause" line.
/// No persistence of any kind (F139.3, F73.3/F73.4 stand):
/// this class's only dependency is <see cref="TimeProvider"/>, so a process restart clears it by
/// construction, the exact same posture <see cref="LlmCallRing"/> itself documents.
///
/// <para>
/// <b>Deliberately NOT composed inside <see cref="LlmCallRing"/>.</b> <c>Story196_LlmCallInspector</c>'s
/// own F73.3 structural proof pins that class to EXACTLY ONE constructor parameter
/// (<c>IOptionsMonitor&lt;LlmOptions&gt;</c>) as evidence it cannot persist anything — adding a second
/// dependency there would break that proof for an unrelated reason. <see cref="LlmCallRecorder"/> is
/// where the two independent observers of "a call resolved" reunite (SPEC F139 review finding F2,
/// PLAN T330) — every <see cref="LlmCopyWriter"/>/<see cref="CrosstalkScriptWriter"/>/
/// <c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c> call site feeds that ONE class, which then calls
/// both this class's own <see cref="Record"/> and <see cref="LlmCallRing.Record"/> — never one class
/// doing both jobs itself.
/// </para>
///
/// <para>
/// <b>Rolling 24h via hourly buckets, aged lazily (SPEC F139.2).</b> Each <see cref="Record"/> stamps
/// the CURRENT UTC hour's bucket for the given key; a bucket more than 24h older than "now" is dropped
/// the next time either <see cref="Record"/> or <see cref="Snapshot"/> runs, never on a background
/// timer of its own. Memory is bounded by (at most 25 live hourly buckets) × (distinct cause/model/kind
/// combinations actually seen) — never an unbounded list of raw call timestamps, and never a sweep an
/// operator has to remember exists.
///
/// <b>Precision note:</b> because entries are grouped by hour rather than by exact timestamp, the
/// true retention window is "24h to 25h", never a razor's-edge 24h+1-second cutoff — an entry recorded
/// at the very start of its own hourly bucket can be retained for up to an additional ~59 minutes
/// past a strict 24h before that bucket ages out. Acceptable for a "dominant recent cause" admin
/// surface (SPEC F139.2/F139.4); nothing here promises second-level retention precision.
/// </para>
/// </summary>
public sealed class LlmCallCauseCounters(TimeProvider timeProvider)
{
    static readonly TimeSpan RollingWindow = TimeSpan.FromHours(24);

    readonly object gate = new();

    // Hour-bucket start (UTC, truncated to the hour) -> per-(cause, model, kind) count within that
    // hour. A plain Dictionary (T330 review advisory), not a SortedDictionary: neither Record's own
    // Prune call nor Snapshot ever reads buckets in hour order — Prune filters Keys by a cutoff
    // comparison (order-independent) and Snapshot sums every bucket's Values into one unordered
    // total — so a SortedDictionary's O(log n) insert/lookup would only be paying for an ordering
    // guarantee this class never spends.
    readonly Dictionary<DateTimeOffset, Dictionary<(LlmCallCause Cause, string Model, LlmCallKind Kind), int>> buckets = new();

    /// <summary>Counts one resolved call under its current-hour bucket (SPEC F139.2). Called from
    /// <see cref="LlmCallRecorder.Record"/> — the one shared method that reunites this call with
    /// <see cref="LlmCallRing.Record"/> at every resolution point (see that class's own remarks for
    /// why this store still stays a separate singleton rather than folding into the ring itself).</summary>
    public void Record(LlmCallCause cause, string model, LlmCallKind kind)
    {
        var now = timeProvider.GetUtcNow();
        var hour = TruncateToHour(now);

        lock (gate)
        {
            if (!buckets.TryGetValue(hour, out var counts))
            {
                counts = [];
                buckets[hour] = counts;
            }

            var key = (cause, model, kind);
            counts[key] = counts.GetValueOrDefault(key) + 1;

            Prune(hour);
        }
    }

    /// <summary>Every (cause, model, kind) combination counted within the rolling 24h window, summed
    /// across hourly buckets — the read seam T334's counter summary/health tile consume. Ages out on
    /// the 24-25h hourly-bucket band this class's own remarks document (STORY-353 AC2, amended at
    /// T330 review) — never a razor's-edge 24h+1s cutoff, always over-retention rather than under.</summary>
    public IReadOnlyList<LlmCallCauseCount> Snapshot()
    {
        lock (gate)
        {
            Prune(TruncateToHour(timeProvider.GetUtcNow()));

            var totals = new Dictionary<(LlmCallCause Cause, string Model, LlmCallKind Kind), int>();
            foreach (var counts in buckets.Values)
            {
                foreach (var (key, count) in counts)
                    totals[key] = totals.GetValueOrDefault(key) + count;
            }

            return totals
                .Select(entry => new LlmCallCauseCount(entry.Key.Cause, entry.Key.Model, entry.Key.Kind, entry.Value))
                .ToList();
        }
    }

    /// <summary>
    /// The single highest-count non-<see cref="LlmCallCause.Success"/> row within the rolling 24h
    /// window, restricted to <paramref name="kind"/> (SPEC F139.2, PLAN T334) — the red health tile's
    /// "dominant recent cause" line reads directly off this, never re-deriving it from
    /// <see cref="Snapshot"/> itself. Ties (equal counts) break first by <see cref="LlmCallCause"/>'s
    /// own declaration order, then by an ordinal comparison of <see cref="LlmCallCauseCount.Model"/> —
    /// deterministic, never "whichever the dictionary happens to enumerate first".
    ///
    /// <para>
    /// <see langword="null"/> when nothing but <see cref="LlmCallCause.Success"/> (or nothing at all)
    /// was recorded for <paramref name="kind"/> within the window. Restricted to one <c>kind</c>
    /// rather than pooling Copy and Crosstalk together: the "LLM" dashboard tile this feeds
    /// (<c>GenWave.Host.Api.StatusController</c>) reflects <c>LlmCopyStatusHolder</c>'s own
    /// last-attempt verdict — a copy-writer failure, never a crosstalk one — so its explanation has to
    /// stay scoped to the SAME kind that made the tile red in the first place, or the line would name
    /// a cause the operator's own red tile was never actually about.
    /// </para>
    /// </summary>
    public LlmCallCauseCount? DominantFailure(LlmCallKind kind) =>
        Snapshot()
            .Where(row => row.Kind == kind && row.Cause != LlmCallCause.Success)
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Cause)
            .ThenBy(row => row.Model, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Drops every bucket more than <see cref="RollingWindow"/> older than <paramref name="nowHour"/>
    /// — called from inside <see cref="gate"/> by both <see cref="Record"/> and <see cref="Snapshot"/>,
    /// so a quiet counter (no calls at all) still self-trims the moment anyone reads or writes it, with
    /// no timer of its own (this class's own remarks).</summary>
    void Prune(DateTimeOffset nowHour)
    {
        var cutoff = nowHour - RollingWindow;
        var stale = buckets.Keys.Where(hour => hour < cutoff).ToList();
        foreach (var hour in stale)
            buckets.Remove(hour);
    }

    static DateTimeOffset TruncateToHour(DateTimeOffset instant) =>
        new(instant.Year, instant.Month, instant.Day, instant.Hour, minute: 0, second: 0, instant.Offset);
}
