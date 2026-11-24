namespace GenWave.Core.TalkRules;

/// <summary>A stored <c>talk_check</c> row read back: its id, when it was written, and the record it holds.</summary>
public sealed record TalkCheckEntry(long Id, DateTimeOffset CheckedAt, TalkCheckRecord Record);
