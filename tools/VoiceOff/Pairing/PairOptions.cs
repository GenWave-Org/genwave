namespace GenWave.VoiceOff.Pairing;

/// <summary>What <c>pair</c> was asked for. <paramref name="Seed"/> is null to draw one at random.</summary>
internal sealed record PairOptions(string RunId, string Root, string EnginesDirectory, string GauntletDirectory, int? Seed);
