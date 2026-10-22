using GenWave.VoiceOff.Script;
using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Pairing;

/// <summary>One pair before it has ids: the challenger, the lane it was rendered in, the line, and which side the baseline took.</summary>
internal sealed record PlannedPair(string Challenger, RenderLane Lane, GauntletLine Line, bool BaselineIsA);
