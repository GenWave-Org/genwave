using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The Library Gardener's own findings-queue seam (SPEC F153.1, F153.2, F153.9; STORY-374,
/// STORY-375; PLAN T372, gh-#529) — the same "Core-level port a MediaLibrary repository implements
/// directly" placement <see cref="IMediaRotationSink"/>/<see cref="IThumbStore"/> already establish:
/// no third-party type appears anywhere in this signature, so it belongs here rather than in
/// <c>GenWave.MediaLibrary</c>, even though <c>Garden.RotFindingRepository</c> (its one
/// implementation) is the only thing that ever calls Npgsql/Dapper to satisfy it.
///
/// <para>
/// <b>Reconcile methods are per-kind, not one generic call</b> (F153.2's "as built at T372"
/// amendment): each kind's own predicate and evidence shape are different SQL, so each gets its own
/// method here — <see cref="ReconcileDeadFilesAsync"/> is the first (T372); later kinds
/// (near-duplicate, stale-metadata, shelf-dust, unreachable) add their own siblings rather than
/// widening one signature to a lowest-common-denominator shape. Every reconcile runs SET-BASED,
/// inside the repository, in one transaction — a two-statement open/re-open then resolve, never a
/// per-row loop in C#.
/// </para>
/// </summary>
public interface IRotFindingStore
{
    /// <summary>
    /// Reconciles every <see cref="RotKind.DeadFile"/> finding against <c>library.media</c>'s
    /// current state (SPEC F153.3): opens (or re-opens a resolved) finding for a row whose
    /// <c>state = 'failed'</c>, or whose <c>state = 'unavailable'</c> and has stayed that way past
    /// <paramref name="unavailableGrace"/>; resolves an open finding for a row that no longer
    /// matches. A <see cref="RotState.Dismissed"/> row is never touched by either half.
    /// </summary>
    /// <param name="unavailableGrace">How long a row must have been <c>unavailable</c> before it
    /// counts as dead — the caller (<c>Garden.DeadFileGardenerPass</c>) computes this from
    /// <c>Library:Scan:MissThreshold × Library:ScanIntervalSeconds</c>, read live.</param>
    Task ReconcileDeadFilesAsync(TimeSpan unavailableGrace, CancellationToken ct);

    /// <summary>
    /// Reconciles every <see cref="RotKind.NearDuplicate"/> finding against
    /// <c>library.find_near_duplicates(tolerance_ms)</c>'s current result (SPEC F153.5; STORY-376;
    /// PLAN T374): opens (or re-opens a resolved) finding for every media id the function returns
    /// right now, resolves an open finding for a media id it no longer returns. A
    /// <see cref="RotState.Dismissed"/> row is never touched by either half.
    ///
    /// <para>
    /// Evidence is built entirely in SQL: <c>group_key</c>, <c>title_variant</c>, <c>siblings</c>
    /// (the group's OTHER members — media id + duration, for the future Keep-this-one write), and
    /// <c>versions</c> (playable rows sharing this row's <c>(artist_key, title_key)</c> but NOT in
    /// its own group — a different <c>title_variant</c> or beyond tolerance — media id, title,
    /// variant, and duration, nearest-duration-first, capped at 10).
    /// </para>
    ///
    /// <para>
    /// <b>Ruled at T374 (2026-08-30), SPEC F153.5's own rider:</b> the function anchors every
    /// candidate to its PARTITION's shortest duration only — never a second clustering level — so
    /// 200000/203000/203500 ms at a 2000 ms tolerance opens NO finding even though the last two are
    /// only 500 ms apart. A known miss, pinned as a regression fact by
    /// <c>Garden.RotFindingRepository</c>'s own facts, not a bug: gh-#610's shape (exact-duplicate
    /// doubles) is caught either way, and the risk this epic ranks highest is false alarms, not
    /// misses (PROJECT.md #3).
    /// </para>
    /// </summary>
    /// <param name="toleranceMs">Duration tolerance in milliseconds — the caller
    /// (<c>Garden.NearDuplicateGardenerPass</c>) reads <c>Gardener:DuplicateToleranceMs</c> live,
    /// floored at 0.</param>
    Task ReconcileNearDuplicatesAsync(int toleranceMs, CancellationToken ct);

