namespace GenWave.Core.Domain;

/// <summary>
/// Folds an edit's per-engine voices into a persona card (SPEC F219.7/F219.10), shared by create and update.
/// The lockstep rule itself lives in one place, <see cref="PersonaCard.WithVoice"/> (own engine, legacy empty name
/// = kokoro, <c>Voice</c> wins, no blank ids stored); this only overlays the given entries first. A blank id in the
/// given entries clears that engine's voice.
/// When <see cref="PersonaDraft.Voices"/> is omitted the card is returned untouched.
/// </summary>
public static class PersonaVoiceLockstep
{
    /// <summary><paramref name="card"/> already carries the draft's voice id; this adds the map half.</summary>
    public static PersonaCard Apply(PersonaCard card, PersonaDraft draft)
    {
        if (draft.Voices is not { } given)
            return card;

        // Fold the existing map first (legacy empty engine name to kokoro, no blank ids), so a given entry meets the
        // key it replaces or clears.
        var existing = (card with { Voices = card.VoicesByEngine() }).WithVoice(card.Voice).Voices
            ?? new Dictionary<string, string>();
        var overlaid = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var (engine, voiceId) in given)
            overlaid[engine] = voiceId;

        return (card with { Voices = overlaid }).WithVoice(card.Voice);
    }
}
