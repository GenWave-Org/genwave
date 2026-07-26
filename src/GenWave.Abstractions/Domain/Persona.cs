namespace GenWave.Core.Domain;

/// <summary>
/// A DJ persona row (SPEC F35.1, STORY-118): the backstory/style/voice profile a future
/// orchestrator task blends into TTS patter. <see cref="Voice"/> of <c>""</c> is a deliberate
/// sentinel meaning "use the station's own default voice" (<c>Station:Voice</c>), not "unset".
/// </summary>
/// <param name="ImportedFrom">
/// Provenance stamp (SPEC F90.7, STORY-237): <c>null</c> for a persona authored in place via the
/// CRUD endpoints, <c>"file"</c> for a file-uploaded import, or the catalog entry's slug for a
/// catalog import. Display-only — no selection/render/spectator path reads this.
/// </param>
/// <param name="ImportedAt">
/// The moment <see cref="ImportedFrom"/> was last stamped (import or re-import); <c>null</c> exactly
/// when <see cref="ImportedFrom"/> is <c>null</c>.
/// </param>
public sealed record Persona(
    long Id,
    string Name,
    string Backstory,
    string Style,
    string Voice,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ImportedFrom = null,
    DateTime? ImportedAt = null);
