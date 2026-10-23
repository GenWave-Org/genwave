namespace GenWave.VoiceOff.Reeling;

/// <summary>What <c>reel</c> was asked for.</summary>
internal sealed record ReelOptions(string RunId, string Root, string EnginesDirectory, string GauntletDirectory);
