namespace GenWave.Core.TalkRules;

/// <summary>The Postgres side of the check log: <c>station.talk_check</c> (F220.29).</summary>
public interface ITalkCheckStore
{
    /// <summary>Writes one row and sweeps rows older than <c>TalkRules:CheckRetentionDays</c> in the same transaction.</summary>
    Task AppendAsync(TalkCheckRecord record, CancellationToken ct);

    /// <summary>
    /// Rows written at or after <paramref name="since"/>, oldest first. Verdicts come back sorted by rule id:
    /// a jsonb column keeps no key order, so compare verdicts as a set, not by position.
    /// Unbounded: it returns every matching row in memory, so it is for a known-small window (specs, a single day).
    /// Anything over a wide window (the replay) pages with <see cref="ReadPageAsync"/>.
    /// </summary>
    Task<IReadOnlyList<TalkCheckEntry>> ReadSinceAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// One keyset page: rows written at or after <paramref name="since"/> whose id is greater than
    /// <paramref name="afterId"/>, in id order, at most <paramref name="limit"/> of them (clamped to 1..<see cref="MaxPageSize"/>).
    /// Start with an <paramref name="afterId"/> of 0 and pass the last id of each page back; an empty page is the end.
    /// </summary>
    Task<IReadOnlyList<TalkCheckEntry>> ReadPageAsync(DateTimeOffset since, long afterId, int limit, CancellationToken ct);

    /// <summary>The most rows one <see cref="ReadPageAsync"/> returns.</summary>
    const int MaxPageSize = 1000;
}
