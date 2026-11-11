using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.30–.31, gh-#293) — pure ledger of a persona's taglines (quoted quirk spans of at least
/// two words, plus the card's <c>tagline</c>) against its recent utterances. No I/O, no clock.
/// </summary>
public interface ITaglineLedger
{
    /// <summary>
    /// One status per tagline of <paramref name="card"/>. Uses are read from <paramref name="recent"/> (the
    /// persona's utterances; <c>fallback</c> rows never count). <paramref name="spentShiftQuirks"/> are the
    /// rule texts of the <c>maxPerShift</c> quirks already spent this shift; their spans are unavailable.
    /// </summary>
    IReadOnlyList<TaglineStatus> Evaluate(
        PersonaCard card,
        IReadOnlyList<UtteranceEntry> recent,
        IReadOnlyList<string> spentShiftQuirks,
        DateTimeOffset now);
}
