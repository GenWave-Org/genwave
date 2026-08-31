using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F115.1, STORY-305, PLAN T239) — CRUD access to <c>station.show</c>: the named-show
/// identity package (name/tagline/flavor/provenance) an hour of airtime can carry. Deliberately never
/// maps, reads, or writes <c>persona_id</c>, or any <c>envelope</c> key beyond <c>rotation</c> (SPEC
/// F115.2 — a law of the epic, not an oversight, narrowed by exactly one field at SPEC F152.3/PLAN
/// T360 — see <see cref="SetRotationAsync"/>); a future schedulable-bundle slice adds a wider seam
/// separately. No DI registration and no consumer land with this seam — <c>/api/shows</c> (PLAN T240)
/// is the first.
/// </summary>
public interface IShowStore
{
    /// <summary>Returns every show row, ordered by name.</summary>
    Task<IReadOnlyList<Show>> GetAllAsync(CancellationToken ct);

    /// <summary>Returns the show identified by <paramref name="id"/>, or null if no such row exists.</summary>
    Task<Show?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>Returns the show identified by <paramref name="slug"/>, or null if no such row exists
    /// — the primitive a slug-addressed route (<c>/api/shows/{slug}</c>) resolves through.</summary>
    Task<Show?> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Creates a new authored show from <paramref name="draft"/>: <c>slug</c> is derived from
    /// <paramref name="draft"/>'s <c>Name</c> via the house Slugify, <c>imported_from</c>/
    /// <c>imported_at</c> stay null. Returns <see cref="ShowWriteResult.Created"/> with the new row on
    /// success, <see cref="ShowWriteResult.InvalidName"/> if <c>Name</c> is blank/whitespace-only or
    /// its Slugify output equals the fallback literal <c>"persona"</c> — whether by Slugify's own
    /// empty-slug rescue (an emoji-only name) or the ordinary path landing on that same string (e.g.
    /// the literal name <c>"Persona"</c> itself; see <see cref="ShowWriteResult.InvalidName"/>'s own
    /// remarks, PLAN T240 review A1), <see cref="ShowWriteResult.BudgetExceeded"/> if a field exceeds its SPEC F115.1 1×
    /// budget (checked before the write ever reaches Postgres), or
    /// <see cref="ShowWriteResult.SlugConflict"/> if the derived slug collides with an existing show
    /// (enforced by the DB's <c>UNIQUE(slug)</c>, not a pre-read).
    /// </summary>
    Task<ShowWriteResult> CreateAsync(ShowDraft draft, CancellationToken ct);

    /// <summary>
    /// Updates the show identified by <paramref name="id"/> with <paramref name="draft"/>'s fields —
    /// re-derives <c>slug</c> from the new <c>Name</c> the same way <see cref="CreateAsync"/> does, and
    /// never touches <c>imported_from</c>/<c>imported_at</c>. Returns <see cref="ShowWriteResult.Updated"/>
    /// with the row after the write (<c>updated_at</c> advanced) on success,
    /// <see cref="ShowWriteResult.NotFound"/> if no such show exists,
    /// <see cref="ShowWriteResult.InvalidName"/>/<see cref="ShowWriteResult.BudgetExceeded"/> the same
    /// way <see cref="CreateAsync"/> does, or <see cref="ShowWriteResult.SlugConflict"/> if another show
    /// already holds the re-derived slug.
    /// </summary>
    Task<ShowWriteResult> UpdateAsync(long id, ShowDraft draft, CancellationToken ct);

    /// <summary>
    /// Deletes the show identified by <paramref name="id"/>. Returns
    /// <see cref="ShowWriteResult.Deleted"/> on success, <see cref="ShowWriteResult.NotFound"/> if no
    /// such show exists, or <see cref="ShowWriteResult.Referenced"/> if
    /// <c>station.segment_schedule.show_id</c> still names it (the FK's own <c>ON DELETE RESTRICT</c>,
    /// caught as SQLSTATE 23503) — this seam's own case carries no detail beyond "referenced". Naming
    /// the format-clock blocks (and scoped imaging rows) a still-referenced show cannot be deleted
    /// through is the endpoint-layer guard PLAN T240 builds on top of that case.
    /// </summary>
    Task<ShowWriteResult> DeleteAsync(long id, CancellationToken ct);

