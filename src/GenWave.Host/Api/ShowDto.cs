namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for a show row (SPEC F115.1, F115.3, F115.4): the whole identity package this ADMIN
/// surface edits — name/slug/tagline/flavor plus provenance (<c>importedFrom</c>/<c>importedAt</c>,
/// the db/25 pattern <see cref="PersonaDto"/> already carries). <see cref="Flavor"/> is prompt-only
/// and private FOREVER on every OTHER surface (SPEC F115.3, the persona-soul precedent) — but this
/// DTO backs the admin editor that AUTHORS it, so it is the one deliberate exception; the public/
/// spectator show projection (PLAN T251) is a separate, narrower DTO that never adds this field.
/// </summary>
public sealed record ShowDto(
    long Id, string Name, string Slug, string? Tagline, string? Flavor,
    string? ImportedFrom, DateTime? ImportedAt);
