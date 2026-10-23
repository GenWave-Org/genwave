namespace GenWave.VoiceOff.Listening;

/// <summary>One line of <c>votes.jsonl</c>. The file is append-only; the last line per (pairId, judge) counts.</summary>
internal sealed record Vote(string PairId, string Judge, VotePick Pick, DateTimeOffset At, VoteReason? Why = null);
