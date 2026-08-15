namespace GenWave.Core.Domain;

/// <summary>
/// The show identity a schedule block can carry (SPEC F115.1, F116.1; STORY-306, PLAN T241): the
/// narrow, resolver-facing projection of <c>station.show</c> that rides
/// <see cref="ScheduleSegment.Show"/> and, from there,
/// <see cref="GenWave.Abstractions.Playout.OnAirSnapshot.Show"/>. Lives beside <see cref="ScheduleSegment"/>
/// in the published <c>GenWave.Abstractions</c> contract surface (same reason that type does — a
/// caller building against the SDK needs to see show identity riding the resolver's own snapshot),
/// NOT beside the full <c>Show</c> CRUD entity (<c>GenWave.Core.Domain.Show</c>, unpublished), which
/// this type is not.
///
/// <para>
/// Deliberately excludes every <c>station.show</c> column beyond identity/slug:
/// <c>ImportedFrom</c>/<c>ImportedAt</c>/<c>CreatedAt</c>/<c>UpdatedAt</c> are <c>Show</c>'s own CRUD
/// concern, never needed on the air. Above all, this type has NO member for the DORMANT
/// <c>persona_id</c>/<c>envelope</c> bundle columns (SPEC F115.2 — unread this epic, a law not an
/// oversight): no query that projects a row into this type can even ACCIDENTALLY carry them forward,
/// and no consumer reading <see cref="GenWave.Abstractions.Playout.OnAirSnapshot.Show"/> can observe them
/// either — the dormant-columns-unread pin is enforced by this type's own SHAPE, not merely by a
/// query that happens not to select them today.
/// </para>
///
/// <para>
/// <b><see cref="Slug"/> joined this snapshot at PLAN T285 (SPEC F127.8 review F4)</b> — the original
/// F115.1 design excluded it as "Show's own CRUD concern, never needed on the air," but
/// <c>GenWave.Orchestration.CrosstalkPlanner.IsShowEnabled</c> needs exactly this stable, rename-proof
/// identity to match <c>Crosstalk:Shows</c> against (the mutable, non-unique <see cref="Name"/> would
/// let an operator's rename silently kill banter forever — the T175 "names slugs, not labels" rule
/// <c>SettingValidator</c>'s own <c>Station:Theme</c> guard already follows). Declared as a defaulted
/// body property rather than a fifth primary-constructor parameter — this record already shipped
/// inside the Abstractions 5.0.0 NuGet with a 4-arg <c>ctor</c> and 4-arity <c>Deconstruct</c>; adding
/// a fifth positional parameter would have silently deleted both from the published binary surface.
/// The body-property shape preserves that shipped 4-arg ctor and Deconstruct exactly, so this widening
/// is genuinely additive (a minor bump, joining <see cref="SegmentKind.Crosstalk"/> on the pending
/// ledger) rather than a break. <c>ScheduleRepository</c>/<c>SpecialsRepository</c>'s own LEFT JOIN
/// against <c>station.show</c> now selects <c>slug</c> alongside name/tagline/flavor and sets it via
/// <c>with { Slug = ... }</c>/object-initializer to populate it for real.
/// </para>
/// </summary>
/// <param name="Id">The show's stable row id (<c>station.show.id</c>).</param>
/// <param name="Name">The show's display name.</param>
/// <param name="Tagline">Public, broadcast-shaped (SPEC F115.3) — safe for the spectator surface.
/// <see langword="null"/> when the show carries none.</param>
/// <param name="Flavor">Prompt-only, private forever (SPEC F115.3 — the persona-soul precedent):
/// despite riding this same internal snapshot, a consumer building a PUBLIC payload (spectator, T251)
/// must never forward this field. The same law covers log lines (F115.3's "no log line" half): this
/// record's compiler-generated <c>ToString()</c> renders <see cref="Flavor"/> verbatim, so no
/// <c>{Segment}</c>/<c>{Snapshot}</c>-style structured-log placeholder may ever bind an
/// <see cref="GenWave.Abstractions.Playout.OnAirSnapshot"/> or <see cref="ScheduleSegment"/> — or this type
/// directly — on any public-adjacent logging path; log the identity fields
/// (<see cref="Id"/>/<see cref="Name"/>) by name instead. <see langword="null"/> when the show
/// carries none.</param>
public sealed record ShowSummary(long Id, string Name, string? Tagline, string? Flavor)
{
    /// <summary>
    /// The stored <c>station.show.slug</c> column (db/35, unique/not-null) — the identity
    /// <c>Crosstalk:Shows</c> (SPEC F127.8, PLAN T285) is keyed on. Defaults to <c>""</c> for every
    /// pre-T285 construction site that never sets it — mirrors <see cref="Persona.Slug"/>'s own
    /// default-empty-string sentinel.
    /// </summary>
    public string Slug { get; init; } = "";
}
