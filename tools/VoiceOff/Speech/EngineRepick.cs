using GenWave.VoiceOff.Script;

namespace GenWave.VoiceOff.Speech;

/// <summary>The one optional second choice of a register's voice, with why (SPEC F214.5). It replaces the first choice.</summary>
internal sealed record EngineRepick(GauntletRegister Register, string Voice, string Reason);
