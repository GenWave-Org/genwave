namespace GenWave.Core.Domain;

/// <summary>
/// A named show row (SPEC F115.1, STORY-305, PLAN T239): the identity package — name, tagline,
/// flavor, provenance — an hour of airtime can carry, authored once via <c>IShowStore</c> and
/// referenced across the format clock. Deliberately excludes <c>persona_id</c>/<c>envelope</c> — the
/// DORMANT schedulable-bundle columns db/35 ships alongside the rest of this table (SPEC F115.2, a
/// law of this epic, not an oversight): no type or query in this epic maps, reads, or writes them: the
/// deferred bundle slice adds that seam separately.
/// </summary>
/// <param name="Tagline">
/// Public, broadcast-shaped (SPEC F115.3) — joins the F67 disclosure inventory as a pinned field.
/// <c>null</c> when the show carries none.
/// </param>
/// <param name="Flavor">
/// Prompt-only, private forever (SPEC F115.3 — the persona-soul precedent): never appears in a public
/// payload, spectator surface, or log line. <c>null</c> when the show carries none.
/// </param>
/// <param name="ImportedFrom">
/// Provenance stamp (SPEC F115.1, the F90/db-25 pattern): <c>null</c> for a show authored in place via
/// <see cref="Abstractions.IShowStore.CreateAsync"/>/<see cref="Abstractions.IShowStore.UpdateAsync"/>,
/// <c>"file"</c> for a file-uploaded import, or the catalog entry's slug for a catalog import. No
/// writer for the import path exists yet (PLAN T254) — every row this seam can itself produce carries
/// <c>null</c> here.
/// </param>
/// <param name="ImportedAt">The moment <see cref="ImportedFrom"/> was last stamped; <c>null</c> exactly
/// when <see cref="ImportedFrom"/> is.</param>
/// <param name="Slug">
/// The stored <c>station.show.slug</c> column — re-derived from <see cref="Name"/> via the house
/// Slugify (<c>LegacyPersonaCardMapper.Slugify</c>, the T68 golden-table contract) on every authored
/// create/edit, mirroring <c>Persona.Slug</c>'s own re-derive-on-every-write rule. An imported show
/// instead keeps whatever slug the import route was given (T254), same caveat as
/// <c>Persona.Slug</c>'s own remarks.
/// </param>
public sealed record Show(
    long Id,
    string Name,
    string Slug,
    string? Tagline,
    string? Flavor,
    string? ImportedFrom,
    DateTime? ImportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
