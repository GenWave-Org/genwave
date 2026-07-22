namespace GenWave.Core.Domain;

/// <summary>
/// Everything <see cref="Abstractions.IPersonaImportStore.ImportAsync"/> needs to write one persona
/// card transactionally (SPEC F79.3, STORY-209, PLAN T67). <paramref name="Card"/> is already
/// validated by the time this reaches the store (deserialization through
/// <see cref="PersonaCardSerializer"/> IS the validation, F79.6) — its own <see cref="PersonaCard.Lore"/>
/// and <see cref="PersonaCard.Taste"/> ARE the exact authored rows to upsert, so this request never
/// duplicates them as separate parameters.
/// </summary>
/// <param name="Slug">
/// The target persona's slug (the import route's own path segment, never a field inside the card
/// JSON — mirrors export's <c>{slug}/export</c> addressing).
/// </param>
/// <param name="LegacyVoice">
/// The flat string <see cref="Persona.Voice"/> column import resolves the card's
/// <see cref="PersonaCard.Voice"/> to for THIS station (SPEC F79.4) — <c>""</c> when the card's voice
/// doesn't resolve here (station-default sentinel, <see cref="Persona.Voice"/>'s own convention) or
/// when it does/can't-be-checked, the card's own <see cref="VoiceSpec.VoiceId"/> verbatim. Resolution
/// itself is <c>PersonaController</c>'s job (it alone holds the TTS voice-lister seam); this store
/// only persists whatever the caller already decided.
/// </param>
/// <param name="Card">
/// The card to persist as <c>station.persona.definition</c> — UNCHANGED from what deserialized (see
/// remarks above): the card's own <see cref="PersonaCard.Voice"/> is never rewritten to the resolved
/// <paramref name="LegacyVoice"/>, so a persona whose authored voice doesn't resolve on this station
/// keeps that authored intent legible on read (re-export, or a future "does this still not resolve"
/// check) rather than losing it the moment it lands here.
/// </param>
public sealed record PersonaImportRequest(string Slug, string LegacyVoice, PersonaCard Card);
