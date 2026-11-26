namespace GenWave.Core.TalkRules;

/// <summary>
/// The rules API's read side over <c>station.talk_check</c> (SPEC F220.24, F220.25): what each rule flagged. A flag is a verdict
/// recorded with the rule switched on and a real objection; a rule that could not run ("Could not check") and a shaper or recorder
/// that broke are not flags. Shadow and rules mode rows both count: a shadow flag is what the rule would have caught.
/// </summary>
public interface ITalkFlagReader
{
    /// <summary>Per rule and kind of talk, how many attempts it flagged and how many its shaper or recorder broke on (F220.9), for rows written at or after <paramref name="since"/>. Only pairs with a non-zero count come back.</summary>
    Task<IReadOnlyList<TalkRuleVerdictCount>> CountVerdictsAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>The newest attempts <paramref name="ruleId"/> flagged at or after <paramref name="since"/>, newest first, at most <paramref name="limit"/> (clamped to 1..100).</summary>
    Task<IReadOnlyList<TalkFlaggedAttempt>> LatestFlagsAsync(string ruleId, DateTimeOffset since, int limit, CancellationToken ct);
}
