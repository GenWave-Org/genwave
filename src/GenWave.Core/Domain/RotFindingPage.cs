namespace GenWave.Core.Domain;

/// <summary>
/// One page of <see cref="Abstractions.IRotFindingStore.ListWithMediaAsync"/>'s own joined read (SPEC
/// F153.9 rider 2026-08-31; STORY-382 AC6/AC8, STORY-383; PLAN T385/T386) — <see cref="Items"/> is the
/// <c>limit</c>/<c>offset</c> window's own rows. <see cref="Total"/> is the exact count of matching
/// PAGING UNITS for a KIND-SCOPED read — matching rows for a flat kind, matching DISTINCT
/// <c>group_key</c>s for <see cref="RotKind.NearDuplicate"/> (T385 review HIGH-1: a group counts once
/// it qualifies, i.e. at least one member matches the state filter — every one of its member rows then
/// renders regardless of that member's own state) — but is <see langword="null"/> (T386 review, taking
/// T385's own carry-forward) for the KIND-LESS read, which never runs a second query to compute an
/// exact cross-kind total: the type itself now says "not computed" rather than overloading a real page
/// size into that meaning. <see cref="Total"/> is never the row count of <see cref="Items"/> itself for
/// a near-duplicate page, which is every MEMBER row of the selected groups, not one row per paging
/// unit.
///
/// <para>
/// <b>RULED (round-2 review HIGH-2):</b> a near-duplicate group's rendered member rows exclude any
/// member whose OWN state is <see cref="RotState.Resolved"/> — the resolve half never clears a row's
/// own <c>group_key</c>, so a member that left <c>find_near_duplicates</c> on its own (no longer a
/// duplicate, still eligible, still in rotation) would otherwise keep rendering inside its old group.
/// Dismissed = the operator closed the finding while the media is still a duplicate → render;
/// resolved = the system closed it because the media is no longer a duplicate → don't render. This
/// never changes which groups qualify into <see cref="Total"/> above, only which of a qualifying
/// group's own rows land in <see cref="Items"/>.
/// </para>
/// </summary>
/// <param name="Items">Every row the read returns for this page — for
/// <see cref="RotKind.NearDuplicate"/>, every member row of every group the page selected (a page
/// never holds a partial group); for every other kind (and the kind-less read), the flat row window
/// itself.</param>
/// <param name="Total"><see langword="null"/> for the kind-less read (page-local paging with no exact
/// cross-kind total, never computed); otherwise the exact count of matching paging units for a
/// kind-scoped read — distinct <c>group_key</c>s for <see cref="RotKind.NearDuplicate"/>, matching rows
/// otherwise. <c>GardenerController</c> (T386) puts this on the wire as <c>total</c> exactly when it is
/// non-null — a kind-less call's response therefore carries no <c>total</c> member at all, the
/// T377-pinned shape, STORY-382 AC8.</param>
public sealed record RotFindingPage(IReadOnlyList<RotFindingWithMedia> Items, int? Total);
