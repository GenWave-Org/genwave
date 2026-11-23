namespace GenWave.Core.TalkRules;

/// <summary>The Postgres side of the check log: <c>station.talk_check</c> (F220.29).</summary>
public interface ITalkCheckStore
{
    /// <summary>Writes one row and sweeps rows older than <c>TalkRules:CheckRetentionDays</c> in the same transaction.</summary>
    Task AppendAsync(TalkCheckRecord record, CancellationToken ct);

    /// <summary>
    /// Rows written at or after <paramref name="since"/>, oldest first. Verdicts come back sorted by rule id:
    /// a jsonb column keeps no key order, so compare verdicts as a set, not by position.
    /// Unbounded: it returns every matching row in memory. T791 (replay) must bound or stream it before calling it over a wide window.
    /// </summary>
    Task<IReadOnlyList<TalkCheckEntry>> ReadSinceAsync(DateTimeOffset since, CancellationToken ct);
}
