namespace GenWave.Core.TalkRules;

/// <summary>Ad-only snapshot fields.</summary>
public sealed record TalkAdFields(string SponsorName, string? SponsorPhone, int SpotSeconds, bool PackOwned);