    /// <summary>
    /// Reconciles every <see cref="RotKind.StaleMetadata"/> finding against
    /// <c>library.media</c>'s current tag state (SPEC F153.6; STORY-377; PLAN T375): opens (or
    /// re-opens a resolved) finding for a <c>ready</c>, <c>eligible</c>, non-<c>never_play</c> row
    /// missing one or more of five fields — <c>artist</c> (blank), <c>title</c> (blank, or the
    /// "Track NN" family), <c>year</c> (null with a recorded lookup miss), <c>moods</c> (null with
    /// a recorded tag miss), <c>measurable</c> (explicitly <see langword="false"/>, never a bare
    /// <see langword="null"/>) — resolves an open finding once none of the five hold. A row with an
    /// operator-stamped <c>tags_edited_at</c> is exempt for <c>artist</c>/<c>title</c>/<c>year</c>
    /// only (the three operator-patchable fields); <c>moods</c>/<c>measurable</c> are never exempt.
    /// A <see cref="RotState.Dismissed"/> row is never touched by either half.
    ///
    /// <para>
    /// Deliberately NOT <c>MediaRepository.PlayablePredicate</c> (ORCHESTRATOR ruling): that
    /// predicate requires <c>m.measurable</c> true, which would exclude every row this pass exists
    /// to flag for a <see langword="false"/> <c>measurable</c> value — the scope here is
    /// <c>state = 'ready' and eligible and not never_play</c> only.
    /// </para>
    ///
    /// <para>
    /// Evidence is <c>{"fields": [...]}</c> — the subset of <c>artist</c>/<c>title</c>/<c>year</c>/
    /// <c>moods</c>/<c>measurable</c> currently failing, in that fixed order; a row whose computed
    /// set is empty gets no finding at all, and one already open resolves.
    /// </para>
    /// </summary>
    Task ReconcileStaleMetadataAsync(CancellationToken ct);

    /// <summary>
    /// Reconciles every <see cref="RotKind.ShelfDust"/> finding (SPEC F153.7; STORY-377; PLAN
    /// T375): opens (or re-opens a resolved) finding for a
    /// <c>Catalog.MediaRepository.PlayablePredicate</c> row with no <c>library.media_rotation</c>
    /// row (or one with <c>play_count = 0</c>), discovered further back than
    /// <paramref name="shelfAge"/>, and carrying no currently-<c>open</c>
    /// <see cref="RotKind.Unreachable"/> finding of its own — resolves an open finding once the row
    /// airs, stops being playable, or an <c>unreachable</c> finding opens for it. A
    /// <see cref="RotState.Dismissed"/> row is never touched by either half.
    /// </summary>
    /// <param name="shelfAge">How long since <c>discovered_at</c> before a never-aired row counts
    /// as dust — the caller (<c>Garden.ShelfDustGardenerPass</c>) reads
    /// <c>Gardener:ShelfDustDays</c> live, floored at 1 day, and bound as a Postgres
    /// <c>interval</c>.</param>
    Task ReconcileShelfDustAsync(TimeSpan shelfAge, CancellationToken ct);

