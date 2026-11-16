namespace GenWave.Core.Domain;

/// <summary>
/// A DJ said a signature phrase more often in one shift than the persona card allows (SPEC F218.29):
/// <see cref="Occurrences"/> uses of <see cref="Phrase"/> against a limit of <see cref="MaxPerShift"/>;
/// <see cref="LastSaidAt"/> is when the shift's most recent line was written, so the UI can name the day.
/// </summary>
public sealed record CatchphraseOveruse(
    long PersonaId, string PersonaName, string ShiftKey, string Phrase, int MaxPerShift, int Occurrences,
    DateTimeOffset LastSaidAt);
