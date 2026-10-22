using GenWave.VoiceOff.Script;

namespace GenWave.VoiceOff.Speech;

/// <summary>One finished render of one gauntlet line in one lane.</summary>
internal sealed record RenderRecord(GauntletRegister Register, string LineId, RenderLane Lane, string Voice, string AudioPath);