    /// <summary>
    /// The import write path (SPEC F118.2, F115.5, STORY-315, PLAN T254): a single, ATOMIC conditional
    /// upsert by <paramref name="slug"/> — <c>ON CONFLICT (slug) DO UPDATE ... WHERE imported_from IS
    /// NOT NULL</c> (the gh-#394 conditional-write form), not a read-then-write pair. This is what
    /// makes the SPEC F115.5 authored-vs-imported collision gate itself race-proof: the "does the
    /// existing row belong to an import" check and the write that would overwrite it happen inside the
    /// SAME statement, so no interleaved authored write between a read and a write can ever land in
    /// the gap (contrast <see cref="Api.ShowsController.Update"/>'s own F115.5 gate, which DOES still
    /// carry the narrower gh-#394 read-then-write exposure that method's own remarks document — this
    /// method has none). Returns the row on success — a fresh insert (no existing row), or a genuine
    /// re-import (an existing row whose <c>imported_from</c> was already non-null) — or
    /// <see langword="null"/> when the conflict target's existing row is AUTHORED
    /// (<c>imported_from IS NULL</c>): the WHERE clause declines the update, so nothing was touched.
    /// <paramref name="slug"/> is the caller's own route slug, never re-derived: unlike
    /// <see cref="CreateAsync"/>/<see cref="UpdateAsync"/>, which always derive <c>slug</c> from the
    /// draft's own <c>Name</c> via the house Slugify, an import carries no name-derived slug opinion at
    /// all — <see cref="Core.Domain.Show"/>'s own remarks (a show-manifest document has no <c>slug</c>
    /// field, mirroring <c>PersonaCard</c>, not <c>ThemeManifest</c>).
    ///
    /// <para>
    /// <paramref name="tagline"/>/<paramref name="flavor"/> collapse blank/whitespace-only to
    /// <see langword="null"/> the same way <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> already
    /// do (<see cref="Core.Domain.Show"/>'s own "null when the show carries none" contract) — this
    /// method's caller (<see cref="Api.ShowsController.Import"/>) hands the manifest's raw, possibly-
    /// empty strings straight through rather than pre-normalizing them itself.
    /// </para>
    ///
    /// <para>
    /// Callers own EVERY other validation this method performs none of — route-slug shape/reservation
    /// and the SPEC F115.1 2× import budget ceiling both run BEFORE this is ever called
    /// (<see cref="Api.ShowsController.Import"/>'s own remarks name the full gate order) — mirrors
    /// <c>IThemeStore.UpsertAsync</c>'s own "pure persistence, no validation" posture, narrowed only by
    /// the ONE gate (authored-vs-imported) this method itself must own to make it atomic.
    /// </para>
    ///
    /// <para>
    /// <b><paramref name="rotation"/> (SPEC F152.6, PLAN T363) — "no opinion," NOT <see cref="SetRotationAsync"/>'s
    /// own "clear the rule."</b> <see langword="null"/> means the manifest carried no rotation opinion
    /// at all (a 1.0 manifest with no <c>envelope</c>, a present <c>envelope</c> with no <c>rotation</c>
    /// key, or an explicit <c>envelope.rotation: null</c> — <c>GenWave.Host.Shows.ShowManifest</c>'s own
    /// three collapsed cases, that type's own remarks) — an EXISTING show's own rotation rule, if any,
    /// is left completely untouched by this write, on BOTH the insert and re-import branches. This is
    /// the one place <see cref="RotationPredicate"/>'s nullability means something DIFFERENT from every
    /// other seam on this interface: <see cref="SetRotationAsync"/>'s own <see langword="null"/> REMOVES
    /// an existing rule; this parameter's <see langword="null"/> never removes anything, it simply never
    /// writes the <c>rotation</c> key at all. A non-null <paramref name="rotation"/> (already validated
    /// by the caller — <c>GenWave.Host.Shows.ShowManifestParser</c>'s own SPEC F152.1/F152.5 bound
    /// checks) MERGES into <c>envelope</c> the identical way <see cref="SetRotationAsync"/> does,
    /// preserving every dormant sibling <c>envelope</c> key.
    /// </para>
    /// </summary>
    Task<Show?> ImportAsync(
        string slug, string name, string? tagline, string? flavor, string importedFrom,
        RotationPredicate? rotation, CancellationToken ct);

