using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.1, F82.5 — the unit-pinned predicate/context matcher: every non-null
/// <see cref="TastePredicate"/> field must match the candidate (AND semantics, never OR), and the
/// rule's <see cref="TasteContext"/> must gate open for the supplied day/hour. Matching is a pure
/// function of its inputs — no clock, no I/O — so <see cref="PersonaRanker"/> is the only caller that
/// resolves "day" and "hour" from a real clock (station-local <see cref="TimeProvider"/>, SPEC F82.1).
/// </summary>
public static class TasteMatcher
{
    /// <summary>
    /// True when <paramref name="rule"/> fires for <paramref name="candidate"/> at the given
    /// day-of-week/hour.
    /// </summary>
    public static bool Matches(TasteRule rule, PersonaRankCandidate candidate, DayOfWeek day, int hour) =>
        MatchesPredicate(rule.Predicate, candidate) && MatchesContext(rule.Context, day, hour);

    static bool MatchesPredicate(TastePredicate predicate, PersonaRankCandidate candidate) =>
        MatchesField(predicate.Artist, candidate.Artist) &&
        MatchesField(predicate.Genre, candidate.Genre) &&
        MatchesTag(predicate.Tag, candidate.Moods);

    /// <summary>A null predicate field never constrains the match (SPEC F82.1); comparison is
    /// case-insensitive (SPEC F82.5).</summary>
    static bool MatchesField(string? predicateValue, string? candidateValue) =>
        predicateValue is null ||
        (candidateValue is not null && string.Equals(predicateValue, candidateValue, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tag doubles as the mood channel (SPEC F85.5) — matches if any of the candidate's
    /// moods equals the predicate's tag, case-insensitively.</summary>
    static bool MatchesTag(string? tag, IReadOnlyList<string> moods) =>
        tag is null || moods.Any(mood => string.Equals(mood, tag, StringComparison.OrdinalIgnoreCase));

    static bool MatchesContext(TasteContext context, DayOfWeek day, int hour) =>
        MatchesDay(context.DaysOfWeek, day) && MatchesHour(context.StartHour, context.EndHour, hour);

    /// <summary>Empty day list means "every day" — no day gate (SPEC F82.1). A null list gets the
    /// same meaning (gh-#87): a context of <c>{}</c> deserializes <see cref="TasteContext.DaysOfWeek"/>
    /// to null despite the record's non-nullable annotation (STJ fills constructor parameters by
    /// reflection), and the least-astonishing reading of "no day list at all" is "no day gate" —
    /// never an NRE that silently disables the rule.</summary>
    static bool MatchesDay(IReadOnlyList<DayOfWeek>? daysOfWeek, DayOfWeek day) =>
        daysOfWeek is null || daysOfWeek.Count == 0 || daysOfWeek.Contains(day);

    /// <summary>Either bound left null leaves that side open (SPEC F82.1); the window is
    /// [start, end) — end is exclusive.</summary>
    static bool MatchesHour(int? startHour, int? endHour, int hour) =>
        (startHour is null || hour >= startHour.Value) &&
        (endHour is null || hour < endHour.Value);
}
