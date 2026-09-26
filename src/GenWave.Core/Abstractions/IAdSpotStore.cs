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
/// <b>No cross-schema SQL transaction (the db/22 role boundary, SPEC F159.1's own as-built rider).</b>
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
///
/// <b><see cref="SwapRenderedMediaAsync"/> reads across that same boundary, but still never writes
/// across it (gh-#854).</b> Its own implementation resolves the old media row's current
/// eligible/never_play facts through <c>IAdminMediaLookup</c> — a <c>library_svc</c>-rooted read seam,
/// the SAME one <c>AdRenderService.ResolveBedAsync</c> already holds — from INSIDE the method that
/// also runs the guarded <c>station.ad_spot</c> UPDATE; the two never share one SQL transaction (still
/// two separate connections, two separate roles), but both run before this method reports its own
/// result. The eligibility WRITES this
/// method makes once the swap's own UPDATE commits still cross through <c>IAuthoredCatalogWriter</c>
/// exactly like every other <c>library.media</c> write in this project — the
/// <c>JinglePackRepository</c> precedent (a station-first-with-compensation write sequence spanning
/// two roles' connections inside one C# method) is the shape this method mirrors, not a novel one.
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
    /// call — the caller loops this call up to <c>AdSpotWorker.MaxRendersPerTick</c> times per tick,
    /// re-checking the on-air gate between claims; STORY-432, PLAN T456, gh-#745). Returns
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
    /// <paramref name="mediaId"/>, <paramref name="renderVersion"/> (gh-#854), and <c>rendered_at</c>
    /// — PLAN T401's own render-success seam. See this interface's own remarks for why this never
    /// opens a cross-schema transaction with the <c>library.media</c> insert that must already have
    /// happened. Total: a row not currently <see cref="AdState.Rendering"/> (already handled by a
    /// different call, or never claimed) leaves the guarded <c>WHERE</c> matching nothing — reports
    /// <see langword="false"/>, never throws.
    /// </summary>
    Task<bool> MarkReadyAsync(long id, long mediaId, int renderVersion, CancellationToken ct);

    /// <summary>
    /// <see cref="AdState.Rendering"/> to <see cref="AdState.Failed"/> (SPEC F159.2), stamping
    /// <paramref name="failReason"/> — PLAN T401's own render-failure seam (a TTS or measurement
    /// failure mid-render, distinct from STORY-390 AC3's own pre-render validation failure, which
    /// <see cref="CreateAsync"/> already covers via <see cref="NewAdSpot.InitialState"/>). Total,
    /// mirrors <see cref="MarkReadyAsync"/>'s own posture exactly.
    /// </summary>
    Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct);

    /// <summary>
    /// The oldest <see cref="AdState.Ready"/> spot whose <see cref="AdSpot.RenderVersion"/> trails
    /// <paramref name="currentVersion"/> (gh-#854) — the background re-render backlog's own read,
    /// oldest <c>state_changed_at</c> first. <paramref name="excludeIds"/> withholds spots the worker
    /// already gave up on this process lifetime (a render failure never retries in the same run) — the
    /// <see cref="ListAiringExclusionsAsync"/> array-parameter precedent, one seam over. Returns
    /// <see langword="null"/> when the backlog is empty, never an error.
    /// </summary>
    Task<AdSpot?> FindStaleReadyAsync(int currentVersion, IReadOnlyCollection<long> excludeIds, CancellationToken ct);

    /// <summary>
    /// Atomically swaps a re-rendered spot onto <paramref name="newMediaId"/>; retires
    /// <paramref name="oldMediaId"/> after commit (gh-#854). <see cref="AdState.Ready"/> never leaves
    /// <see cref="AdState.Ready"/> for this, and <c>state_changed_at</c> is deliberately left
    /// untouched — a product decision: swapping in a fresher render must never reset the
    /// refresh-retire clock <see cref="ListReadyOlderThanAsync"/> reads off that same column. Declines
    /// — reporting <see langword="false"/>, never throwing, touching neither row — when ANY guard
    /// fails:
    /// <list type="bullet">
    /// <item><paramref name="oldMediaId"/> no longer resolves, or is already ineligible, or is
    /// never_play — re-checked fresh, immediately before the swap, so only a genuine race with an
    /// operator's own hand (never a stale upfront read) can ever cause this; <c>AdSpotWorker</c> (a
    /// plain-text reference: GenWave.Core never references GenWave.Ads, L10) also runs a cheap,
    /// non-authoritative version of this same check upfront, purely to skip a wasted render, never as
    /// the guard itself, or</item>
    /// <item>the <c>station.ad_spot</c> row is no longer <c>state = 'ready' AND media_id = </c>
    /// <paramref name="oldMediaId"/> — a spot edited, retired, or already re-rendered out from under
    /// this attempt, or</item>
    /// <item>the row already carries a <c>pending_retire_media_id</c> from an EARLIER swap still
    /// waiting on its own old-media turn-off — a second swap landing before that first marker clears
    /// must never overwrite it, which would orphan the first swap's own old row with nothing left
    /// pointing back at it.</item>
    /// </list>
    /// Only when every guard holds does the implementation commit: the guarded <c>station.ad_spot</c>
    /// UPDATE stamps <c>pending_retire_media_id = oldMediaId</c> AND
    /// <c>pending_confirm_media_id = newMediaId</c> in the SAME statement (gh-#854, db/48) — two
    /// durable markers, one per row this swap touches, landing together with the swap itself. Both
    /// markers' own eventual best-effort flips (old media turned off, new media confirmed eligible) run
    /// AFTER this UPDATE commits, never inside its own transaction — <paramref name="oldMediaId"/>'s
    /// turn-off runs first, then <paramref name="newMediaId"/>'s confirm, deliberately in that order (a
    /// failure confirming new eligible leaves nothing airing from a media id nothing yet points at; the
    /// reverse order would risk the opposite). Either flip usually lands in the SAME call, immediately
    /// after commit, but a crash or a failure leaves its own marker durably pending rather than lost —
    /// <see cref="ListPendingRetiresAsync"/>/<see cref="ListPendingConfirmsAsync"/>'s own remarks cover
    /// the guaranteed invariant: every media row a swap ever touches converges eventually, even across
    /// a crash, never silently accepted as a permanent orphan or a permanent ineligible new row. A
    /// cancellation BEFORE this call is even reached is the one case that stays exactly as before: it
    /// leaves behind only a still-ineligible <paramref name="newMediaId"/> row — inert, accepted;
    /// <c>AdRenderService.RenderStaleAsync</c>'s own remarks cover that case.
    /// </summary>
    Task<bool> SwapRenderedMediaAsync(long id, long oldMediaId, long newMediaId, int renderVersion, CancellationToken ct);

    /// <summary>
    /// Every row still waiting on its own durable old-media turn-off (gh-#854, db/48's
    /// <c>pending_retire_media_id</c>) — <c>AdSpotWorker</c>'s own drain (a plain-text reference:
    /// GenWave.Core never references GenWave.Ads, L10) reads this at the top of every tick, cheap and
    /// unconditional, so a swap's post-commit flip that crashed or failed mid-flight is never left for
    /// anything but a later tick to finish. A pure read — the "never turn off a media id some
    /// ad_spot STILL points at" self-heal is a SEPARATE write, <see cref="ClearReferencedPendingRetiresAsync"/>,
    /// which the caller runs first, every tick, before this read: this method itself never mutates
    /// anything. Bounded, the same <c>MaxUnpagedRows</c> ceiling every other unpaged read in the
    /// implementation already shares.
    /// </summary>
    Task<IReadOnlyList<PendingAdSpotRetire>> ListPendingRetiresAsync(CancellationToken ct);

    /// <summary>
    /// Clears one row's own <c>pending_retire_media_id</c> once its old media is actually turned off
    /// (gh-#854) — guarded on BOTH <paramref name="id"/> AND <paramref name="mediaId"/> still matching
    /// the stamped value: a second swap landing on the SAME spot between a caller's own
    /// <see cref="ListPendingRetiresAsync"/> read and this call stamps a NEWER pending id, and this
    /// clear must never blow that away. Total: reports <see langword="false"/>, never throws, when the
    /// row's own pending id no longer matches (already cleared, or replaced by a newer swap) — the
    /// caller simply leaves it for a later drain.
    /// </summary>
    Task<bool> ClearPendingRetireAsync(long id, long mediaId, CancellationToken ct);

    /// <summary>
    /// Self-heals every pending retire marker whose old media id is now some OTHER spot's own CURRENT
    /// <see cref="AdSpot.MediaId"/> (an operator re-pointed a spot at it) — clears the marker in place,
    /// without ever attempting the flip a caller would otherwise have to skip itself (gh-#854). A
    /// write with no return value worth reporting. The caller (<c>AdSpotWorker</c>'s own
    /// drain) runs this FIRST, every tick, before <see cref="ListPendingRetiresAsync"/>.
    /// </summary>
    Task ClearReferencedPendingRetiresAsync(CancellationToken ct);

    /// <summary>
    /// Every row still waiting on its own durable new-media eligibility confirm (gh-#854, db/48's
    /// <c>pending_confirm_media_id</c>) — <see cref="ListPendingRetiresAsync"/>'s own shape, one marker
    /// over. A pure read; nothing here self-heals by re-pointing, since a confirm marker's own guard is
    /// narrower still — see <see cref="IsReadyOnMediaAsync"/>, which the caller (the confirm drain)
    /// checks per row before ever attempting the flip, rather than this method filtering rows out
    /// itself. Bounded, the same <c>MaxUnpagedRows</c> ceiling every other unpaged read in the
    /// implementation already shares.
    /// </summary>
    Task<IReadOnlyList<PendingAdSpotConfirm>> ListPendingConfirmsAsync(CancellationToken ct);

    /// <summary>
    /// Clears one row's own <c>pending_confirm_media_id</c> once its new media is actually confirmed
    /// eligible (gh-#854) — <see cref="ClearPendingRetireAsync"/>'s own guarded shape, one marker over:
    /// guarded on BOTH <paramref name="id"/> AND <paramref name="mediaId"/> still matching the stamped
    /// value, total, reports <see langword="false"/> rather than throwing when the row's own pending id
    /// no longer matches.
    /// </summary>
    Task<bool> ClearPendingConfirmAsync(long id, long mediaId, CancellationToken ct);

    /// <summary>
    /// Whether <paramref name="id"/> is currently <see cref="AdState.Ready"/> AND its own
    /// <see cref="AdSpot.MediaId"/> still equals <paramref name="mediaId"/> — the guard a confirm
    /// marker's own flip must pass before it is ever attempted (gh-#854): an operator retiring the spot
    /// between the swap's own commit and this flip must never revive a row the operator meant to pull,
    /// and a second swap already moving the row on to a THIRD media id must never confirm a media id
    /// the spot no longer even names. Shared by <see cref="SwapRenderedMediaAsync"/>'s own inline
    /// post-commit confirm attempt and the worker's later confirm-marker drain — the SAME check, run
    /// from two different call sites, never duplicated.
    /// </summary>
    Task<bool> IsReadyOnMediaAsync(long id, long mediaId, CancellationToken ct);

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
    /// clearing any prior <c>job_error</c>/<c>job_failed_kind</c> (SPEC F174, F175; PLAN T432, T463 —
    /// the preview/write job seam PLAN T439–T445 build against). Guarded on <c>job_kind IS NULL</c>: a
    /// row already claimed by another job reports <see cref="AdSpotJobStampResult.Busy"/> rather than
    /// stealing or queuing behind it — the caller's own signal to skip this tick, the
    /// <see cref="ClaimNextApprovedAsync"/> "SKIP LOCKED, never block" posture applied per-row instead
    /// of via a locking read. A fresh claim always starts with <see cref="AdSpot.JobFailedKind"/>
    /// null, even on a row whose PREVIOUS job failed — that fact only names the most recent job's own
    /// failure, and this one hasn't failed yet.
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
    ///
    /// <para>
    /// Also stamps <see cref="AdSpot.JobFailedKind"/> (PLAN T463, db/47): the kind that was just
    /// cleared (the row's own <c>job_kind</c> going into this call) when <paramref name="error"/> is
    /// non-null, else <see langword="null"/> — so a caller reading the row back can tell WHICH step
    /// (<c>"write"</c> or <c>"preview"</c>) the most recent failure happened during, without also
    /// keeping <c>job_kind</c> itself non-null once the claim is released.
    /// </para>
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

    /// <summary>
    /// The guardian's own preview-cleanup candidate read (SPEC F176.2; STORY-429; PLAN T442) — every
    /// row that currently carries a rendered preview (<c>preview_path IS NOT NULL</c>) AND either has
    /// left the editable draft/approved lifecycle (<see cref="AdState.Ready"/> or
    /// <see cref="AdState.Retired"/> — a preview of a spot no longer being worked on has nothing left
    /// to preview) OR has simply outlived <paramref name="retention"/> since it was rendered
    /// (<c>preview_at</c> older than <paramref name="now"/> minus <paramref name="retention"/>). The
    /// caller (<see cref="AdSpotLifecycleGuardianService"/>, a plain-text reference: GenWave.Core never
    /// references GenWave.Ads, L10) deletes each returned row's own file, then clears its stamp via
    /// <see cref="ClearPreviewAsync"/>.
    /// </summary>
    Task<IReadOnlyList<AdSpot>> ListPreviewsToSweepAsync(TimeSpan retention, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Clears a preview stamp — nulls <c>preview_path</c>/<c>preview_at</c>/<c>preview_key</c>
    /// together (SPEC F176.2; STORY-429; PLAN T442), total by id (the <see cref="ClearJobAsync"/>
    /// posture: an id with no preview stamped, or no row at all, still reports whatever this store's
    /// own "no matching row" case reports — see the implementation's own remarks for the exact
    /// boundary). Never touches <c>job_kind</c>/<c>job_started_at</c>/<c>job_error</c> — a preview
    /// sweep and a queued job are two independent claims on the SAME row.
    /// </summary>
    Task<bool> ClearPreviewAsync(long id, CancellationToken ct);
}
