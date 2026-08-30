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
    /// moment the queue had any real depth — T377's admin surface reuses this same paging rather than
    /// adding its own). <paramref name="kind"/>/<paramref name="state"/> <see langword="null"/> means
    /// "any". Default <paramref name="limit"/> 200 matches this codebase's other admin-list page
    /// sizes; callers needing a different page pass it explicitly.
    /// </summary>
    Task<IReadOnlyList<RotFinding>> ListAsync(
        RotKind? kind, RotState? state, CancellationToken ct, int limit = 200, int offset = 0);

    /// <summary>
    /// How many <see cref="RotState.Open"/> findings exist per <see cref="RotKind"/> right now — the
    /// <c>GET /api/status</c> Gardener tile's own aggregate (SPEC F153.9, T377). A kind with zero
    /// open findings is simply absent from the result rather than present with a zero count.
    /// </summary>
    Task<IReadOnlyDictionary<RotKind, int>> CountOpenByKindAsync(CancellationToken ct);
}
