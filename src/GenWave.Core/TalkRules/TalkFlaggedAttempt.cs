namespace GenWave.Core.TalkRules;

/// <summary>
/// One attempt a rule flagged. <paramref name="Dj"/> is the DJ's name (the sponsor's name for an ad when no DJ is on the row), or null
/// when neither is known; <paramref name="Words"/> is the flag's evidence.
/// </summary>
public sealed record TalkFlaggedAttempt(long CheckId, DateTimeOffset CheckedAt, string? Dj, TalkKinds Kind, string Words, TalkOutcome Outcome);
