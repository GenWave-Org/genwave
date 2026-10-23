using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>The body of POST /vote. Read with <see cref="StrictJson"/>, so an unknown member, a bad pick or a why outside <see cref="VoteReason"/> is refused.</summary>
internal sealed record VoteRequest(string PairId, string Judge, VotePick Pick, VoteReason? Why = null);
