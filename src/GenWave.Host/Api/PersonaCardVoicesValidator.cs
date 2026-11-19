using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// Import-boundary check for <see cref="PersonaCard.Voices"/> (SPEC F219.7). An imported card is never
/// rewritten (byte-stable round trip), so a map that breaks the lockstep with <see cref="PersonaCard.Voice"/>
/// is refused with a clear message.
/// </summary>
internal static class PersonaCardVoicesValidator
{
    /// <summary>
    /// The first problem in a voice map's entries alone (no card to agree with): a blank engine name, a blank voice id,
    /// or one engine named twice. One rule for engine names (T751 review 2): lookups ignore case, so a map that names
    /// one engine twice by case alone is refused, and the lockstep check ignores case too. Shared by the import check
    /// and the persona write (<c>PersonaRequest.Voices</c>). A persona write passes <paramref name="allowBlankIds"/>: there
    /// a blank id means "clear this engine's voice" (SPEC F219.10); a card on disk never holds one.
    /// </summary>
    internal static string? FirstEntryProblem(IReadOnlyDictionary<string, string> voices, bool allowBlankIds = false)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (engine, voiceId) in voices)
        {
            if (string.IsNullOrWhiteSpace(engine))
                return "Every voices entry needs an engine name.";
            if (!allowBlankIds && string.IsNullOrWhiteSpace(voiceId))
                return "Every voices entry needs a voice id.";
            if (!seen.Add(engine))
                return "Voices names an engine twice (engine names ignore case).";
        }

        return null;
    }

    /// <summary>The first problem found in <paramref name="card"/>'s voice map, or <see langword="null"/> when it is valid or absent.</summary>
    internal static string? FirstProblem(PersonaCard card)
    {
        if (card.Voices is not { } voices)
            return null;

        if (FirstEntryProblem(voices) is { } entryProblem)
            return entryProblem;

        // Nothing to agree with when the card has no voice of its own; the legacy empty engine name means kokoro,
        // the same fold VoiceMap and the write path (PersonaCard.WithVoice) use.
        if (string.IsNullOrWhiteSpace(card.Voice.VoiceId))
            return null;

        var ownEngine = PersonaCardEngine.Of(card);
        var voiceIdForEngine = VoiceMap.FromEngineVoices(voices).VoiceFor(ownEngine);
        if (voiceIdForEngine is null)
            return "Voices must include the engine of the card's voice.";
        if (!string.Equals(voiceIdForEngine, card.Voice.VoiceId, StringComparison.Ordinal))
            return "The voices entry for the card's voice engine must match the card's voice id.";

        return null;
    }
}
