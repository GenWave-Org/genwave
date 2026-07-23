using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;

namespace GenWave.Core.Playout;

/// <summary>
/// Keeps the engine's queue fed and self-heals after restarts or drains by reconciling to the engine —
/// the source of truth — every tick (PRD §11). Pull-based and position-free: it pulls the next track
/// through <see cref="INextItemProvider"/> instead of indexing a playlist, so there is no position
/// bookkeeping to recover. Pure logic: it holds the cross-tick state and takes only abstractions, so it
/// unit-tests with no socket, DB, or audio device. The owning <c>BackgroundService</c> supplies the
/// timer and the try/catch.
/// </summary>
/// <param name="ls">The engine control-plane seam.</param>
/// <param name="next">The selection seam (SEAM 1) the feeder pulls the next item through.</param>
/// <param name="rotationProvider">
/// The live source of the anti-repeat ring's capacity (SPEC F41.6) — read fresh on every ring write
/// (<see cref="Remember"/>), never cached in a field, so a live <c>Station:Rotation:RecentWindow</c>
/// edit trims (or grows) the ring on the very next advance with no process restart. Replaces the
/// old hardcoded <c>recentCapacity</c> ctor default.
/// </param>
/// <param name="targetLufs">Loudness target (config tunable; defaults to PRD §10).</param>
/// <param name="ceilingDbtp">True-peak ceiling (config tunable; defaults to PRD §10).</param>
/// <param name="events">
/// The event seam a real track-id advance is published through as <see cref="TrackAired"/>
/// (gitea-#246 — replaces the single-cast <c>OnAdvance</c> Action, whose second subscriber silently
/// overwrote the first). Defaults to <see cref="NoOpStationEventSink"/>; the host binds the sink
/// that pushes play history.
/// </param>
public sealed class PlayoutFeeder(
    ILiquidsoapControl ls,
    INextItemProvider next,
    IRotationSettingsProvider rotationProvider,
    double targetLufs = -16.0,
    double ceilingDbtp = -1.0,
    IStationEventSink? events = null)
{
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    /// <summary>Convention shared with the Orchestrator: TTS segment ids start with this, music never does.</summary>
    const string TtsIdPrefix = "tts:";

    /// <summary>Refill stops here even if the provider never yields music — a misbehaving provider
    /// must not make a single tick push unbounded TTS into the engine.</summary>
    const int MaxChainLength = 8;

    /// <summary>Claim (d)'s (pendingAirQueue) size-cap multiplier, applied over
    /// <c>max(MaxChainLength, RecentWindow)</c> in <see cref="MarkPendingAir"/> — gh-#88's
    /// last-resort backstop against an id that genuinely never airs.</summary>
    const int PendingAirCapMultiplier = 4;

    // The one-ahead CHAIN: keep on-air + one prepared chain of [0..n TTS segments, 1 music track].
    // A TTS segment airs for seconds — shorter than the tick interval — so preparing one item at a
    // time guarantees the queue runs dry between segments and the safe loop blips on air (gitea-#155).
    // Pushing through to the next music track keeps the queue non-empty across every TTS boundary,
    // and the minutes-long music tail restores the comfortable refill margin.
    readonly Queue<string> recent = new();
    readonly HashSet<string> chainIds = new(StringComparer.Ordinal);

    // Last-pushed item metadata: retained so the TrackAired event can supply title/artist/gainDb/
    // durationMs/personaPick when a new track_id comes on-air (the feeder doesn't receive the
    // MediaItem on advance, only at push). DurationMs is stamped from the MediaItem at push time
    // (SPEC F50.2) — an engine-initiated entry (populated from ExtractAnnotations below) always
    // carries null, since duration never rides the annotate line. PersonaPick (SPEC F82.6/F83.1/
    // F86.1, PLAN T73) rides the same push-time capture: an engine-initiated entry always carries
    // null too — the feeder never pushed it, so there was never a persona pick behind it.
    //
    // Lifetime is decoupled from the anti-repeat ring (SPEC F57.1 — closes gitea-#229): an entry
    // survives while its id is (a) the current on-air id, (b) a member of the current pushed chain,
    // (c) present in ANY slot of the `recent` ring, or (d) pushed by this feeder with that push's
    // airing not yet PROVEN (pendingAirQueue, gh-#88) — released via ReleaseIfDead the moment none
    // of the four hold. Claim (d) exists because (c) alone is bounded by RecentWindow and can evict
    // an id long before a backlogged engine queue actually airs it: without (d), a pushed id whose
    // air-lag outlasts the ring falls through to the F57.4 echo branch below, mistaken for an
    // engine-initiated advance, nulling its PersonaPick (the T73 booth-log stamp silently lost).
    // Bare-id eviction keyed only to a ring dequeue stays retired: an id occupying multiple ring
    // slots keeps its metadata until the LAST occurrence leaves. See pendingAirQueue below for how
    // claim (d) itself stays bounded — it is not simply cleared on the id's OWN observed advance.
    readonly Dictionary<string, (string? Title, string? Artist, double GainDb, int? DurationMs, PersonaPickDiagnostics? PersonaPick)> pushedMeta
        = new(StringComparer.Ordinal);

    // Media ids whose pushedMeta entry is feeder-authoritative — set at PushAsync time from the
    // exact MediaItem we queued, so TickAsync must never overwrite it with an engine-output guess.
    // Anything NOT in this set that reaches pushedMeta got there via EngineMetadata.ExtractAnnotations
    // on an engine-initiated advance (safe rotation, which the feeder never pushes) and is refreshed
    // EVERY time that id comes back on-air — not just the first — so a transient miss on one
    // occurrence self-heals on the next poll of the same track instead of sticking forever
    // (F24.1/F29.9, gitea-#192). Shares the F57.1 liveness lifetime documented on pushedMeta above.
    // Also drives the refill no-op below (SPEC F57.1(d), gh-#88): an advance onto ANY id still
    // here — not just the CURRENT chain — is this feeder's own backlog self-draining, not foreign.
    readonly HashSet<string> feederOwnedIds = new(StringComparer.Ordinal);

    // Claim (d) of the F57.1 liveness rule (gh-#88): media ids this feeder has pushed whose airing
    // has not yet been PROVEN, in PUSH order (FIFO — the engine plays a pushed queue strictly in the
    // order it was queued). Deliberately NOT a HashSet keyed only to the id's OWN observed advance:
    // a TTS segment routinely airs and departs entirely between two polls (PlayoutFeederService's
    // tick interval vs. the push-time-ring rationale on Remember below) — its OWN advance is never
    // individually observed, so a same-id-only removal would pin it forever. Three release paths,
    // all below: DrainPendingAirUpTo (the normal path — a later, observed sibling further along in
    // push order proves an unobserved predecessor aired too), ReleasePriorChainPendingAir (fires ONLY
    // on a confirmed drain — a foreign-but-real advance does NOT prove our own backlog is gone, so
    // must never strip it: that would re-break the very pick-survives-backlog case this claim
    // exists for), and a live size cap in MarkPendingAir (last-resort backstop for an id that
    // genuinely never airs — an engine flush wiping the queue). FIFO order is also what keeps a
    // repeat push of the same id exactly-once: see DrainPendingAirUpTo's ordering note.
    readonly Queue<string> pendingAirQueue = new();

    string? onAirId;
    string? chainEndId; // the chain's final pushed id (music) — its airing means nothing is queued behind
    bool onAirIsReal;   // is the current on-air item one of ours? false while safe rotation airs
    bool booted;
    int prepared;
    DateTimeOffset onAirStartedAt;  // wall-clock instant the current on-air item was first detected

    /// <summary>
    /// The feeder's current on-air state, updated at the end of each completed tick.
    /// Null until the feeder has completed its first successful tick (cold-start).
    /// Read by the Host layer (<c>PlayoutFeederService</c>) without issuing engine telnet calls.
    /// </summary>
    public OnAirState? CurrentOnAir { get; private set; }

    public async Task TickAsync(CancellationToken ct)
    {
        var id = await ls.OnAirNewestAsync(ct);     // stamped media id, drain token, or null
        if (id is null) return;                       // nothing resolved yet (cold start)

        // The id that just stopped being on-air, when this tick observed an advance — re-checked for
        // liveness (F57.1) once this tick's chain/ring writes have settled, so an id that just lost
        // its last liveness claim (on-air) is released here rather than leaking forever.
        string? departedOnAirId = null;

        if (id != onAirId)                            // boot, a real advance, or a drain transition
        {
            var advancedAt = DateTimeOffset.UtcNow;
            departedOnAirId = onAirId;
            onAirId = id;
            onAirStartedAt = advancedAt;
            var meta = await ls.MetadataAsync(id, ct);
            onAirIsReal = meta.TryGetMediaId(out var mediaId);   // our stamped id present?
            if (onAirIsReal)
            {
                // Feeder-pushed ids (feederOwnedIds) already joined the ring at push time and got
                // their feeder-authoritative pushedMeta entry then (SPEC F57.3) — an observed advance
                // onto one must NOT re-enqueue it here (exactly-once per airing) or overwrite its
                // metadata with an engine-output guess (F57.2, gitea-#219). Only an engine-initiated
                // advance — the feeder never pushed this id — reaches this branch: it joins the ring
                // NOW, at first observed advance, and has its metadata (re)populated from the output
                // annotate line every time it recurs, not just the first, so a transient miss on one
                // occurrence self-heals the next time the track comes back on-air instead of sticking
                // forever (F24.1/F29.9, gitea-#192). Missing or unparseable fields degrade to null/0
                // (F7.4). DurationMs is always null here — an engine-initiated play never carries a
                // fabricated duration (SPEC F50.2).
                //
                // F57.4 NARROWING (Epic Z / Z1 review ruling 2026-07-15, gh-#5) — NOT a defect:
                // this guard means "not CURRENTLY feeder-owned", not "never pushed". An id still
                // ring-live from a feeder push that later airs ENGINE-INITIATED (e.g. safe rotation
                // drawing the same catalog track) skips this branch and keeps serving its pushed
                // metadata — which is correct and strictly more complete (real durationMs,
                // full-precision gainDb). The engine-echo self-heal above therefore applies to ids
                // not currently feeder-owned; the still-owned entry releases on on-air departure,
                // and never-silent is untouched either way.
                if (!feederOwnedIds.Contains(mediaId))
                {
                    Remember(mediaId);
                    var (title, artist, gainDb) = meta.ExtractAnnotations();
                    pushedMeta[mediaId] = (title, artist, gainDb, DurationMs: null, PersonaPick: null);
                }
                else
                {
                    // Claim (d) is satisfied for mediaId — drain the pending queue up to (and
                    // including) it (see DrainPendingAirUpTo), which also proves every earlier,
                    // still-pending push aired too, even one whose own advance was never
                    // individually observed (SPEC F57.1(d), gh-#88).
                    DrainPendingAirUpTo(mediaId);
                }

                // Publish the advance (e.g. for PlayHistoryService's sink). The event carries only
                // Core-friendly primitives — no Host types cross this seam.
                {
                    pushedMeta.TryGetValue(mediaId, out var pm);
                    events.Publish(new TrackAired(mediaId, pm.Title, pm.Artist, pm.GainDb, advancedAt, pm.DurationMs, pm.PersonaPick));
                }
            }

            if (!booted)
            {
                // First reconciliation: NEVER trust the airing item to imply a filled queue. Since
                // Epic K (F21.4) safe-rotation tracks are first-class annotated pushes carrying a
                // track_id, so "a track_id is airing" cannot distinguish a real pushed track (api
                // restart mid-chain, queue actually filled) from a drain the engine is covering
                // (fresh boot, queue empty). Trusting a safe track deadlocks the feeder: prepared
                // stays 1, and a single-segment safe rotation replays the SAME id forever, so no
                // foreign-id advance ever corrects the guess — observed live 2026-07-12: seven
                // minutes of drain with 9,086 ready tracks, broken only by a manual q.push. The
                // old "eventual foreign-id advance recovers" escape hatch depends on id VARIETY
                // the safe rotation does not guarantee. Refilling immediately instead costs at
                // most one extra queued chain after an api restart mid-music — genuinely bounded
                // and self-draining now that the no-op rule below recognizes ANY id this feeder
                // still owns, not just the current chain (SPEC F57.1(d), gh-#88). That claim was
                // FALSE pre-fix: the old chain-only no-op read every backlogged advance as foreign,
                // compounding into an unbounded engine queue (345 requests observed live). Never-
                // silent (F1.3) outranks queue tidiness. Amends F7.5 (2026-07-12); gh-#88 (2026-07-22).
                booted = true;
                prepared = 0;
            }
            else if (!onAirIsReal || !feederOwnedIds.Contains(mediaId) || mediaId == chainEndId)
            {
                // Refill when: drained to safe; an id we never pushed airs (restart/foreign); or
                // this feeder's own chain-end id (music) reached air — everything WE queued is now
                // consumed. Keyed on feederOwnedIds (SPEC F57.1(d), gh-#88), NOT chainIds: an
                // advance onto ANY id this feeder still owns is a no-op, even from an EARLIER chain
                // sitting in a backlog (a metadata flap or api restart mid-chain seeds one). The
                // pre-fix chainIds-only key recognized only the CURRENT chain, so every backlogged
                // advance read as foreign and pushed a new chain per advance — an unbounded queue
                // (345 requests observed live). Landing on the chain-end id still forces a refill:
                // that specifically means everything WE queued has now aired.
                prepared = 0;
            }
        }
        else if (!onAirIsReal)
        {
            // Safe rotation is STILL airing: whatever we pushed last tick never made it to air
            // (lost push, failed resolution). The drain token never changes, so without this the
            // feeder would believe prepared==1 forever — the deadlock observed live. Safe-rotation
            // airing means nothing is prepared, period (PRD §6.3): retry the refill every tick
            // until real content airs.
            prepared = 0;
        }

        if (prepared == 0)                            // (re)fill the one-ahead chain
        {
            // The chain about to be discarded held its members "queued" (F57.1(b)); once cleared,
            // re-evaluate each one's liveness — an id that already lost its other claims (e.g. a TTS
            // segment that aired and passed under Station:Rotation:RecentWindow=0, where the ring
            // never holds anything, F57.2) is released here instead of leaking until some future
            // ring write that may never come.
            var staleChain = chainIds.ToArray();
            chainIds.Clear();
            chainEndId = null;

            // Chain reset happens for one of two reasons: a CONFIRMED drain (!onAirIsReal — no
            // track_id airing at all right now, so the engine's queue is verified empty and safe
            // rotation is covering it), or a FOREIGN-but-real advance (some other real track_id is
            // airing that we never pushed). Only the confirmed-drain case proves staleChain is
            // abandoned (gh-#88): an empty queue cannot possibly still hold anything of ours, so
            // whatever of staleChain never got proven aired via the normal DrainPendingAirUpTo path
            // above never will now — strip claim (d) up front, or ReleaseIfDead below can't release
            // it. A foreign-but-real advance proves nothing about OUR queue's contents either way —
            // it is NOT evidence staleChain was abandoned (this is Fix 1's entire premise: see the
            // refill no-op above) — so it must NOT strip. Stripping on a foreign advance was gh-#88
            // review round 2's own near-miss: it would have re-broken the pick-survives-backlog
            // repro this claim exists to fix.
            if (!onAirIsReal) ReleasePriorChainPendingAir(staleChain);
            foreach (var staleId in staleChain) ReleaseIfDead(staleId);

            for (var i = 0; i < MaxChainLength; i++)
            {
                // Tolerant pull: null is non-fatal — retry next tick; the safe rotation covers the
                // gap (PRD §4.1). The inter-service call is never on the hard-real-time path.
                var item = await next.GetNextAsync(new PlayoutContext(Snapshot()), ct);
                if (item is null) break;

                var gainDb = Gain.NormGainDb(item.Loudness, targetLufs, ceilingDbtp);
                await ls.PushAsync(item, gainDb, ct);
                // Stamped from the MediaItem we already hold — zero DB reads per poll (SPEC F50.2,
                // F16.6 stands). item.DurationMs carries the tts:* segment's measured cue-derived
                // duration (F66.1) or the catalog's stored value for music; only an engine-initiated
                // advance (elsewhere in this method) is null, rehydrated later at the Host layer (F66.2).
                pushedMeta[item.MediaId] = (item.Title, item.Artist, gainDb, item.DurationMs, item.PersonaPick);
                feederOwnedIds.Add(item.MediaId);
                MarkPendingAir(item.MediaId);   // claim (d) starts here (SPEC F57.1(d), gh-#88)
                chainIds.Add(item.MediaId);
                chainEndId = item.MediaId;
                prepared = 1;

                // Join the anti-repeat ring NOW, at push time — not at first observed advance (SPEC
                // F57.3, closes gitea-#220): a track shorter than the poll interval can complete before
                // any tick ever observes it on-air, so waiting for that observation could let the
                // same id repeat inside one poll window. Chain membership (F57.1(b), just above)
                // already keeps this entry's metadata alive regardless of what the trim below evicts.
                Remember(item.MediaId);

                // Music ends the chain; keep pulling while the provider yields TTS segments.
                if (!item.MediaId.StartsWith(TtsIdPrefix, StringComparison.Ordinal)) break;
            }
        }

        if (departedOnAirId is not null) ReleaseIfDead(departedOnAirId);

        // Publish the updated on-air state so the Host layer can serve it without a telnet call.
        // We only reach this point when id was non-null (the early return above guards cold-start).
        string? currentMediaId = onAirIsReal ? onAirId : null;
        (string? Title, string? Artist, double GainDb, int? DurationMs, PersonaPickDiagnostics? PersonaPick) currentMeta = currentMediaId is not null
            ? pushedMeta.GetValueOrDefault(currentMediaId)
            : default;

        CurrentOnAir = new OnAirState(
            MediaId: currentMediaId,
            Title: currentMeta.Title,
            Artist: currentMeta.Artist,
            GainDb: currentMeta.GainDb,
            StartedAt: onAirStartedAt,
            DurationMs: currentMeta.DurationMs,
            IsReal: onAirIsReal,
            IsReady: true);
    }

    // Enqueues mediaId into the anti-repeat ring and trims to the live capacity (SPEC F41.6, read
    // fresh from rotationProvider on EVERY write — never cached in a field, so a shrunk
    // Station:Rotation:RecentWindow trims the ring on THIS write and a grown one simply stops
    // evicting sooner; RecentWindow == 0 empties the ring immediately). Called either at push time
    // (feeder-owned ids) or at first observed advance (engine-initiated ids) — never both for the
    // same airing (SPEC F57.3, exactly-once). Ring eviction no longer implies eviction of the id's
    // cached metadata: ReleaseIfDead only clears it once none of the F57.1 liveness claims still hold.
    void Remember(string mediaId)
    {
        recent.Enqueue(mediaId);

        var capacity = rotationProvider.Current.RecentWindow;
        while (recent.Count > capacity)
        {
            var evicted = recent.Dequeue();
            ReleaseIfDead(evicted);
        }
    }

    // Enqueues mediaId onto claim (d)'s pending-air queue (pendingAirQueue above) and trims to a
    // live cap: a generous multiple of chain depth or the ring — comfortably above any legitimate
    // multi-chain backlog (gh-#88's live incident queued 345) — so an id that genuinely never airs
    // at all (e.g. an engine flush wiping the queued backlog) still releases eventually, oldest
    // first, instead of pinning forever. Read fresh, like Remember's ring capacity, for the same
    // live-tunability reason.
    void MarkPendingAir(string mediaId)
    {
        pendingAirQueue.Enqueue(mediaId);

        var cap = Math.Max(MaxChainLength, rotationProvider.Current.RecentWindow) * PendingAirCapMultiplier;
        while (pendingAirQueue.Count > cap)
        {
            var evicted = pendingAirQueue.Dequeue();
            ReleaseIfDead(evicted);
        }
    }

    // Claim (d)'s normal release path: an OBSERVED advance onto mediaId proves every earlier,
    // still-pending push ahead of it has aired too — the engine plays a pushed queue strictly in
    // the FIFO order it was queued — so this un-pins a TTS segment whose OWN advance was never
    // individually caught between polls (gh-#88): the item that follows it eventually IS observed,
    // and draining up to THAT id releases the segment retroactively. A no-op if mediaId isn't
    // queued (already drained by an earlier match, or capped away in MarkPendingAir), so a stale
    // echo can never over-drain entries queued after it.
    //
    // Every dequeued id — predecessors AND mediaId itself — gets a ReleaseIfDead check, exactly
    // like Remember's and MarkPendingAir's own eviction loops (gh-#88 review round 2): a mid-chain
    // no-op advance runs NO chain-reset sweep (staleChain stays untouched), so a drained predecessor
    // from an EARLIER, already-superseded chain — one whose ring slot a small RecentWindow has since
    // evicted too — would otherwise leave pendingAirQueue as its LAST liveness claim, dropped here
    // with nothing left to ever release pushedMeta/feederOwnedIds for it. Checking mediaId itself is
    // safe and cheap: it is the current on-air id, so it survives via claim (a) regardless.
    //
    // FIFO order is also what keeps a repeat push of the SAME id exactly-once: the earliest
    // still-pending occurrence always sits nearest the front, so this always resolves the OLDEST
    // unobserved occurrence first. A later, independent re-push of the same id enqueues its own
    // fresh entry BEHIND the current one, so it is never touched by a drain that has already
    // finished — see MarkPendingAir, called strictly after any drain in the same tick.
    void DrainPendingAirUpTo(string mediaId)
    {
        if (!pendingAirQueue.Contains(mediaId)) return;

        while (pendingAirQueue.Count > 0)
        {
            var drained = pendingAirQueue.Dequeue();
            ReleaseIfDead(drained);
            if (drained == mediaId) break;
        }
    }

    // Called only for a staleChain the caller has confirmed GENUINELY abandoned — a drain, not a
    // foreign-but-real advance (see the call site) — so whatever of it never got proven aired via
    // the normal DrainPendingAirUpTo path never will now. Strips it from claim (d) so ReleaseIfDead's
    // pass just after can actually release it (gh-#88). A no-op in the common case: an ordinary
    // chain-end advance has already drained the whole chain via DrainPendingAirUpTo above.
    void ReleasePriorChainPendingAir(IReadOnlyCollection<string> staleChain)
    {
        if (staleChain.Count == 0 || pendingAirQueue.Count == 0) return;

        var stale = new HashSet<string>(staleChain, StringComparer.Ordinal);
        if (!pendingAirQueue.Any(stale.Contains)) return;

        var survivors = pendingAirQueue.Where(id => !stale.Contains(id)).ToArray();
        pendingAirQueue.Clear();
        foreach (var survivor in survivors) pendingAirQueue.Enqueue(survivor);
    }

    // SPEC F57.1's liveness rule: mediaId's cached metadata is retained while it is (a) the current
    // on-air id, (b) a member of the current pushed chain, (c) present in ANY slot of the ring, or
    // (d) a feeder push whose own airing has not yet been proven (pendingAirQueue, gh-#88) —
    // checked by membership, not by count, so an id occupying multiple ring slots survives an older
    // occurrence's eviction (gitea-#229), and a backlogged push survives ring eviction until it airs.
    bool IsLive(string mediaId) =>
        mediaId == onAirId || chainIds.Contains(mediaId) || recent.Contains(mediaId) || pendingAirQueue.Contains(mediaId);

    // Removes mediaId's cached metadata once none of the F57.1 liveness claims hold. A no-op while
    // any claim still stands, or if the id was never cached to begin with.
    void ReleaseIfDead(string mediaId)
    {
        if (IsLive(mediaId)) return;
        pushedMeta.Remove(mediaId);
        feederOwnedIds.Remove(mediaId);
    }

    IReadOnlyList<string> Snapshot() => recent.ToArray();
}
