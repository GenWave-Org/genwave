using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The <c>station.ad_brief</c> seam (SPEC F159.1, F162.2, F171.6; STORY-389, STORY-392, STORY-406;
/// PLAN T398, T403b, T405, T432) — deliberately narrow, exactly <see cref="IAdSpotStore"/>'s own
/// "Core-level port a MediaLibrary repository implements directly" placement, one table over.
/// <see cref="UpsertAsync"/>/<see cref="SampleEnabledAsync"/> shipped with T398 (STORY-389 AC1's own
/// upsert fact, T400's own prompt sampler); <see cref="ListAllAsync"/>/<see cref="CreateOwnerAsync"/>/
/// <see cref="SetEnabledAsync"/> widen the seam additively for T403b's Briefs admin surface (SPEC
/// F162.1); <see cref="UpsertAllAsync"/> widens it again for T405's ad-pack install (SPEC F162.2);
/// PLAN T432 retargets every brand-keyed member to sponsor-keyed (SPEC F171 — the sponsors epic),
/// widening rather than replacing each contract's own PRESERVE-on-conflict/cap semantics; PLAN T437
/// adds <see cref="UninstallPackAsync"/> (SPEC F172.4, STORY-416), the uninstall
/// <see cref="UpsertAllAsync"/>'s own pack install never had a counterpart for.
/// </summary>
public interface IAdBriefStore
{
    /// <summary>
    /// Upserts one brief, keyed on <c>(pack_slug, sponsor_id, premise)</c> folded (SPEC F171.6's own
    /// pack-install key, <c>station.ad_brief</c>'s <c>ad_brief_sponsor_id_premise_key</c> constraint,
    /// db/46). A SECOND call with the SAME <paramref name="packSlug"/>/<paramref name="sponsorId"/>/
    /// <paramref name="premise"/> triple updates the existing row's <paramref name="tone"/>/
    /// <paramref name="structure"/> in place — never a duplicate row, and <c>created_at</c> is
    /// untouched by the update half. A DIFFERENT <paramref name="premise"/> for the same
    /// <paramref name="sponsorId"/> is a legal second row (SPEC F171.6 — several angles per sponsor).
    ///
    /// <para>
    /// <b>RULED at T405 review — <paramref name="enabled"/> is PRESERVE-on-conflict, never
    /// overwrite.</b> <paramref name="enabled"/> only ever sets the value for a BRAND-NEW row (the
    /// INSERT half); a SECOND call for an EXISTING key leaves that row's own <c>enabled</c> flag
    /// exactly as it was, no matter what <paramref name="enabled"/> the caller passes on that second
    /// call. <c>enabled</c> is the operator's OWN lever (<see cref="SetEnabledAsync"/>, SPEC F162.1) —
    /// a content-refresh upsert (a pack reinstall, SPEC F162.2) must never silently re-enable a brief
    /// the operator deliberately disabled, or silently disable one the operator deliberately
    /// re-enabled.
    /// </para>
    /// </summary>
    Task<AdBrief> UpsertAsync(
        string? packSlug, long sponsorId, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct);

    /// <summary>
    /// Upserts EVERY brief in <paramref name="briefs"/> for ONE <paramref name="packSlug"/>, inside a
    /// SINGLE transaction (SPEC F162.2, PLAN T405) — either every declared brief lands, or none does
    /// (a failure partway through rolls back the whole batch, never a partially-installed pack).
    /// Deliberately keyed on <c>(packSlug, brief.SponsorId)</c> — NOT
    /// <see cref="UpsertAsync"/>'s own <c>(sponsor_id, premise_key)</c> "several angles per sponsor"
    /// key (SPEC F171.6): a pack's declared brief for one brand is ONE slot across reinstalls, so its
    /// <see cref="AdBriefUpsertInput.Premise"/>/<see cref="AdBriefUpsertInput.Tone"/>/
    /// <see cref="AdBriefUpsertInput.Structure"/> all REFRESH in place on the SAME row even when the
    /// manifest's own premise text changes between installs (T405 review F2 — "content refreshes,
    /// operator state persists" is one property, not a contradiction). A brand-new
    /// <c>(packSlug, brief.SponsorId)</c> key lands <c>enabled: true</c> (SPEC F162.2's "installed
    /// briefs are live by default"); an EXISTING one's own <c>enabled</c> flag is left exactly as the
    /// operator last set it. Returns every upserted row, in <paramref name="briefs"/>' own order.
    /// </summary>
    Task<IReadOnlyList<AdBrief>> UpsertAllAsync(
        string packSlug, IReadOnlyList<AdBriefUpsertInput> briefs, CancellationToken ct);

    /// <summary>
    /// Picks ONE row at random from every currently <c>enabled</c> brief of an UNPAUSED sponsor ONLY
    /// (SPEC F160.2's own "one brief sampled from enabled ad_brief rows", narrowed by SPEC F173.2's
    /// own "the worker's refill never drafts from a paused sponsor's briefs"; PLAN T402, T440,
    /// <c>AdSpotWorker</c>'s first read of this store) — <see langword="null"/> when no unpaused
    /// sponsor has an enabled brief, a normal, silent outcome (an empty brief universe, every brief
    /// disabled, or every enabled brief's own sponsor paused) this call's caller treats as "nothing to
    /// generate this tick", never an error. Random, not oldest/round-robin: unlike <c>ad_spot</c>'s own
    /// oldest-first render claim (a genuine work QUEUE), briefs are a standing catalog with no
    /// per-row "already used" state to rotate through — the SAME "no memory needed" reasoning
    /// <c>LibraryAdSpotSource</c>'s own <c>GetRandomReadyAdSpotAsync</c> already applies one seam
    /// over for picking which ready spot airs next.
    /// </summary>
    Task<AdBrief?> SampleEnabledAsync(CancellationToken ct);

    /// <summary>
    /// Every brief, pack and owner alike, newest-created-first (SPEC F162.1's Briefs tab — PLAN
    /// T403b) — no paging. The brief universe is an operator-curated catalog, dozens not thousands,
    /// the SAME "small catalog" reasoning <see cref="SampleEnabledAsync"/>'s own remarks already give
    /// for skipping a more elaborate scheme one query over; a full list is the honest shape rather
    /// than page/limit/offset ceremony no caller needs yet (T403b's own YAGNI call, documented at the
    /// implementation).
    /// </summary>
    Task<IReadOnlyList<AdBrief>> ListAllAsync(CancellationToken ct);

    /// <summary>
    /// Creates a NEW owner-authored brief (<c>pack_slug</c> forced <see langword="null"/>) — refuses,
    /// never silently updates, when an owner brief with the SAME folded
    /// <paramref name="premise"/> for <paramref name="sponsorId"/> already exists (SPEC F171.6: several
    /// angles are legal, but not two rows for the same angle). Atomic: the INSERT's own
    /// <c>ON CONFLICT ... DO NOTHING</c> IS the check — no separate exists-then-insert round trip, so
    /// no race window between two concurrent creates for the same key. Returns the created row, or
    /// <see langword="null"/> when the key already holds — the caller's own signal to surface 409
    /// (PLAN T403b's own ruling). Deliberately a SEPARATE member from <see cref="UpsertAsync"/>, which
    /// stays reachable, unabridged, for a future pack-install caller that legitimately wants
    /// insert-or-update semantics.
    /// </summary>
    Task<AdBrief?> CreateOwnerAsync(
        long sponsorId, string? premise, string? tone, string? structure, bool enabled, CancellationToken ct);

    /// <summary>
    /// Flips <c>enabled</c> on any brief by id — pack or owner alike (PLAN T403b: enable/disable is
    /// the operator's own lever over pack content too; only CREATE is owner-only). Returns the
    /// updated row, or <see langword="null"/> for an unknown id — the caller's own 404 signal, never
    /// an exception (the <c>AnnouncementRepository.ReArmAsync</c> "guarded WHERE, total" precedent,
    /// one return shape richer since a caller here wants the fresh row back, not just a bool).
    /// </summary>
    Task<AdBrief?> SetEnabledAsync(long id, bool enabled, CancellationToken ct);

    /// <summary>
    /// Uninstalls one ad pack — SPEC F172.4, STORY-416, PLAN T437 — the ad pack's own home store owns
    /// the WHOLE uninstall in one transaction, exactly the way <see cref="UpsertAllAsync"/> already
    /// owns the whole install: every <c>station.ad_brief</c> row for <paramref name="packSlug"/> is
    /// removed, every <c>source = 'pack'</c> <c>station.ad_spot</c> row for this slug that is not
    /// already <c>retired</c> — and not currently <c>rendering</c>, which (PLAN T437 review round 2)
    /// stays undiscardable exactly as <see cref="IAdSpotStore"/>'s own retire member already treats it
    /// — is retired, and every <c>station.sponsor</c> row for this slug is removed too — except one
    /// still named by a spot (any state, including one THIS call just retired, or one still
    /// <c>rendering</c>) or by an OWNER-authored <c>station.ad_brief</c> row (this pack sponsor's own
    /// <c>pack_slug</c>-scoped brief delete above never touches an owner brief), either of which the
    /// row's own <c>NOT NULL REFERENCES ... ON DELETE RESTRICT</c> FK
    /// (db/46) will not allow, and which this call therefore deliberately leaves in place — reported
    /// back via <see cref="AdPackUninstallResult.Deleted.KeptSponsors"/> — rather than attempting and
    /// failing (<see cref="AdPackUninstallResult.Deleted"/>'s own remarks name why).
    ///
    /// <para>
    /// Refuses first, writes nothing, when at least one OWNER-authored reference to one of this pack's
    /// sponsors still exists — an owner <c>station.ad_spot</c> row (any state, including
    /// <c>retired</c>, so long as it is not itself one of the rows THIS call would retire) or a
    /// <c>station.show</c> row (<see cref="AdPackUninstallResult.InUse"/>, naming up to the first ten
    /// of each by title, ordered by <c>created_at</c>). A pack's own <c>source = 'pack'</c> spots are
    /// never themselves a reason to refuse — this call is exactly what retires them.
    /// </para>
    ///
    /// <para>
    /// <see cref="AdPackUninstallResult.NotFound"/> when neither table held a row for
    /// <paramref name="packSlug"/> before the call — there was nothing "installed" to uninstall.
    /// </para>
    /// </summary>
    Task<AdPackUninstallResult> UninstallPackAsync(string packSlug, CancellationToken ct);
}
