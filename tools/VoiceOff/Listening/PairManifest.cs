namespace GenWave.VoiceOff.Listening;

/// <summary><c>manifest.json</c>, written by <c>pair</c>; <c>reels</c> may be empty.</summary>
internal sealed record PairManifest(string RunId, string GauntletHash, IReadOnlyList<ManifestPair> Pairs, IReadOnlyList<ManifestReel> Reels);
