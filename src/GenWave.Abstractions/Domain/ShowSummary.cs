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
/// Deliberately excludes every <c>station.show</c> column beyond identity: <c>Slug</c>/
/// <c>ImportedFrom</c>/<c>ImportedAt</c>/<c>CreatedAt</c>/<c>UpdatedAt</c> are <c>Show</c>'s own CRUD
/// concern, never needed on the air. Above all, this type has NO member for the DORMANT
/// <c>persona_id</c>/<c>envelope</c> bundle columns (SPEC F115.2 — unread this epic, a law not an
/// oversight): no query that projects a row into this type can even ACCIDENTALLY carry them forward,
/// and no consumer reading <see cref="GenWave.Abstractions.Playout.OnAirSnapshot.Show"/> can observe them
/// either — the dormant-columns-unread pin is enforced by this type's own SHAPE, not merely by a
/// query that happens not to select them today.
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
public sealed record ShowSummary(long Id, string Name, string? Tagline, string? Flavor);
