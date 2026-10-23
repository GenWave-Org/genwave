namespace GenWave.VoiceOff.Pairing;

/// <summary>The pairs that can be built, and how many were left out because a clip was missing or failed.</summary>
internal sealed record PairPlan(IReadOnlyList<PlannedPair> Pairs, int Skipped);
