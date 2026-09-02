using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The <c>station.ad_brief</c> seam (SPEC F159.1, F162.2; STORY-389, STORY-392; PLAN T398, T403b,
/// T405) — deliberately narrow, exactly <see cref="IAdSpotStore"/>'s own "Core-level port a
/// MediaLibrary repository implements directly" placement, one table over. <see cref="UpsertAsync"/>/
/// <see cref="SampleEnabledAsync"/> shipped with T398 (STORY-389 AC1's own upsert fact, T400's own
/// prompt sampler); <see cref="ListAllAsync"/>/<see cref="CreateOwnerAsync"/>/
/// <see cref="SetEnabledAsync"/> widen the seam additively for T403b's Briefs admin surface (SPEC
/// F162.1); <see cref="UpsertAllAsync"/> widens it again for T405's ad-pack install (SPEC F162.2) —
/// every prior member's contract is untouched, save <see cref="UpsertAsync"/>'s own <c>enabled</c>
/// semantics, RULED at T405 review (see that member's own remarks).
/// </summary>
public interface IAdBriefStore
{
    /// <summary>
    /// Upserts one brief, keyed on <c>(pack_slug, brand)</c> (SPEC F162.2's own pack-install key,
    /// <c>station.ad_brief</c>'s <c>UNIQUE NULLS NOT DISTINCT (pack_slug, brand)</c> constraint,
    /// db/42). A SECOND call with the SAME <paramref name="packSlug"/>/<paramref name="brand"/> pair
    /// updates the existing row's <paramref name="premise"/>/<paramref name="tone"/>/
    /// <paramref name="structure"/> in place — never a duplicate row, and <c>created_at</c> is
    /// untouched by the update half.
    ///
    /// <para>
    /// <b>RULED at T405 review — <paramref name="enabled"/> is PRESERVE-on-conflict, never
    /// overwrite.</b> <paramref name="enabled"/> only ever sets the value for a BRAND-NEW row (the
    /// INSERT half); a SECOND call for an EXISTING <c>(pack_slug, brand)</c> pair leaves that row's
    /// own <c>enabled</c> flag exactly as it was, no matter what <paramref name="enabled"/> the
    /// caller passes on that second call. <c>enabled</c> is the operator's OWN lever
    /// (<see cref="SetEnabledAsync"/>, SPEC F162.1) — a content-refresh upsert (a pack reinstall,
    /// SPEC F162.2) must never silently re-enable a brief the operator deliberately disabled, or
    /// silently disable one the operator deliberately re-enabled.
    /// </para>
    ///
    /// <para>
    /// <b>Ratified by Dean 2026-09-02 (SPEC F159.1 rider, PLAN T398): the owner-brief cap.</b>
    /// <paramref name="packSlug"/> <see langword="null"/> means an owner-authored brief — Postgres's
    /// <c>NULLS NOT DISTINCT</c> modifier makes every such call collapse onto the SAME row for a
    /// given <paramref name="brand"/>, so re-authoring an existing owner brief for a brand UPDATES
    /// it, never forks a second one: a brand is a brand.
    /// </para>
    /// </summary>
    Task<AdBrief> UpsertAsync(
        string? packSlug, string brand, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct);

    /// <summary>
    /// Upserts EVERY brief in <paramref name="briefs"/> for ONE <paramref name="packSlug"/>, inside a
    /// SINGLE transaction (SPEC F162.2, PLAN T405) — either every declared brief lands, or none does
    /// (a failure partway through rolls back the whole batch, never a partially-installed pack). The
    /// SAME PRESERVE-on-conflict contract <see cref="UpsertAsync"/> carries applies per brief: a
    /// brand-new <c>(packSlug, brief.Brand)</c> pair lands <c>enabled: true</c> (SPEC F162.2's
    /// "installed briefs are live by default"); an EXISTING one has its
    /// <see cref="AdBriefUpsertInput.Premise"/>/<see cref="AdBriefUpsertInput.Tone"/>/
    /// <see cref="AdBriefUpsertInput.Structure"/> REPLACED but its own <c>enabled</c> flag left
    /// exactly as the operator last set it. Returns every upserted row, in <paramref name="briefs"/>'
    /// own order.
    /// </summary>
    Task<IReadOnlyList<AdBrief>> UpsertAllAsync(
        string packSlug, IReadOnlyList<AdBriefUpsertInput> briefs, CancellationToken ct);

    /// <summary>
    /// Picks ONE row at random from every currently <c>enabled</c> brief (SPEC F160.2's own "one
    /// brief sampled from enabled ad_brief rows"; PLAN T402, <c>AdSpotWorker</c>'s first read of this
    /// store) — <see langword="null"/> when no brief is enabled, a normal, silent outcome (an empty
    /// brief universe, or every one disabled) this call's caller treats as "nothing to generate this
    /// tick", never an error. Random, not oldest/round-robin: unlike <c>ad_spot</c>'s own
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
    /// never silently updates, when an owner brief for <paramref name="brand"/> already exists (the
    /// ratified one-owner-brief-per-brand cap, <see cref="UpsertAsync"/>'s own remarks; SPEC F159.1
    /// rider). Atomic: the INSERT's own <c>ON CONFLICT ... DO NOTHING</c> IS the check — no separate
    /// exists-then-insert round trip, so no race window between two concurrent creates for the same
    /// brand. Returns the created row, or <see langword="null"/> when the cap already holds — the
    /// caller's own signal to surface 409 (PLAN T403b's own ruling). Deliberately a SEPARATE member
    /// from <see cref="UpsertAsync"/>, which stays reachable, unabridged, for a future pack-install
    /// caller that legitimately wants insert-or-update semantics.
    /// </summary>
    Task<AdBrief?> CreateOwnerAsync(
        string brand, string? premise, string? tone, string? structure, bool enabled, CancellationToken ct);

    /// <summary>
    /// Flips <c>enabled</c> on any brief by id — pack or owner alike (PLAN T403b: enable/disable is
    /// the operator's own lever over pack content too; only CREATE is owner-only). Returns the
    /// updated row, or <see langword="null"/> for an unknown id — the caller's own 404 signal, never
    /// an exception (the <c>AnnouncementRepository.ReArmAsync</c> "guarded WHERE, total" precedent,
    /// one return shape richer since a caller here wants the fresh row back, not just a bool).
    /// </summary>
    Task<AdBrief?> SetEnabledAsync(long id, bool enabled, CancellationToken ct);
}
