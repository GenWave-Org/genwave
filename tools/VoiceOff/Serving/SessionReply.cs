using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>The answer to GET /session: the cap and this judge's pairs in this judge's order.</summary>
internal sealed record SessionReply(int Cap, IReadOnlyList<SessionPair> Pairs);
