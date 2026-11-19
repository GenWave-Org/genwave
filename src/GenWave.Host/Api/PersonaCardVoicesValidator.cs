using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Import-boundary check for <see cref="PersonaCard.Voices"/> (SPEC F219.7). An imported card is never
/// rewritten (byte-stable round trip), so a map that breaks the lockstep with <see cref="PersonaCard.Voice"/>
/// is refused with a clear message.
/// </summary>
internal static class PersonaCardVoicesValidator
{
    /// <summary>The first problem found in <paramref name="card"/>'s voice map, or <see langword="null"/> when it is valid or absent.</summary>
    internal static string? FirstProblem(PersonaCard card)
    {
        if (card.Voices is not { } voices)
            return null;

        // One rule for engine names (T751 review 2): lookups ignore case, so a map that names one engine twice
        // by case alone is refused here, and the lockstep check below ignores case too.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (engine, voiceId) in voices)
        {
            if (string.IsNullOrWhiteSpace(engine))
                return "Every voices entry needs an engine name.";
            if (string.IsNullOrWhiteSpace(voiceId))
                return "Every voices entry needs a voice id.";
            if (!seen.Add(engine))
                return "Voices names an engine twice (engine names ignore case).";
        }

        var voiceIdForEngine = voices.FirstOrDefault(v => string.Equals(v.Key, card.Voice.Engine, StringComparison.OrdinalIgnoreCase)).Value;
        if (voiceIdForEngine is null)
            return "Voices must include the engine of the card's voice.";
        if (!string.Equals(voiceIdForEngine, card.Voice.VoiceId, StringComparison.Ordinal))
            return "The voices entry for the card's voice engine must match the card's voice id.";

        return null;
    }
}
