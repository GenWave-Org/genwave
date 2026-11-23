namespace GenWave.Core.TalkRules;

/// <summary>
/// A line a speaker said earlier. The snapshot keeps every row any rule may want, newest first; each rule picks its
/// own window (e.g. legacy's repeat window = the speaker's newest 10 rows, then drop fallbacks; the last hour, ...) and skips <paramref name="Fallback"/> rows when it should.
/// </summary>
/// <param name="PersonaId">The persona who said it.</param>
/// <param name="Text">The line as stored.</param>
/// <param name="WrittenAt">When it was written.</param>
/// <param name="Fallback">True when it was a template line, not the speaker's own words.</param>
public sealed record TalkRecentLine(long PersonaId, string Text, DateTimeOffset WrittenAt, bool Fallback = false);
