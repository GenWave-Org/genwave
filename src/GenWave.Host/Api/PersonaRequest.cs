namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/personas</c> (create) and <c>PATCH /api/personas/{id}</c> (edit)
/// (SPEC F35.4). <see cref="Name"/> is required, non-blank; <see cref="Backstory"/>,
/// <see cref="Style"/>, and <see cref="Voice"/> are all optional and default to <c>""</c> when
/// omitted (<c>""</c> voice is the station-default sentinel — <see cref="Core.Domain.Persona.Voice"/>).
/// All fields are nullable here so the controller produces a typed 400 for a blank/missing name
/// instead of a model-binder 400.
///
/// <para>
/// <see cref="Soul"/> (gh-#256): optional direct edit of the persona card's soul text — what the
/// Admin UI submits when editing a catalog-hired DJ, whose narrative (with its embedded
/// <c>Style:</c> line) lives in the card rather than the legacy backstory/style columns. Omitted or
/// blank means "not editing the soul" — see <see cref="Core.Domain.PersonaDraft.Soul"/>.
/// </para>
///
/// <para>
/// <see cref="Voices"/> (SPEC F219.10): optional <c>{engine: voiceId}</c>, one voice per voice engine. Every key must
/// be a registry engine, else 400. A blank id clears that engine's voice (it is never stored). Saved on the card in lockstep with <see cref="Voice"/> (see
/// <see cref="Core.Domain.PersonaVoiceLockstep"/>: <see cref="Voice"/> stays the voice of the card's own engine and
/// wins over a disagreeing entry, and a blank <see cref="Voice"/> drops the entry for the card's own engine; engines
/// not named keep their voice). Omitted leaves the card's map untouched.
/// </para>
/// </summary>
public sealed record PersonaRequest(
    string? Name, string? Backstory, string? Style, string? Voice, string? Soul = null,
    IReadOnlyDictionary<string, string>? Voices = null);
