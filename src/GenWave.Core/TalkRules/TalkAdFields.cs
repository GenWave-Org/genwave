namespace GenWave.Core.TalkRules;

/// <summary>
/// Ad-only snapshot fields: the sponsor facts and the length limits the ad validator judged the script against.
/// </summary>
/// <param name="SponsorName">The sponsor's own display name; blank when none is known. An owner sponsor's literal name is allowed text for the brand check.</param>
/// <param name="SponsorPhone">The sponsor's own phone number as stored, or null. For an owner sponsor, a phone-shaped run with these digits is allowed.</param>
/// <param name="SpotSeconds">The spot's target air length.</param>
/// <param name="PackOwned">True for a pack-owned (parody) sponsor: neither the name nor the phone skip applies.</param>
/// <param name="ToleranceRatio">The live <c>Ads:DurationToleranceRatio</c>: the estimate may run to <c>SpotSeconds * (1 + ToleranceRatio)</c>.</param>
/// <param name="MaxLineChars">The per-line character budget (<c>Llm:MaxCopyChars</c>) the ad parser enforced.</param>
/// <param name="CharsPerSecond">The spoken-rate constant behind the text duration estimate (total characters / this).</param>
/// <param name="ObservedLineSeconds">Per line, the duration estimator's answer when it is grounded in a real measurement (Historical or Exact), else 0. A line's seconds are the larger of its text length / <paramref name="CharsPerSecond"/> and this value, one entry per line of <see cref="TalkSnapshot.Lines"/>.</param>
public sealed record TalkAdFields(
    string SponsorName,
    string? SponsorPhone,
    int SpotSeconds,
    bool PackOwned,
    double ToleranceRatio,
    int MaxLineChars,
    double CharsPerSecond,
    TalkList<double> ObservedLineSeconds);
