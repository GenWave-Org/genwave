namespace GenWave.VoiceOff.Listening;

/// <summary>One pair of a session as the page sees it: the pair id and two clip paths under <c>session/</c>. No engine, line, lane or class.</summary>
internal sealed record SessionPair(string PairId, string A, string B);
