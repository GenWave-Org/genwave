namespace GenWave.Core.TalkRules;

/// <summary>A line a speaker said earlier: the last 10 non-fallback lines and every line from the last hour.</summary>
public sealed record TalkRecentLine(long PersonaId, string Text, DateTimeOffset WrittenAt);
