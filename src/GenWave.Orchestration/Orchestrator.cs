namespace GenWave.Orchestration;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Plans and interleaves music tracks and TTS patter segments per <see cref="CadenceConfig"/>.
/// Maintains an internal buffer so that a single unit (back-announce + station-id + lead-in + music)
/// can be planned at once and dequeued one item at a time.
///
/// Cadence per unit:
///   1. Back-announce for the PREVIOUS track (if any, if configured)
///   2. Station-ID every N units (if configured)
///   3. Lead-in for the NEXT track (if configured)
///   4. The music track itself
///
/// TTS ids always start with "tts:"; music ids never do.  tts:* ids are stripped from the ordered
/// recent-ids list before calling <see cref="IMediaCatalog"/> so recent-repeat avoidance stays clean.
///
/// Music selection reads <paramref name="scopeProvider"/> on every call (SPEC F30.1) rather than a
/// scope stored on the station identity — this project references only <c>GenWave.Core</c>
/// and cannot see the Host's live options monitor directly, so <see cref="IStationScopeProvider"/>
/// is the thin seam it depends on instead. Never cache the read result in a field.
///
/// Cadence is read the same way, through <paramref name="cadenceProvider"/> (gitea-#211 — F30.1's
/// precedent applied to cadence): read exactly ONCE per unit, into a local, at the top of
/// <see cref="EnqueuePatterAsync"/> — not once per cadence check within that unit — so one unit is
/// planned under one consistent cadence snapshot rather than three racing live reads that could
/// straddle a concurrent settings write mid-unit.
///
/// Music selection calls <see cref="IMediaCatalog.GetRotationCandidateAsync"/> (SPEC F41.1, closes
/// gitea-#210/gitea-#213) instead of the strict-exclude <c>GetRandomReadyAsync</c> — a tiered preference query
/// that relaxes rather than drains. <paramref name="rotationProvider"/> is read fresh on every call
/// (same F30.1/gitea-#211 discipline) for the artist-separation depth passed to that tier; a relaxed
/// candidate (<see cref="RotationCandidate.RepeatedRecent"/>/<see cref="RotationCandidate.RepeatedArtist"/>)
/// logs a WARN naming which constraint gave way, and a null candidate — now genuinely "zero playable
/// rows" (F41.2) — logs a WARN naming the drain and returns null non-fatally (F6.3 stands).
///
/// <paramref name="renderBudgetProvider"/> caps how long any single TTS render may take, read fresh
/// once per unit (SPEC F44.2, gitea-#197 — the same discipline <paramref name="cadenceProvider"/> and
/// <paramref name="identityProvider"/> follow) rather than a boot-frozen <see cref="TimeSpan"/> —
/// Program.cs used to compute this once at composition-root time and hand it in as a fixed value for
/// the life of the process. A segment that exceeds the budget, faults, or returns null is silently
/// dropped; the unit continues with the next ready item (typically music).
///
/// Each segment's <see cref="SegmentRequest.Voice"/> is resolved through <paramref name="personaAccessor"/>
/// fresh per render (SPEC F35.2, F35.3, F35.5) — the active persona's voice when non-empty, else
/// the station's own default voice — never cached, so a live activate/deactivate reaches the very
/// next segment with no restart. One DB read per segment is negligible at cadence scale; this is the
/// documented design, not a shortcut to revisit later.
///
/// <see cref="SegmentRequest.PersonaName"/> is stamped from that SAME accessor read (SPEC F39.1,
/// gitea-#212) — never a second call — so <c>Voice</c> and <c>PersonaName</c> on one <see cref="SegmentRequest"/>
/// always describe the same persona, even mid-switch.
///
/// Station identity (<see cref="StationIdentity.Id"/>/<see cref="StationIdentity.Name"/>/
/// <see cref="StationIdentity.Voice"/>) is read through <paramref name="identityProvider"/> once per
/// unit, at the top of <see cref="EnqueuePatterAsync"/> (SPEC F44.1, gitea-#196, the same discipline
/// <paramref name="cadenceProvider"/> follows one line below) — never cached in a field — so a live
/// <c>Station:Name</c>/<c>Station:Voice</c> edit reaches the very next unit's segments with no
/// process restart.
///
/// The station-id cadence check (below) never builds its <see cref="SegmentRequest"/> directly
/// (SPEC F74.1/F74.2, STORY-197): it enqueues a deferral into <paramref name="deferralQueue"/>,
/// which <see cref="EnqueuePatterAsync"/> drains in the same pass. This planning pass IS the next
/// track boundary — a whole unit (back-announce/station-id/lead-in/music) is queued atomically
/// before the next track ever reaches air — so draining here can never land mid-track. Routing
/// even an always-immediately-due trigger through the queue formalizes the seam a future deferred
/// producer (e.g. a wall-clock-scheduled handoff) shares: enqueue whenever its own trigger fires,
/// drain only at a boundary.
/// </summary>
public sealed class Orchestrator(
    IStationIdentityProvider identityProvider,
    IStationScopeProvider scopeProvider,
    ICadenceProvider cadenceProvider,
    IRotationSettingsProvider rotationProvider,
    IMediaCatalog catalog,
    ITtsSegmentSource tts,
    IActivePersonaAccessor personaAccessor,
    ILogger<Orchestrator> logger,
    IRenderBudgetProvider renderBudgetProvider,
    SpeechDeferralQueue deferralQueue) : INextItemProvider
{
    readonly Queue<MediaItem> buffer = new();
    MediaItem? previousTrack;
    int unitCount;

    /// <inheritdoc/>
    public async Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
    {
        if (buffer.Count > 0) return buffer.Dequeue();

        // Strip tts:* from the recent-ids list (F12.6 discipline) so the ordered-recent list
        // GetRotationCandidateAsync tiers against stays music-only. ctx.RecentMediaIds is already
        // the feeder's ring oldest-first, most-recent LAST (SPEC F41.1) — Where preserves that order.
        var orderedRecentIds = ctx.RecentMediaIds
            .Where(id => !id.StartsWith("tts:", StringComparison.Ordinal))
            .ToList();

        // Read the live scope and artist-separation depth on every selection call — never store
        // either — so a live scope edit (SPEC F30) or rotation edit (F41.6) takes effect on the
        // very next pull with no process restart.
        var artistSeparation = rotationProvider.Current.ArtistSeparation;
        var candidate = await catalog.GetRotationCandidateAsync(
            scopeProvider.Current, orderedRecentIds, artistSeparation, ct);
        if (candidate is null)
        {
            // F41.2: null now means a GENUINE drain — zero playable rows in scope, never merely
            // "everything playable happens to be recent". Non-fatal (F6.3 stands) — the feeder
            // retries next tick — but loud, since gitea-#210's silent version of this is the bug closed.
            logger.LogWarning(
                "Rotation selection found zero playable tracks in scope — a genuine drain " +
                "(SPEC F41.2), distinct from an anti-repeat or artist-separation adjustment.");
            return null;
        }

        if (candidate.RepeatedRecent)
        {
            logger.LogWarning(
                "Anti-repeat window relaxed — playable catalog smaller than the recent window; " +
                "selected {MediaId} despite it appearing in the recent list (SPEC F41.5).",
                candidate.Media.MediaId);
        }

        if (candidate.RepeatedArtist)
        {
            logger.LogWarning(
                "Artist-separation relaxed — no track avoided the last {ArtistSeparation} artists; " +
                "selected {MediaId} with a repeated artist (SPEC F41.5).",
                artistSeparation, candidate.Media.MediaId);
        }

        var track = candidate.Media.ToMediaItem();

        await EnqueuePatterAsync(previousTrack, track, ct);
        buffer.Enqueue(track);

        previousTrack = track;
        unitCount++;

        return buffer.Dequeue();
    }

    async Task EnqueuePatterAsync(MediaItem? prev, MediaItem next, CancellationToken ct)
    {
        // Read cadence ONCE per unit, up front (gitea-#211) — so this unit's back-announce/station-id/
        // lead-in decisions all see the same snapshot even if a live PUT /api/settings edit lands
        // mid-unit. Never read cadenceProvider.Current again below this line.
        var cadence = cadenceProvider.Current;

        // Read station identity ONCE per unit too (SPEC F44.1, gitea-#196) — same discipline, same
        // reason: a live Station:Name/Station:Voice edit must not straddle a single unit's three
        // segment builds. Never read identityProvider.Current again below this line.
        var identity = identityProvider.Current;

        // Read the render budget ONCE per unit too (SPEC F44.2, gitea-#197) — same discipline again: a
        // live Tts:RenderBudgetSeconds edit must not straddle a single unit's renders. Never read
        // renderBudgetProvider.Current again below this line.
        var renderBudget = renderBudgetProvider.Current;

        // Each segment's voice+persona-name pair is resolved (a fast, local accessor call — SPEC
        // F35.3, F39.1) immediately before that segment's SegmentRequest is built, so the actual TTS
        // renders below still all kick off back-to-back with no render awaited in between
        // (render-ahead is unaffected — the accessor call is negligible next to a real render's
        // synthesis+mix+measure latency). ResolvePersonaAsync reads personaAccessor exactly ONCE per
        // call, returning both values from the same read (F39.1) — never resolve Voice and
        // PersonaName from two separate accessor calls, which could straddle a concurrent
        // activate/deactivate and pair a stale name with a fresh voice or vice versa.
        var pendingRenders = new List<Task<MediaItem?>>();

        // 1. Back-announce for the previous track
        if (cadence.BackAnnounceAfterEachTrack && prev is not null)
        {
            var (voice, personaName) = await ResolvePersonaAsync(identity.Voice, ct);
            var req = new SegmentRequest(
                SegmentKind.BackAnnounce,
                voice,
                identity.Name,
                prev,
                DateTimeOffset.UtcNow,
                identity.Id,
                personaName);
            pendingRenders.Add(tts.RenderAsync(req, ct));
        }

        // 2. Station ID every N units (checked BEFORE incrementing unitCount). unitCount > 0 joins
        // the guard (SPEC F42.1, STORY-136, closes gitea-#216): the FIRST station ID airs only once N
        // units have elapsed, never at boot — unitCount == 0 % N == 0 used to fire on the very
        // first unit, which is exactly the boot-blast this guard now excludes.
        //
        // The trigger no longer builds the segment itself (SPEC F74.1/F74.2, STORY-197): it
        // enqueues a deferral, and the drain immediately below picks it up in this SAME boundary
        // pass (see class remarks for why that is still "never mid-track"). Supersede (F74.2) is
        // the queue's job, not this check's — a second same-kind enqueue before the next drain
        // would simply replace this one.
        if (cadence.StationIdEveryNUnits > 0
            && unitCount > 0
            && unitCount % cadence.StationIdEveryNUnits == 0)
        {
            deferralQueue.Enqueue(SpeechDeferralKind.StationId, "cadence: Station:Cadence:StationIdEveryNUnits");
        }

        // Drain every deferral due at this boundary. Today the only producer is the cadence check
        // just above, always due "now" — but this loop is written for ANY due deferral, including
        // one enqueued by a future producer several units ago (SPEC F74.1 — "regardless of
        // wall-clock slip").
        foreach (var deferral in deferralQueue.TryDequeueDue(DateTimeOffset.UtcNow))
        {
            if (deferral.Kind != SpeechDeferralKind.StationId) continue; // only kind wired so far

            var (voice, personaName) = await ResolvePersonaAsync(identity.Voice, ct);
            var req = new SegmentRequest(
                SegmentKind.StationId,
                voice,
                identity.Name,
                null,
                DateTimeOffset.UtcNow,
                identity.Id,
                personaName);
            pendingRenders.Add(tts.RenderAsync(req, ct));
        }

        // 3. Lead-in for the next track
        if (cadence.LeadInBeforeEachTrack)
        {
            var (voice, personaName) = await ResolvePersonaAsync(identity.Voice, ct);
            var req = new SegmentRequest(
                SegmentKind.LeadIn,
                voice,
                identity.Name,
                next,
                DateTimeOffset.UtcNow,
                identity.Id,
                personaName);
            pendingRenders.Add(tts.RenderAsync(req, ct));
        }

        // Await each render with the budget; skip any that time out, fault, or return null.
        foreach (var renderTask in pendingRenders)
        {
            try
            {
                var winner = await Task.WhenAny(renderTask, Task.Delay(renderBudget, ct));
                if (winner == renderTask && renderTask.IsCompletedSuccessfully && renderTask.Result is { } seg)
                    buffer.Enqueue(seg);
                // else: timeout or cancellation → silently skip this segment
            }
            catch
            {
                // renderTask faulted → skip silently
            }
        }
    }

    /// <summary>
    /// Resolves the voice AND persona name for one segment render from a SINGLE
    /// <paramref name="personaAccessor"/> read (SPEC F35.3, F39.1) — never two separate calls, so
    /// the returned pair always describes the same persona even mid-switch. Voice is the active
    /// persona's voice when non-empty, else <paramref name="stationVoice"/> (SPEC F44.1 — the
    /// caller's single per-unit <see cref="IStationIdentityProvider"/> read, never a second live
    /// read from in here); persona name is the active persona's <see cref="Persona.Name"/> whenever
    /// a persona resolved (regardless of whether its own <see cref="Persona.Voice"/> is the empty
    /// sentinel), else <see langword="null"/>.
    ///
    /// Re-read fresh per call — never cached in a field — so a live activate/deactivate (F35.5)
    /// reaches the very next segment. <paramref name="personaAccessor"/>'s own contract never
    /// throws, but this Orchestrator stays defensive per F12.4 regardless: any unexpected fault
    /// still degrades to <c>(stationVoice, null)</c> rather than costing the segment.
    /// </summary>
    async Task<(string Voice, string? PersonaName)> ResolvePersonaAsync(string stationVoice, CancellationToken ct)
    {
        try
        {
            var persona = await personaAccessor.ResolveAsync(ct);
            if (persona is not null)
            {
                var voice = string.IsNullOrEmpty(persona.Voice) ? stationVoice : persona.Voice;
                return (voice, persona.Name);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Falls through to (stationVoice, null) below — an accessor fault must never cost the slot.
        }

        return (stationVoice, null);
    }
}
