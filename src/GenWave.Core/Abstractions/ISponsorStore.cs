using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The store behind <c>station.sponsor</c> (SPEC F171; STORY-406, STORY-407, STORY-410; PLAN T432) —
/// owner-authored and pack-installed sponsors share the one table, split by <c>Sponsor.PackSlug</c>
/// (<see langword="null"/> = owner-authored). Every write returns a discriminated outcome
/// (<see cref="SponsorWriteResult"/>/<see cref="SponsorDeleteResult"/>) rather than throwing for an
/// expected, recoverable condition — the <c>IAdSpotStore</c>/<c>IShowStore</c> precedent.
/// </summary>
public interface ISponsorStore
{
    /// <summary>Lists every sponsor, ordered by name. When <paramref name="q"/> is non-null, only
    /// sponsors whose folded name (<c>station.sponsor_fold</c>) contains the folded
    /// <paramref name="q"/> as a substring are returned — the fold collapses whitespace/case, it does
    /// not tokenize, so word order in <paramref name="q"/> matters. Each row carries its own
    /// referencing counts (SPEC F172), computed alongside the list rather than per-row.</summary>
    Task<IReadOnlyList<SponsorListRow>> ListAsync(string? q, CancellationToken cancellationToken);

    /// <summary>Reads one sponsor by id, or <see langword="null"/> when none exists.</summary>
    Task<Sponsor?> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>Creates a new owner-authored sponsor (<c>PackSlug</c> is always <see langword="null"/>
    /// on the result). Returns <see cref="SponsorWriteResult.NameTaken"/> when the folded name already
    /// exists in the owner namespace, or <see cref="SponsorWriteResult.InvalidField"/> when a db/46
    /// <c>CHECK</c> rejects one field's shape.</summary>
    Task<SponsorWriteResult> CreateOwnerAsync(NewSponsor sponsor, CancellationToken cancellationToken);

    /// <summary>Idempotent batch find-or-create for every pack-installed sponsor a manifest declares
    /// (SPEC F171.1/F2/F3; round-3 finding R1), keyed on (<paramref name="packSlug"/>, folded name) —
    /// calling this twice with the same <paramref name="packSlug"/>/<paramref name="names"/> returns
    /// the same rows both times (an ad-pack install/re-install, <c>AdPackController</c>'s own
    /// caller). ONE round trip: every fold is computed together, a collision between two DIFFERENT
    /// <paramref name="names"/> is refused BEFORE any row is written
    /// (<see cref="SponsorPackWriteResult.NamesCollide"/> — never a partial install of the other
    /// brands in the same manifest), and every non-colliding name is then upserted inside a single
    /// transaction by (<paramref name="packSlug"/>, folded name). Never fails on a name collision
    /// AGAINST an existing row in the pack's own namespace; it upserts into it — only a collision
    /// BETWEEN two of THIS call's own <paramref name="names"/> is refused.</summary>
    Task<SponsorPackWriteResult> UpsertPackSponsorsAsync(
        string packSlug, IReadOnlyList<string> names, CancellationToken cancellationToken);

    /// <summary>Idempotent find-or-create for the ONE owner-authored sponsor named
    /// <paramref name="name"/> (<c>PackSlug IS NULL</c>) — an ad/brief create-or-edit's own "resolve
    /// the sponsor the caller typed" step (PLAN T434/T435, replacing the former
    /// <c>ResolveOwnerSponsorAsync</c> controller-level helper). Exact lookup by folded name first; a
    /// miss inserts, and a concurrent insert race (two callers resolving the same brand-new name at
    /// once) is resolved by re-selecting the winner rather than surfacing the race as an error. Only
    /// ever returns <see cref="SponsorWriteResult.Ok"/> (found or created) or
    /// <see cref="SponsorWriteResult.InvalidField"/> (a db/46 <c>CHECK</c> rejects
    /// <paramref name="name"/>'s own shape) — never <c>NameTaken</c>/<c>NamePackOwned</c>/
    /// <c>NotFound</c>/<c>VersionConflict</c>, which only apply to <see cref="CreateOwnerAsync"/>/
    /// <see cref="UpdateAsync"/>'s own sparse-edit semantics, not a plain find-or-create.</summary>
    Task<SponsorWriteResult> FindOrCreateOwnerAsync(string name, CancellationToken cancellationToken);

    /// <summary>Applies a sparse edit (SPEC F171.1) under an optimistic-concurrency guard. Every
    /// outcome: <see cref="SponsorWriteResult.Ok"/> (the row after the write);
    /// <see cref="SponsorWriteResult.NotFound"/> (no sponsor with this id);
    /// <see cref="SponsorWriteResult.NamePackOwned"/> (<c>SponsorEdit.Name</c> is non-null and
    /// <c>Sponsor.PackSlug</c> is non-null — refused before the version is even checked: only the
    /// installing pack, via <see cref="UpsertPackSponsorsAsync"/>, may rename a pack sponsor);
    /// <see cref="SponsorWriteResult.VersionConflict"/> (<paramref name="expectedVersion"/> no longer
    /// matches the row's current <c>Sponsor.Version</c>); <see cref="SponsorWriteResult.NameTaken"/>
    /// (the new folded name collides with another owner sponsor); <see cref="SponsorWriteResult.InvalidField"/>
    /// (a db/46 <c>CHECK</c> rejects one edited field's shape).</summary>
    Task<SponsorWriteResult> UpdateAsync(
        long id, SponsorEdit edit, string expectedVersion, CancellationToken cancellationToken);

    /// <summary>Sets — or idempotently re-affirms — <c>Sponsor.Paused</c>. Stamps
    /// <c>Sponsor.PausedAt</c> the first time a sponsor is paused, leaves it unchanged on a repeat
    /// pause, and clears it back to <see langword="null"/> on resume. Returns the row after the write,
    /// or <see langword="null"/> when no sponsor with the requested id exists.</summary>
    Task<Sponsor?> SetPausedAsync(long id, bool paused, CancellationToken cancellationToken);

    /// <summary>Deletes a sponsor only when nothing still names it. Returns
    /// <see cref="SponsorDeleteResult.InUse"/> — carrying the referencing counts and up to ten
    /// referencing titles (STORY-410 AC2) — when at least one <c>station.ad_brief</c>,
    /// <c>station.ad_spot</c>, or <c>station.show</c> row still points at it.</summary>
    Task<SponsorDeleteResult> DeleteIfUnreferencedAsync(long id, CancellationToken cancellationToken);
}
