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
    /// <c>state_changed_at desc, id desc</c> — newest-transitioned-first, so a fresh batch of drafts
    /// or a just-failed spot needing triage surfaces at the top regardless of when the row was
    /// originally created. <paramref name="limit"/>/<paramref name="offset"/> are floored by the
    /// implementation (the <c>RotFindingRepository.ClampPaging</c> precedent) — never trust every
    /// caller to have already clamped them.
    /// </summary>
    Task<AdSpotPage> ListByStateAsync(AdState? state, int limit, int offset, CancellationToken ct);

    /// <summary>
    /// How many generated spots (<see cref="AdSource.Llm"/> or <see cref="AdSource.Pack"/> source) sit
    /// anywhere in the stock pipeline — <see cref="AdState.Draft"/>, <see cref="AdState.Approved"/>,
    /// <see cref="AdState.Rendering"/>, or <see cref="AdState.Ready"/> (SPEC F159.3's own
    /// <c>Station:Ads:TargetCount</c> stock count, as-built rider gh-#689). A draft waiting for the
    /// owner's eye under <c>AutoApprove=false</c> IS stock on its way — counting only the ready shelf
    /// left that pile unbounded (one new draft per tick, forever). <see cref="AdState.Failed"/> never
    /// counts (it waits for an operator retry or discard and must never block refill),
    /// <see cref="AdState.Retired"/> is terminal, and <see cref="AdSource.Owner"/> spots never count
    /// toward the target the stock pass refills.
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
}
