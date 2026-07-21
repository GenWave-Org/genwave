namespace GenWave.Core.Domain;

/// <summary>
/// One card-authored pronunciation correction (SPEC F71.1, F71.7): replace <see cref="From"/> with
/// <see cref="To"/> wherever it appears in booth-bound text. Mirrors <c>GenWave.Tts.SpeechCorrection</c>'s
/// <c>{From, To}</c> shape by deliberate instruction (SPEC F71.1) rather than by shared type — this
/// project (the MIT contract surface, zero dependencies) cannot reference <c>GenWave.Tts</c>, and this
/// is the portable, exported card shape rather than the live compiled-and-matched runtime one. Station
/// corrections merge <b>over</b> card corrections at render (station wins on an identical
/// <see cref="From"/>) — only that merge seam ships this quarter (F71.7); a card's own corrections
/// arrive with import (Q4).
/// </summary>
public sealed record PersonaCorrection(string From, string To);