    /// <summary>
    /// Reconciles every <see cref="RotKind.Unreachable"/> finding against a caller-supplied, ALREADY
    /// DISTINCT set of envelope tuples (SPEC F153.8; STORY-378; PLAN T376): opens (or re-opens a
    /// resolved) finding for a <c>Catalog.MediaRepository.PlayablePredicate</c> row admitted by NONE
    /// of <paramref name="envelopes"/>, resolves an open finding once the row is admitted by at
    /// least one tuple or stops being playable. A <see cref="RotState.Dismissed"/> row is never
    /// touched by either half.
    ///
    /// <para>
    /// Evidence is <c>{"reason": &lt;"genre"|"energy"&gt;, "envelopes": &lt;tuple count&gt;}</c> —
    /// <c>"genre"</c> when NO tuple's own genre constraint admits the row at all, else
    /// <c>"energy"</c> (the row's genre is admitted by at least one tuple, but no tuple that admits
    /// its genre also admits its energy); a row failing both reads <c>"genre"</c> — genre wins the
    /// tie (T376 ORCHESTRATOR ruling).
    /// </para>
    ///
    /// <para>
    /// <b>No station-schema table (STORY-378 AC6)</b> — envelopes arrive purely as caller-supplied
    /// values; <c>Garden.UnreachableGardenerPass</c> (the one caller) is the only place that ever
    /// reads the schedule grid, over <see cref="IScheduleStore"/>, an entirely separate seam this
    /// store's own implementation never touches.
    /// </para>
    /// </summary>
    /// <param name="envelopes">Every DISTINCT effective envelope tuple currently in play — the
    /// caller's own dedup, never re-derived here. Never empty (the caller's own station-default
    /// fallback guarantees at least one, even for an empty schedule grid); an empty list is an
    /// <see cref="ArgumentException"/>, not a silent no-op.</param>
    Task ReconcileUnreachableAsync(IReadOnlyList<EnvelopeTuple> envelopes, CancellationToken ct);

    /// <summary>
    /// Opens (or re-opens a resolved) ONE <see cref="RotKind.DeadFile"/> finding for
    /// <paramref name="mediaId"/> — <see cref="IDeadFileReporter"/>'s own write (SPEC F153.4;
    /// STORY-375; PLAN T373): the same open/re-open statement shape
    /// <see cref="ReconcileDeadFilesAsync"/>'s own insert half uses, narrowed to a single id
    /// instead of the reconcile's set-based predicate, with evidence
    /// <c>{"reason": <paramref name="reason"/>, "since": &lt;now&gt;}</c>. An unknown media id
    /// matches no row and inserts nothing — never a throw; a <see cref="RotState.Dismissed"/> row
    /// is left untouched, exactly like the reconcile's own insert half.
    ///
    /// <para>
    /// T373 review LOW-4: calling this against an ALREADY-<see cref="RotState.Open"/> finding
    /// overwrites its <c>evidence</c> unconditionally — a report against a row
    /// <see cref="ReconcileDeadFilesAsync"/> already opened for <c>failed</c>/<c>unavailable</c>
    /// makes <c>evidence.reason</c> read <c>push_missing</c> until the next
    /// <see cref="ReconcileDeadFilesAsync"/> tick restores the state-based reason. <c>opened_at</c>
    /// is never bumped for an already-open row (<c>Garden.RotFindingRepository.OpenOrReopenOnConflict</c>'s
    /// own <c>case when ... = 'resolved'</c> guard), so this overwrite alone can never re-arm the
    /// flap guard's own grace window.
    /// </para>
    /// </summary>
    Task OpenDeadFileAsync(long mediaId, string reason, CancellationToken ct);

    /// <summary>
    /// Dismisses a finding at the store level (STORY-374 AC4): an <see cref="RotState.Open"/> row
    /// moves to <see cref="RotState.Dismissed"/> with <c>dismissed_at</c> stamped, and returns
    /// <see langword="true"/>. Anything else — an unknown id, or a row that is already
    /// <see cref="RotState.Dismissed"/> or currently <see cref="RotState.Resolved"/> — is a no-op
    /// that returns <see langword="false"/>; a dismissed row is then never re-opened by any pass
    /// (SPEC F153.2).
    /// </summary>
    Task<bool> DismissAsync(long findingId, CancellationToken ct);

