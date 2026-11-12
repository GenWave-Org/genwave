using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The utterance log (SPEC F218, STORY-532, gh-#293): every accepted LLM-written DJ line, raw and
/// pre-normaliser, with persona, show, kind and <see cref="ShiftKey"/>. The table is the queryable
/// truth for the repetition check; rows older than the retention window are swept at insert.
/// </summary>
public interface IUtteranceStore
{
    /// <summary>Stores <paramref name="utterance"/>, sweeps expired rows in the same transaction, returns the new id.</summary>
    Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct);

    /// <summary>Stamps <c>aired_at</c> on row <paramref name="id"/> (first stamp wins). Unknown id is a no-op.</summary>
    Task MarkAiredAsync(long id, CancellationToken ct);

    /// <summary>The persona's newest <paramref name="take"/> lines, newest first.</summary>
    Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct);

    /// <summary>
    /// The persona's lines written at or after <paramref name="since"/>, newest first, capped defensively
    /// (<see cref="MaxSinceRows"/>). Fallback rows are included; callers filter. Feeds the tagline quota window (F218.31).
    /// </summary>
    Task<IReadOnlyList<UtteranceEntry>> SinceAsync(long personaId, DateTimeOffset since, CancellationToken ct);

    /// <summary>Most rows <see cref="SinceAsync"/> returns.</summary>
    const int MaxSinceRows = 200;

    /// <summary>Deletes rows past the retention window; returns how many went.</summary>
    Task<int> SweepAsync(CancellationToken ct);
}
