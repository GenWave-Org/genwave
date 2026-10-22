namespace GenWave.VoiceOff.Script;

/// <summary>One speaker's turn in a Crosstalk exchange.</summary>
internal sealed record GauntletTurn(GauntletRegister Register, string Text);
