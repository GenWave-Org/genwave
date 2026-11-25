namespace GenWave.Core.TalkRules;

/// <summary>
/// The rules API's read side over <c>station.talk_check</c> (SPEC F220.24, F220.25): what each rule flagged. A flag is a verdict
/// recorded with the rule switched on and a real objection; a rule that could not run ("Could not check") and a shaper or recorder
/// that broke are not flags. Shadow and rules mode rows both count: a shadow flag is what the rule would have caught.
/// </summary>
public interface ITalkFlagReader
{
    /// <summary>How many attempts each rule flagged per kind of talk, for rows written at or after <paramref name="since"/>. Only non-zero counts come back.</summary>
    Task<IReadOnlyList<TalkRuleFlagCount>> CountFlagsAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>The newest attempts <paramref name="ruleId"/> flagged at or after <paramref name="since"/>, newest first, at most <paramref name="limit"/> (clamped to 1..100).</summary>
    Task<IReadOnlyList<TalkFlaggedAttempt>> LatestFlagsAsync(string ruleId, DateTimeOffset since, int limit, CancellationToken ct);
}