    /// <summary>
    /// Every finding matching the given filters, newest-opened first, bounded to
    /// <paramref name="limit"/> rows starting at <paramref name="offset"/> (T372 review LOW-2: the
    /// table carries findings forever with no retention, so an unbounded read was a live footgun the
    /// moment the queue had any real depth). <paramref name="kind"/>/<paramref name="state"/>
    /// <see langword="null"/> means "any". Default <paramref name="limit"/> 200 matches this
    /// codebase's other admin-list page sizes; callers needing a different page pass it explicitly.
    ///
    /// <para>
    /// <b>T377 review — the bound is callee-enforced.</b> A negative <paramref name="offset"/> errors
    /// in Postgres and an unbounded/huge <paramref name="limit"/> re-opens the LOW-2 footgun even
    /// with a well-behaved caller, so <c>Garden.RotFindingRepository</c> floors both itself
    /// (<paramref name="limit"/> to at least 1, capped at 1000; <paramref name="offset"/> to at least
    /// 0) rather than trusting every caller — including <see cref="ListWithMediaAsync"/>'s own callers
    /// — to have already clamped them. <c>GardenerController</c>'s own endpoint clamp is a courtesy
    /// (a friendlier response shape), never the only gate.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RotFinding>> ListAsync(
        RotKind? kind, RotState? state, CancellationToken ct, int limit = 200, int offset = 0);

    /// <summary>
    /// <see cref="ListAsync"/>'s own filters, joined out to the
    /// <c>library.media</c>/<c>library.media_rotation</c>/<c>library.media_rating</c> row each finding
    /// is about — T377's admin surface (SPEC F153.9, STORY-374 AC7), extended at T385 (SPEC F153.9
    /// rider 2026-08-31; STORY-382 AC6, STORY-383) to return <see cref="RotFindingPage.Total"/> of
    /// matching paging units alongside the page, computed against the SAME <paramref name="kind"/>/
    /// <paramref name="state"/> filter — EXACT for a kind-scoped read; PAGE-LOCAL (never derived from a
    /// second query) for the kind-less read — see below for exactly which.
    ///
    /// <para>
    /// <b>The kind-LESS read (<paramref name="kind"/> <see langword="null"/>) keeps T377's exact row
    /// shape, verbatim (regression pin) — but <see cref="RotFindingPage.Total"/> is now PAGE-LOCAL</b>
    /// (T385 review LOW-2). Rows page FLAT, in <c>kind, group_key nulls last, opened_at desc, id</c>
    /// order, BEFORE <c>GardenerController</c> ever groups them by kind — a caller paging with a small
    /// <paramref name="limit"/> can still see a <see cref="RotKind.NearDuplicate"/> group split across a
    /// page boundary here (its own rows are adjacent within one page thanks to the <c>group_key</c>
    /// ordering, but a page edge can still fall inside a group). <see cref="RotFindingPage.Total"/> for
    /// this shape is simply <see cref="RotFindingPage.Items"/>'s own count, NOT the true matching row
    /// count across every kind — no second query runs to compute one. <c>GardenerController</c> never
    /// puts <see cref="RotFindingPage.Total"/> on the wire for a kind-less call at all (T377's own
    /// pinned response shape carries no <c>count</c> field, STORY-382 AC8), so no caller reads this
    /// value for this shape; it exists only for the interface's own uniform return type.
    /// </para>
    ///
    /// <para>
    /// <b>A kind-scoped read (<paramref name="kind"/> non-<see langword="null"/>) pages WITHIN that
    /// kind, and <see cref="RotFindingPage.Total"/> IS the exact matching count.</b> For every kind
    /// except <see cref="RotKind.NearDuplicate"/>, this is the SAME flat row paging, narrowed by
    /// <c>kind</c> — <see cref="RotFindingPage.Total"/> is the exact matching row count. For
    /// <see cref="RotKind.NearDuplicate"/>, the PAGING UNIT is the GROUP: <paramref name="limit"/>/
    /// <paramref name="offset"/> count DISTINCT <c>group_key</c>s, ordered ascending (stable across
    /// pages), and <see cref="RotFindingPage.Items"/> carries EVERY member row of every SELECTED group
    /// — a page can never hold a partial group, so a caller acting on a whole cluster (Keep-this-one)
    /// never sees a truncated one. Row order within a near-duplicate page stays <c>group_key asc,
    /// opened_at desc, id</c> — the SAME relative order the flat kind-scoped shape's own <c>group_key,
    /// opened_at desc, id</c> tail gives a kind already narrowed to one value, so
    /// <c>GardenerController</c>'s own grouping-by-<c>group_key</c> logic keeps working unchanged
    /// either way. <see cref="RotFindingPage.Total"/> here is the exact count of DISTINCT matching
    /// <c>group_key</c>s, never the row count of the returned page.
    /// </para>
    ///
    /// <para>
    /// <b>RULED (T385 review HIGH-1): for the <see cref="RotKind.NearDuplicate"/> group-paged shape,
    /// <paramref name="state"/> scopes which GROUPS qualify, never which MEMBER rows render.</b> A
    /// group qualifies the moment ANY ONE of its members matches <c>kind = near_duplicate</c> +
    /// <paramref name="state"/>; once a group qualifies, ALL of its member rows render in
    /// <see cref="RotFindingPage.Items"/> regardless of each member's own individual state — the
    /// whole-cluster contract above ("a page can never hold a partial group") is unconditional, per SPEC
    /// F153.9 rider's own binding text: "the response returns every member row of every selected
    /// group". A group where NO member matches <paramref name="state"/> (every member dismissed, under
    /// <c>state=open</c>) does not qualify at all — it consumes no page slot and does not count into
    /// <see cref="RotFindingPage.Total"/>.
    /// </para>
    ///
    /// <para>
    /// <b>RULED (round-2 review HIGH-2): a RESOLVED member row never renders inside its group, even
    /// though its own <c>group_key</c> survives a resolve untouched.</b> A near-duplicate member that
    /// left <c>library.find_near_duplicates</c> on its own (an operator retagged it; it is genuinely no
    /// longer a duplicate, still eligible, still in rotation) must not keep appearing inside its old
    /// group — a caller's Keep-this-one bulk write would otherwise pull that distinct, in-rotation track
    /// out of rotation. <b>dismissed = the operator closed the finding while the media is still a
    /// duplicate → render; resolved = the system closed it because the media is no longer a duplicate →
    /// don't render.</b> This exclusion is member-side only — it never changes which GROUPS qualify
    /// above.
    /// </para>
    ///
    /// <para>
    /// T385 review MED-4: the shared <c>ClampPaging</c> cap (1000, see <c>Garden.RotFindingRepository</c>'s
    /// own remarks) counts ROWS for every kind (and the kind-less read) but counts GROUPS for
    /// <see cref="RotKind.NearDuplicate"/> — <paramref name="limit"/> there bounds the number of
    /// DISTINCT <c>group_key</c>s, so the row envelope for that page is at most 1000 groups × each
    /// group's own member count (typically 2–5), never a flat 1000-row cap; a per-row cap would force a
    /// partial group onto a page, which the whole-cluster contract above forbids.
    /// </para>
    ///
    /// <para>
    /// T377 review LOW-2 (unaffected by T385): the flat shape's own <c>order by</c> is NOT
    /// index-covered — <c>group_key</c> sits mid-key (between <c>kind</c> and <c>opened_at</c>), and
    /// no index on <c>library.rot_finding</c> leads with it, so Postgres sorts the whole filtered join
    /// result before applying <c>LIMIT</c> rather than walking an already-ordered index. The
    /// <see cref="ListAsync"/> bound (<c>ClampPaging</c>'s own 1000-row cap) is what keeps that sort
    /// cheap regardless of table size — see <c>Garden.RotFindingRepository.ListWithMediaAsync</c>'s
    /// own remarks for the query text itself.
    /// </para>
    /// </summary>
    Task<RotFindingPage> ListWithMediaAsync(
        RotKind? kind, RotState? state, int limit, int offset, CancellationToken ct);

    /// <summary>
    /// How many <see cref="RotState.Open"/> findings exist per <see cref="RotKind"/> right now — the
    /// <c>GET /api/status</c> Gardener tile's own aggregate (SPEC F153.9, T377). A kind with zero
    /// open findings is simply absent from the result rather than present with a zero count.
    /// </summary>
    Task<IReadOnlyDictionary<RotKind, int>> CountOpenByKindAsync(CancellationToken ct);
}
