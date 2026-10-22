using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Listening;

/// <summary>One blind pair of <c>manifest.json</c>. Exactly one side of <see cref="Truth"/> is the baseline engine.</summary>
internal sealed record ManifestPair(string PairId, string LineId, string LineClass, RenderLane Lane, string A, string B, PairTruth Truth);