    /// <summary>
    /// Persists <paramref name="rotation"/> into <c>station.show.envelope</c>'s <c>rotation</c> key
    /// (SPEC F152.3, F152.5, STORY-372, PLAN T360) — the ONE write this seam performs against the
    /// otherwise-dormant <c>envelope</c> column. <paramref name="rotation"/> non-null MERGES a
    /// <c>{"rotation": {...}}</c> fragment into whatever <c>envelope</c> already holds (jsonb
    /// <c>||</c>) — every sibling key survives untouched, never a whole-document overwrite;
    /// <see langword="null"/> REMOVES the <c>rotation</c> key instead (jsonb <c>-</c>), same sibling
    /// guarantee. Returns <see cref="ShowWriteResult.Updated"/> with the row after the write
    /// (<c>updated_at</c> advanced) on success, or <see cref="ShowWriteResult.NotFound"/> if no such
    /// show exists. No other <see cref="ShowWriteResult"/> case applies — this method performs no
    /// name/slug/budget validation of its own (<paramref name="rotation"/>'s own bound/shape gate is
    /// PLAN T362's endpoint-layer concern, mirroring how <see cref="CreateAsync"/>/<see cref="UpdateAsync"/>'s
    /// own budget gates stay app-seam, not store-seam, for every OTHER field).
    /// </summary>
    Task<ShowWriteResult> SetRotationAsync(long id, RotationPredicate? rotation, CancellationToken ct);

    /// <summary>
    /// Raised after a successful <see cref="SetRotationAsync"/>, <see cref="UpdateAsync"/>, or
    /// <see cref="ImportAsync"/> write (PLAN T363 extends the T360 review HIGH-1 fix to the import path)
    /// — <see cref="Domain.ShowSummary"/> (the resolver-facing projection
    /// <c>ScheduleRepository</c>/<c>SpecialsRepository</c> join at LOAD time, never a per-tick lookup)
    /// carries <c>Name</c>/<c>Tagline</c>/<c>Flavor</c>/<c>Rotation</c> — every one of them now an
    /// operator-editable, behavioral field an already-cached <c>CachingScheduleResolver</c> snapshot
    /// can silently go stale against, the same way <see cref="IScheduleStore.WeekChanged"/> already
    /// guards <c>segment_schedule</c> writes. Mirrors that event's own contract exactly: never raised
    /// when the write is rejected (<see cref="ShowWriteResult.NotFound"/>, <see cref="ShowWriteResult.InvalidName"/>,
    /// <see cref="ShowWriteResult.BudgetExceeded"/>, <see cref="ShowWriteResult.SlugConflict"/>, or —
    /// <see cref="ImportAsync"/>'s own case — a declined authored-collision upsert that returns
    /// <see langword="null"/>), and carries no payload — a subscriber (<c>CachingScheduleResolver</c>)
    /// only ever needs to know "the cached snapshot may be stale," never which show or which field
    /// changed. <see cref="CreateAsync"/> deliberately does NOT raise it: a brand-new show cannot yet be
    /// referenced by any cached snapshot, so there is nothing for an existing cache to go stale against.
    /// <see cref="ImportAsync"/> is NOT the same case, and DOES raise it unconditionally on every
    /// successful upsert (fresh insert or re-import alike) — an import can rewrite name/tagline/flavor
    /// AND, as of SPEC F152.6/PLAN T363, the rotation rule on an EXISTING show, so the same staleness
    /// this event already guards for <see cref="UpdateAsync"/>/<see cref="SetRotationAsync"/> applies
    /// identically to a re-import; raising it unconditionally (rather than only distinguishing "fresh
    /// insert" from "re-import") keeps this one call site simple and never under-fires. Neither does
    /// <see cref="DeleteAsync"/> raise it (station.segment_schedule.show_id's own ON DELETE RESTRICT
    /// already makes deleting a referenced show impossible).
    /// </summary>
    event Action? ShowChanged;
}
