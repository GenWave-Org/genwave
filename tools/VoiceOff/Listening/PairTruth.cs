namespace GenWave.VoiceOff.Listening;

/// <summary>Which engine is behind each side of a pair. Never sent to the listening page.</summary>
internal sealed record PairTruth(string A, string B);
