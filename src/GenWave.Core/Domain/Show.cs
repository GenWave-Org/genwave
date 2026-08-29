using GenWave.Abstractions.Playout;

namespace GenWave.Core.Domain;

/// <summary>
/// A named show row (SPEC F115.1, STORY-305, PLAN T239): the identity package — name, tagline,
/// flavor, provenance — an hour of airtime can carry, authored once via <c>IShowStore</c> and
/// referenced across the format clock. Deliberately excludes <c>persona_id</c> — the DORMANT
/// schedulable-bundle column db/35 ships alongside the rest of this table (SPEC F115.2, a law of this
/// epic, not an oversight): no type or query in this epic maps, reads, or writes it. <c>envelope</c>
/// itself stays dormant too, EXCEPT for the one field <see cref="Rotation"/> now reads (SPEC F152.3,
/// STORY-372, PLAN T360) — see that member's own remarks.
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
/// <c>"file"</c> for a file-uploaded import, or the catalog entry's slug for a catalog import
/// (<see cref="Abstractions.IShowStore.ImportAsync"/>, PLAN T254).
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
/// <param name="Rotation">
/// The show's own "deep cuts" rotation rule (SPEC F152.1, F152.3, STORY-372, PLAN T360) — read from
/// <c>station.show.envelope</c>'s <c>rotation</c> key ONLY, the single field this epic wakes on an
/// otherwise still-dormant column (every other <c>envelope</c> key, and <c>persona_id</c>, stay
/// unread — SPEC F115.2 holds for everything else). <c>null</c> for a show with no rotation rule,
/// an absent/JSON-null <c>rotation</c> key, an empty object, both members explicitly null, or a
/// malformed value (the store logs one WARN and treats it as none — never throws mid-airing, F152.4).
/// A trailing, DEFAULTED positional parameter (not a non-positional <c>init</c> property) — every
/// pre-T360 <c>new Show(...)</c>/<c>with</c> call site across this repo passes exactly nine
/// arguments, so this stays additive rather than forcing every call site to update.
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
    DateTime UpdatedAt,
    RotationPredicate? Rotation = null);
