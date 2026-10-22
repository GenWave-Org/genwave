namespace GenWave.VoiceOff.Script;

/// <summary>
/// One gauntlet line. A Crosstalk line carries <see cref="Turns"/> and no <see cref="Register"/> or
/// <see cref="Text"/>; every other class carries both and no turns. <see cref="Cues"/> is the set of
/// <c>{cue}</c> tokens present in the text.
/// </summary>
internal sealed record GauntletLine(
    string Id,
    GauntletClass Class,
    GauntletRegister? Register,
    string? Text,
    IReadOnlyList<string> Cues,
    IReadOnlyList<GauntletTurn> Turns);
