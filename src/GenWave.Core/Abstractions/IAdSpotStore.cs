using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// The <c>station.ad_spot</c> lifecycle store (SPEC F159.1, F159.2; STORY-389; PLAN T398) — the
/// <see cref="IAnnouncementStore"/>/<see cref="ILiquidsoapControl"/> placement precedent applied
/// here: a Core-level port a MediaLibrary repository implements directly, never widening the
/// published <c>GenWave.Abstractions</c> NuGet surface (a station-schema write seam has no reason to
/// leave this repo).
///
/// <para>
/// <b>Total state machine, nothing ever deleted (SPEC F159.1/.2).</b> Every transition below stamps
/// <c>state_changed_at</c>; an illegal move (the source row isn't in the required FROM state) is
/// refused, never partially applied — see <see cref="AdSpotWriteResult.Conflict"/>'s own remarks.
/// <see cref="AdState.Retired"/> (and, short of a retry, <see cref="AdState.Failed"/>) are
/// otherwise-terminal states an operator or the stock pass leaves in place — the
/// <c>station.announcement</c> posture, one table over.
/// </para>
///
/// <para>
/// <b>xmin only where a caller CARRIES a version.</b> <see cref="ApproveAsync"/>/
/// <see cref="RetryAsync"/>/<see cref="RetireAsync"/> are the operator-facing (PLAN T403 PATCH-verb)
/// transitions — a browser tab can go stale, so each takes back the <see cref="AdSpot.Version"/> its
/// own prior read returned. <see cref="ClaimNextApprovedAsync"/>/<see cref="MarkReadyAsync"/>/
/// <see cref="MarkFailedAsync"/> are system-driven (PLAN T401/T402's own worker and render task) — no
/// caller ever holds a "previous read" of the row to carry a version from, so each is a plain, total,
/// state-conditional <c>UPDATE</c> instead (the <c>AnnouncementRepository.MarkAiredAsync</c>/
/// <c>ClaimOldestAsync</c> precedent, one table over).
/// </para>
///
/// <para>
/// <b>No cross-schema transaction (the db/22 role boundary, SPEC F159.1's own as-built rider).</b>
/// <see cref="MarkReadyAsync"/> only ever writes <c>station.ad_spot</c> — it never touches
/// <c>library.media</c> itself, because <c>station_svc</c> (this store's own role) has no grant into
/// the <c>library</c> schema, the same boundary that already forced <c>media_id</c>/
/// <c>bed_media_id</c> to be plain, FK-less <c>bigint</c>s (db/42's own header). PLAN T401's render
/// task inserts the rendered row through <c>IAuthoredCatalogWriter</c> (a SEPARATE,
/// <c>library_svc</c>-rooted seam) first, then calls <see cref="MarkReadyAsync"/> with the id that
/// insert returned — two round trips, not one transaction, with T401 itself owning any orphan-media
/// cleanup if the second call fails after the first succeeds. <see cref="MarkReadyAsync"/>'s own
/// <c>long mediaId</c> parameter (never nullable) is this store's half of the "ready requires
/// media_id" invariant (SPEC F159.2) — the C# signature makes the illegal call impossible to even
/// write, alongside db/43's own <c>CHECK</c> backstop.
/// </para>
/// </summary>
public interface IAdSpotStore
{
    /// <summary>
    /// Lands a new row (SPEC F159.1) — always born in <see cref="NewAdSpot.InitialState"/> (Draft,
    /// Approved, or Failed only; see that record's own remarks for why). Guards the "<c>fail_reason</c>
    /// iff <see cref="AdState.Failed"/>" invariant here too, in C#, ahead of db/43's own <c>CHECK</c>
    /// — an <see cref="ArgumentException"/> for a mismatched pair is cheaper for a caller to catch
    /// than a round trip to Postgres.
    /// </summary>
    Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct);

    /// <summary>
    /// Single-row lookup by id (PLAN T403's own <c>GET /api/ads/{id}</c>) — <see langword="null"/>
    /// when no row exists, the same "absence is a normal answer" shape <see cref="ClaimNextApprovedAsync"/>
    /// already uses for "nothing approved". Any existing row is reachable regardless of
    /// <see cref="AdState"/> (the <c>MediaController.GetById</c>/F43.1 IDOR-safe precedent: an
    /// operator opening a Failed spot to read why it failed is exactly this call) — the CALLER decides
    /// what to do with a state it does not expect, never this method.
    /// </summary>
    Task<AdSpot?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary><see cref="AdState.Draft"/> to <see cref="AdState.Approved"/> (SPEC F159.2: operator,
    /// or PLAN T400's own automatic path under <c>Station:Ads:AutoApprove</c>) —
    /// xmin-guarded.</summary>
    Task<AdSpotTransitionOutcome> ApproveAsync(long id, string expectedVersion, CancellationToken ct);

    /// <summary><see cref="AdState.Failed"/> to <see cref="AdState.Approved"/> (SPEC F159.2's own
    /// retry) — xmin-guarded, the SAME target state as <see cref="ApproveAsync"/> reached from a
    /// different, and only, legal source state.</summary>
    Task<AdSpotTransitionOutcome> RetryAsync(long id, string expectedVersion, CancellationToken ct);

    /// <summary>
    /// To <see cref="AdState.Retired"/> from <see cref="AdState.Ready"/> (refresh or operator),
    /// <see cref="AdState.Draft"/> (operator discard), <see cref="AdState.Approved"/>, or
    /// <see cref="AdState.Failed"/> (SPEC F159.2's as-built rider, PLAN T403, 2026-09-02 — the discard
    /// gap: an operator needs an exit from a spot that will never render cleanly, or one they simply
    /// changed their mind about before it ever rendered; the announcements decline precedent). All
    /// four share one method: every path reaches the same terminal state with the same side effect
    /// (stamping <c>retired_at</c>), and none needs its own distinguishing behavior at the store
    /// level. <see cref="AdState.Rendering"/> is deliberately EXCLUDED — it stays undiscardable: it is
    /// transient by construction (<see cref="ReArmAsync"/>'s own guardian re-arms it to
    /// <see cref="AdState.Approved"/> within one grace), so a caller wanting to discard a stuck render
    /// waits for that re-arm, then retires from <see cref="AdState.Approved"/>. xmin-guarded.
    /// </summary>
    Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct);

    /// <summary>
    /// Sparse content edit (PLAN T403's own owner editor <c>PATCH /api/ads/{id}</c>) — brand, title,
    /// brief, script, voice plan, spot length, bed. Legal ONLY against a row currently
    /// <see cref="AdState.Draft"/> or <see cref="AdState.Failed"/> (PLAN T403's own ruling: editing an
    /// <see cref="AdState.Approved"/> spot would invalidate a render already in flight or already
    /// landed, and every OTHER state either has no content left to edit
    /// (<see cref="AdState.Ready"/>/<see cref="AdState.Retired"/>) or is mid-flight
    /// (<see cref="AdState.Rendering"/>) — a Failed spot's script is exactly what an operator fixes
    /// before <see cref="ApproveAsync"/>-via-<c>RetryAsync</c>, the SAME reasoning
    /// <see cref="RetryAsync"/>'s own remarks give for why Failed is its one legal FROM state). This
    /// never changes <see cref="AdSpot.State"/> itself — a caller wanting Failed→Approved calls
    /// <see cref="RetryAsync"/> separately, after this edit. xmin-guarded, mirrors every other
    /// operator-facing transition on this store. <see cref="AdSpotEdit"/>'s own remarks describe the
    /// sparse-field ("<see langword="null"/> means unchanged") contract in full.
    /// </summary>
    Task<AdSpotTransitionOutcome> UpdateAsync(long id, AdSpotEdit edit, string expectedVersion, CancellationToken ct);

    /// <summary>
    /// <see cref="AdState.Approved"/> to <see cref="AdState.Rendering"/> (PLAN T402's own worker
    /// claim, SPEC F159.2) — atomically claims the OLDEST <see cref="AdState.Approved"/> row
    /// (<c>state_changed_at</c> ascending) via <c>FOR UPDATE SKIP LOCKED</c>, so two concurrent worker
    /// ticks can never claim the same spot twice (mirrors
    /// <c>AnnouncementRepository.ClaimOldestAsync</c>'s own concurrency shape, narrowed to one row per
    /// call — this worker renders one spot per tick, PLAN T402's own line). Returns
    /// <see langword="null"/> when nothing is <see cref="AdState.Approved"/> — always a legal answer,
    /// never an error (an empty approval queue is a normal day).
    /// </summary>
    Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct);

    /// <summary>
    /// Stamps <paramref name="voicePlanJson"/> onto a currently <see cref="AdState.Rendering"/> row —
    /// PLAN T415's own cast-pick seam, run once between <see cref="ClaimNextApprovedAsync"/> and the
    /// render itself. Never-overwrite is enforced HERE, in SQL, not merely by the caller checking
    /// <see cref="AdSpot.VoicePlan"/> first (SPEC F167's own guarantee that an owner draft's explicit
    /// plan — or any plan a previous stamp already wrote — survives untouched): the implementation's
    /// own <c>coalesce(voice_plan, ...)</c> means a row that already carries a plan is returned
    /// unchanged, at the SQL layer, even under a race the C#-side null check alone couldn't close.
    /// Total: a row not currently <see cref="AdState.Rendering"/> (already resolved or reclaimed by a
    /// concurrent tick) leaves the guarded <c>WHERE</c> matching nothing — returns <see langword="null"/>,
    /// never throws, mirroring <see cref="MarkReadyAsync"/>'s own posture. The caller renders whatever
    /// this returns; a <see langword="null"/> here is a benign, if unlikely, race — never a reason to
    /// abort the tick.
    /// </summary>
    Task<AdSpot?> StampVoicePlanIfNullAsync(long id, string voicePlanJson, CancellationToken ct);

    /// <summary>
    /// Stamps <paramref name="bedMediaId"/> onto a currently <see cref="AdState.Rendering"/> row —
    /// PLAN T416's own bed-pick seam, run once between <see cref="ClaimNextApprovedAsync"/> and the
    /// render itself (the SAME seam <see cref="StampVoicePlanIfNullAsync"/> already occupies, SPEC
    /// F168.2). Never-overwrite is enforced HERE, in SQL, not merely by the caller checking
    /// <see cref="AdSpot.BedMediaId"/> first (SPEC F168.5's own guarantee that an owner's explicit bed
    /// — set at approval time, or stamped by a previous pick — survives untouched): the
    /// implementation's own <c>coalesce(bed_media_id, ...)</c> means a row that already carries a bed
    /// is returned unchanged, at the SQL layer, even under a race the C#-side null check alone
    /// couldn't close. Total: a row not currently <see cref="AdState.Rendering"/> (already resolved or
    /// reclaimed by a concurrent tick) leaves the guarded <c>WHERE</c> matching nothing — returns
    /// <see langword="null"/>, never throws, mirroring <see cref="StampVoicePlanIfNullAsync"/>'s own
    /// posture. The caller renders whatever this returns; a <see langword="null"/> here is a benign,
    /// if unlikely, race — never a reason to abort the tick.
    /// </summary>
    Task<AdSpot?> StampBedIfNullAsync(long id, long bedMediaId, CancellationToken ct);

    /// <summary>
    /// <see cref="AdState.Rendering"/> to <see cref="AdState.Ready"/> (SPEC F159.2), stamping
    /// <paramref name="mediaId"/> and <c>rendered_at</c> — PLAN T401's own render-success seam. See
    /// this interface's own remarks for why this never opens a cross-schema transaction with the
    /// <c>library.media</c> insert that must already have happened. Total: a row not currently
    /// <see cref="AdState.Rendering"/> (already handled by a different call, or never claimed) leaves
    /// the guarded <c>WHERE</c> matching nothing — reports <see langword="false"/>, never throws.
    /// </summary>
    Task<bool> MarkReadyAsync(long id, long mediaId, CancellationToken ct);

    /// <summary>
    /// <see cref="AdState.Rendering"/> to <see cref="AdState.Failed"/> (SPEC F159.2), stamping
    /// <paramref name="failReason"/> — PLAN T401's own render-failure seam (a TTS or measurement
    /// failure mid-render, distinct from STORY-390 AC3's own pre-render validation failure, which
    /// <see cref="CreateAsync"/> already covers via <see cref="NewAdSpot.InitialState"/>). Total,
    /// mirrors <see cref="MarkReadyAsync"/>'s own posture exactly.
    /// </summary>
    Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct);

    /// <summary>
    /// State-scoped paged listing with an exact total (the T385 kind-scoped paging precedent, PLAN
    /// T403's own admin list) — <paramref name="state"/> <see langword="null"/> means "any".
    /// <paramref name="sponsorId"/> <see langword="null"/> means "any sponsor" — non-null narrows
    /// both the page and the total to exactly that sponsor's rows (PLAN T436's own admin filter,
    /// SPEC F171.7's <c>GET /api/ads?sponsorId=</c>). <c>state_changed_at desc, id desc</c> —
    /// newest-transitioned-first, so a fresh batch of drafts or a just-failed spot needing triage
    /// surfaces at the top regardless of when the row was originally created.
    /// <paramref name="limit"/>/<paramref name="offset"/> are floored by the implementation (the
    /// <c>RotFindingRepository.ClampPaging</c> precedent) — never trust every caller to have already
    /// clamped them.
    /// </summary>
    Task<AdSpotPage> ListByStateAsync(AdState? state, long? sponsorId, int limit, int offset, CancellationToken ct);

    /// <summary>
    /// How many generated spots (<see cref="AdSource.Llm"/> or <see cref="AdSource.Pack"/> source) of
    /// an UNPAUSED sponsor sit anywhere in the stock pipeline — <see cref="AdState.Draft"/>,
    /// <see cref="AdState.Approved"/>, <see cref="AdState.Rendering"/>, or <see cref="AdState.Ready"/>
    /// (SPEC F159.3's own <c>Station:Ads:TargetCount</c> stock count, as-built rider gh-#689; narrowed
    /// by SPEC F173.4's own "stock keeping counts only spots of unpaused sponsors", PLAN T440). A
    /// draft waiting for the owner's eye under <c>AutoApprove=false</c> IS stock on its way — counting
    /// only the ready shelf left that pile unbounded (one new draft per tick, forever).
    /// <see cref="AdState.Failed"/> never counts (it waits for an operator retry or discard and must
    /// never block refill), <see cref="AdState.Retired"/> is terminal, <see cref="AdSource.Owner"/>
    /// spots never count toward the target the stock pass refills, and NEITHER does a spot whose own
    /// sponsor is currently paused — <c>TargetCount</c> refills from the other sponsors' briefs while
    /// one is paused, rather than reading the station as already "full" on spots that will never air.
    /// </summary>
    Task<int> CountStockGeneratedAsync(CancellationToken ct);

    /// <summary>
    /// Every <see cref="AdState.Ready"/> spot whose <c>state_changed_at</c> (the moment it entered
    /// <see cref="AdState.Ready"/> — its only outgoing transition is to <see cref="AdState.Retired"/>,
    /// so this stays accurate for as long as the row stays <see cref="AdState.Ready"/>) is older than
    /// <paramref name="age"/> — SPEC F159.3's own refresh candidates. <see cref="AdSource.Owner"/>
    /// spots are excluded outright (never a candidate, SPEC F159.3's exemption), not merely
    /// deprioritized — the stock pass (PLAN T402) never sees one here to retire by mistake.
    /// </summary>
    Task<IReadOnlyList<AdSpot>> ListReadyOlderThanAsync(TimeSpan age, CancellationToken ct);

    /// <summary>
    /// Every currently <see cref="AdState.Rendering"/> row whose <c>state_changed_at</c> is older
    /// than <paramref name="now"/> minus <paramref name="grace"/> — the stuck-render guardian's own
    /// candidate read (PLAN T402, the <c>IAnnouncementLifecycle.FindClaimedPastGraceAsync</c>
    /// precedent one seam over). A read only — the caller (<c>AdSpotLifecycleGuardianService</c>)
    /// drives <see cref="ReArmAsync"/> per candidate itself, mirroring
    /// <see cref="ClaimNextApprovedAsync"/>'s own read-then-transition split. <paramref name="grace"/>
    /// MUST exceed <c>AdSpotWorker</c>'s own per-render budget for this read to only ever catch a
    /// genuinely abandoned row (a crashed process, never a render still honestly in flight) — see
    /// <c>AdSpotLifecycleGuardianService</c>'s own remarks for how that relation is pinned, not merely
    /// documented.
    /// </summary>
    Task<IReadOnlyList<long>> FindRenderingPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// <see cref="AdState.Rendering"/> to <see cref="AdState.Approved"/> for one row (PLAN T402) — the
    /// SAME target state <see cref="ApproveAsync"/> reaches from <see cref="AdState.Draft"/>, reached
    /// here from a DIFFERENT source state by a DIFFERENT, system-driven caller: the stuck-render
    /// guardian (a row past <see cref="FindRenderingPastGraceAsync"/>'s own grace) AND
    /// <c>AdSpotWorker</c>'s own break-window yield (a render this worker abandoned deliberately,
    /// never a failure — the row simply gets another attempt once the window closes, no operator
    /// retry required). No xmin: identical reasoning to <see cref="ClaimNextApprovedAsync"/>/
    /// <see cref="MarkReadyAsync"/>/<see cref="MarkFailedAsync"/> — no caller here ever holds a
    /// "previous read" of the row to carry a version from. Total: a row not currently
    /// <see cref="AdState.Rendering"/> (already resolved by the render itself, or a concurrent guardian
    /// sweep) leaves the guarded <c>WHERE</c> matching nothing and reports <see langword="false"/>,
    /// never throws — the exact claim-conflict shape <c>AdSpotWorker</c>'s own render-outcome handling
    /// is built to tolerate.
    /// </summary>
    Task<bool> ReArmAsync(long id, CancellationToken ct);

    /// <summary>
    /// Media ids to withhold from airing right now, for one of two reasons (SPEC F171, F174; PLAN
    /// T432): the spot's own sponsor is currently <see cref="Domain.Sponsor.Paused"/>, OR the spot's
    /// sponsor is one of the sponsors already carried by the first <paramref name="window"/> entries
    /// of <paramref name="recentMediaIds"/> (most-recent-first — the crosstalk/repeat-sponsor guard,
    /// looked up through <see cref="AdSpot.MediaId"/>, not a separate rotation table). Only
    /// <see cref="AdState.Ready"/> spots are ever candidates — nothing else is airable in the first
    /// place. <paramref name="window"/> of zero excludes only paused-sponsor spots; an empty
    /// <paramref name="recentMediaIds"/> has the same effect regardless of <paramref name="window"/>.
    /// One query — the "counts via one round trip, no N+1" posture <see cref="ISponsorStore.ListAsync"/>
    /// already keeps one seam over.
    /// </summary>
    Task<IReadOnlyList<long>> ListAiringExclusionsAsync(
        IReadOnlyList<long> recentMediaIds, int window, CancellationToken ct);

    /// <summary>
    /// Claims a row for a background job by stamping <c>job_kind</c>/<c>job_started_at</c> and
    /// clearing any prior <c>job_error</c> (SPEC F174, F175; PLAN T432 — the preview/write job seam
    /// PLAN T439–T445 build against). Guarded on <c>job_kind IS NULL</c>: a row already claimed by
    /// another job reports <see cref="AdSpotJobStampResult.Busy"/> rather than stealing or queuing
    /// behind it — the caller's own signal to skip this tick, the <see cref="ClaimNextApprovedAsync"/>
    /// "SKIP LOCKED, never block" posture applied per-row instead of via a locking read.
    /// </summary>
    Task<AdSpotJobStampOutcome> StampJobAsync(long id, string kind, CancellationToken ct);

    /// <summary>
    /// Releases a job claim — clears <c>job_kind</c>/<c>job_started_at</c> and sets <c>job_error</c>
    /// to <paramref name="error"/> (<see langword="null"/> on a clean finish, the failure message
    /// otherwise) — SPEC F174, F175; PLAN T432. Total: an id with no current claim (already cleared,
    /// or never claimed) still reports <see langword="true"/> — clearing an already-clear job is a
    /// harmless no-op, not a conflict, the <see cref="MarkReadyAsync"/>/<see cref="MarkFailedAsync"/>
    /// "guarded WHERE, total" shape narrowed to "row exists" rather than "row in a specific state".
    /// Reports <see langword="false"/> only when no row exists with the given id.
    /// </summary>
    Task<bool> ClearJobAsync(long id, string? error, CancellationToken ct);

    /// <summary>
    /// Stamps a rendered preview clip's own <paramref name="path"/>/<paramref name="key"/> and
    /// <c>preview_at = now()</c> (SPEC F174; PLAN T432) — unconditional by id, mirrors
    /// <see cref="MarkReadyAsync"/>'s own total posture: a preview may be re-rendered any number of
    /// times regardless of the spot's current <see cref="AdState"/>, so there is no state guard here
    /// to conflict with. Reports <see langword="false"/> only when no row exists with the given id.
    /// </summary>
    Task<bool> StampPreviewAsync(long id, string path, string key, CancellationToken ct);

    /// <summary>
    /// <see cref="AdState.Approved"/> to <see cref="AdState.Rendering"/> for exactly the row named by
    /// <paramref name="id"/>, xmin-guarded (SPEC F174.5; PLAN T432) — the operator-driven counterpart
    /// to <see cref="ClaimNextApprovedAsync"/>'s own oldest-first, version-less worker claim: a caller
    /// here already holds a specific row's own prior <see cref="AdSpot.Version"/> (an owner previewing
    /// ONE spot on demand, not the stock worker's tick) and wants exactly that row promoted, not
    /// whichever is oldest. Returns the claimed row, or <see langword="null"/> when it is not currently
    /// <see cref="AdState.Approved"/> or <paramref name="expectedVersion"/> is stale — both collapse to
    /// one outcome, the same "re-read before trying again" contract
    /// <see cref="AdSpotTransitionOutcome.Result"/>'s own <see cref="AdSpotWriteResult.Conflict"/>
    /// gives one seam over, simplified here since no caller needs to tell the two apart.
    /// </summary>
    Task<AdSpot?> ClaimForPromotionAsync(long id, string expectedVersion, CancellationToken ct);
}
