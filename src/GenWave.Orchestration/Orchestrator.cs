namespace GenWave.Orchestration;

using System.Globalization;
using Microsoft.Extensions.Logging;
using GenWave.Abstractions.Playout;
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
/// <para>
/// Every music pick is envelope-aware (SPEC F81, STORY-212): <see cref="SelectEnvelopeAwareCandidateAsync"/>
/// is the seam that has replaced the direct <see cref="IMediaCatalog.GetRotationCandidateAsync"/> call
/// sites below. It tries <paramref name="personaPickProvider"/> first (rung 0 — a no-op today, SPEC
/// F81.2: playout never depends on the persona layer existing; PLAN T64 wires a real ranker in), then
/// falls back to the by-construction-filtered <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/>
/// with <paramref name="envelopeProvider"/>'s live envelope (read fresh per pick, same F30.1
/// discipline). Whatever either source returns is re-checked against the envelope (trust-but-verify,
/// SPEC F81.5) before this Orchestrator trusts it. When the envelope-constrained pool is genuinely
/// empty, a degradation ladder (SPEC F81.6) relaxes rotation, then energy, then genres — each rung
/// logging a loud WARN naming what gave way — before falling back to the plain
/// <see cref="IMediaCatalog.GetRotationCandidateAsync"/> query as the final never-silence rung. See
/// <see cref="SelectEnvelopeAwareCandidateAsync"/>'s own remarks for the full rung order.
/// </para>
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
///
/// Music selection is boundary-aware (SPEC F74.3, STORY-198): <see cref="SelectMusicCandidateAsync"/>
/// checks <paramref name="deferralQueue"/>'s <see cref="SpeechDeferralQueue.NextDue"/> before every
/// pick and, only when something is due strictly in the future within
/// <paramref name="boundaryBiasProvider"/>'s lookahead window, softly biases the pick toward
/// whichever sampled candidate's end lands closest to that due time — never a hard filter, and
/// subordinate to rotation (F41.1/F41.3 tiering still governs which candidates even get sampled).
/// Outside that window (the common case today — see that method's remarks) this degrades to
/// exactly the one <see cref="IMediaCatalog.GetRotationCandidateAsync"/> call this Orchestrator
/// has always made.
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
    SpeechDeferralQueue deferralQueue,
    TimeProvider timeProvider,
    IBoundaryBiasProvider boundaryBiasProvider,
    IEnvelopeProvider? envelopeProvider = null,
    IPersonaPickProvider? personaPickProvider = null) : INextItemProvider
{
    /// <summary>
    /// How many independent rotation-tiered samples <see cref="SelectMusicCandidateAsync"/> draws
    /// when the boundary-bias window is active (SPEC F74.3) — enough to see a few distinct tier-1
    /// rows in even a modest library without turning every biased pick into a database hot loop.
    /// </summary>
    const int BoundarySampleAttempts = 5;

    /// <summary>
    /// SPEC F82.6 — v1's per-pick debug line names the envelope that governed the pick. v1 ships
    /// exactly one 24/7 station-default envelope (SPEC F81.3, no schedule grid) so this is a fixed
    /// sentinel rather than a field on <see cref="SegmentEnvelope"/> itself, which carries no id.
    /// </summary>
    const string EnvelopeId = "station-default";

    // SPEC F81.6's degradation-step vocabulary — the per-pick debug line's sixth field. "None" covers
    // both a winning rung-0 persona pick AND a rung-1 (unrelaxed) envelope-only pick: neither gave up
    // anything the envelope originally asked for.
    const string DegradationStepNone = "none";
    const string DegradationStepRotation = "rotation";
    const string DegradationStepEnergy = "energy";
    const string DegradationStepGenres = "genres";
    const string DegradationStepTerminal = "terminal";

    // Defaults (SPEC F81.2/F81.3): every pre-F81 test/module construction site keeps compiling and
    // behaving exactly as before — no envelope constraint, no persona layer — mirrors the
    // IStationEventSink? events = null → NoOpStationEventSink.Instance idiom used elsewhere in this
    // codebase (e.g. GenWave.Tts.TtsSegmentSource).
    readonly IEnvelopeProvider envelopeProvider = envelopeProvider ?? StationDefaultEnvelopeProvider.Instance;
    readonly IPersonaPickProvider personaPickProvider = personaPickProvider ?? NoOpPersonaPickProvider.Instance;

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
        var candidate = await SelectMusicCandidateAsync(
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

        // Carries SPEC F82.6/F83.1's persona-pick diagnostics from the selection-time RotationCandidate
        // onto the playout-facing MediaItem (T65's staged carrier — see RotationCandidate.PersonaPick's
        // own remarks) — null for every envelope-only pick, including the common persona-off case.
        if (candidate.PersonaPick is { } personaPickDiagnostics)
            track = track with { PersonaPick = personaPickDiagnostics };

        await EnqueuePatterAsync(previousTrack, track, ct);
        buffer.Enqueue(track);

        previousTrack = track;
        unitCount++;

        return buffer.Dequeue();
    }

    /// <summary>
    /// Picks the next music candidate — SPEC F41.1/F41.3 tiering is unchanged and still governs
    /// which candidates are even eligible — and, when a pending deferral
    /// (<see cref="SpeechDeferralQueue.NextDue"/>) is due strictly in the future within
    /// <see cref="boundaryBiasProvider"/>'s lookahead window (SPEC F74.3, STORY-198), softly biases
    /// that pick toward whichever sampled candidate's end lands closest to the due time.
    ///
    /// <para>
    /// A due-now-or-overdue deferral (the only shape today's cadence producer ever enqueues, per
    /// SPEC F74.1) takes the plain unbiased path below: it drains at THIS boundary regardless of
    /// which track follows next, so there is no future "land the end near due" moment left to aim
    /// for. The bias only ever activates for a deferral a producer enqueued AHEAD of its own due
    /// time — no such producer exists yet (STORY-198 builds the seam a future wall-clock-scheduled
    /// one, e.g. a show handoff, will use).
    /// </para>
    ///
    /// <para>
    /// Soft bias, never a filter (AC2): every sample stays eligible for selection regardless of its
    /// score, so a library with only long tracks near a deadline still gets one — this never
    /// re-queries with a narrower predicate, it only re-ranks what <see cref="catalog"/>'s own
    /// tiered query already returned. A candidate with no measured <c>DurationMs</c> (not yet
    /// enriched) carries no score and is never preferred or penalized for it — neutral, picked only
    /// as a last resort when every sample lacks a duration.
    /// </para>
    ///
    /// <para>
    /// Sampling re-issues the SAME envelope-aware pick (identical scope/recent-ids/artist-separation
    /// args — see <see cref="SelectEnvelopeAwareCandidateAsync"/>) up to
    /// <see cref="BoundarySampleAttempts"/> times rather than requiring a new multi-row catalog
    /// method: the underlying tiered <c>ORDER BY ... random() LIMIT 1</c> query already draws from
    /// the whole rotation-valid, envelope-constrained pool, so repeat calls approximate a pool
    /// without widening that interface's contract (SPEC F81.2's "bias may reorder within the
    /// envelope's candidate set — never widen it" applies here too). Outside the bias window this
    /// degrades to exactly one call — today's behavior, unchanged.
    /// </para>
    /// </summary>
    async Task<RotationCandidate?> SelectMusicCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        var due = deferralQueue.NextDue;
        var untilDue = due is null ? default : due.Value - timeProvider.GetUtcNow();

        if (due is null || untilDue <= TimeSpan.Zero || untilDue > boundaryBiasProvider.Current)
            return await SelectEnvelopeAwareCandidateAsync(scope, orderedRecentIds, artistSeparation, ct);

        RotationCandidate? best = null;
        TimeSpan? bestDiff = null;
        RotationCandidate? firstUnscored = null;

        for (var attempt = 0; attempt < BoundarySampleAttempts; attempt++)
        {
            var sample = await SelectEnvelopeAwareCandidateAsync(scope, orderedRecentIds, artistSeparation, ct);
            if (sample is null)
            {
                // Nothing sampled yet at all — a genuine drain (F41.2), not a bias artifact.
                if (best is null && firstUnscored is null) return null;
                break; // the pool emptied mid-sample; keep whatever was already sampled.
            }

            if (sample.Media.DurationMs is int durationMs)
            {
                var diff = (TimeSpan.FromMilliseconds(durationMs) - untilDue).Duration();
                if (bestDiff is null || diff < bestDiff)
                {
                    best = sample;
                    bestDiff = diff;
                }
            }
            else
            {
                firstUnscored ??= sample;
            }
        }

        return best ?? firstUnscored;
    }

    /// <summary>
    /// The envelope-aware pick seam (SPEC F81.2/F81.5/F81.6, STORY-212 T62) — every call site that
    /// used to go straight to <see cref="IMediaCatalog.GetRotationCandidateAsync"/> now goes through
    /// here instead. The live envelope (<paramref name="envelopeProvider"/>, read fresh — never
    /// cached — same F30.1 discipline every sibling provider follows) governs both rung 0 and the
    /// ladder below.
    ///
    /// <para>
    /// <b>Rung 0 — persona pick (SPEC F81.6):</b> <see cref="TryPersonaPickAsync"/> tries
    /// <paramref name="personaPickProvider"/> first. Today that is always
    /// <see cref="NoOpPersonaPickProvider"/>, so this rung is a pass-through no-op — SPEC F81.2's
    /// "playout never depends on the persona layer existing" holds exactly because nothing is bound
    /// ahead of it yet. PLAN T64 (STORY-213) registers a real ranker-backed
    /// <see cref="IPersonaPickProvider"/> here instead; a throwing/timing-out implementation
    /// degrades to the ladder below with one loud WARN rather than a faulted pick.
    /// </para>
    ///
    /// <para>
    /// <b>Trust-but-verify (SPEC F81.5):</b> a NON-null rung-0 pick is checked against the envelope's
    /// genre allow-list before being trusted. A violation is discarded, logged, and the ladder below
    /// supplies the replacement instead — never the persona pick provider a second time in the same
    /// cycle. The ladder's OWN output is never subject to this re-check: each of its rungs already
    /// queries <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/> with whatever envelope THAT rung
    /// actually relaxed to, so a rung-4 pick is by construction conforming to the RELAXED envelope
    /// it was queried against, even though it would (correctly) fail a check against the original,
    /// unrelaxed one. With <see cref="NoOpPersonaPickProvider"/> — the only binding today — rung 0
    /// always returns <see langword="null"/>, so this re-check never fires; it exists for T64's
    /// ranker, which could in principle score a track outside the envelope's own candidate set.
    /// </para>
    ///
    /// <para>
    /// Energy IS part of this re-check as of PLAN T64 (SPEC F81.5, T62 review carry-over):
    /// <see cref="RotationCandidate.Energy"/> — populated by <see cref="RankerPersonaPickProvider"/>'s
    /// own <c>EnvelopeCandidateRow</c> mapping, still <see langword="null"/> for every candidate the
    /// envelope-only ladder itself produces (<see cref="IMediaCatalog.GetRotationCandidateAsync"/>/
    /// <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/> never populate it) — is checked against
    /// <paramref name="envelope"/>'s energy band the same way <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/>'s
    /// own predicate does: <see langword="null"/> always passes (enrichment lag must never silence a
    /// pick, SPEC F81.4). A candidate whose provider never populated <c>Energy</c> is therefore
    /// unaffected by this leg — this re-check gained a capability, it did not tighten one that used to
    /// pass everything.
    /// </para>
    /// </summary>
    async Task<RotationCandidate?> SelectEnvelopeAwareCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        var envelope = envelopeProvider.Current;

        var personaPick = await TryPersonaPickAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        if (personaPick is not null)
        {
            if (SatisfiesEnvelope(personaPick, envelope))
            {
                LogPerPickDebugLine(personaPick, DegradationStepNone);
                return personaPick;
            }

            logger.LogWarning(
                "Persona pick {MediaId} violated the segment envelope on re-check ({Violation}) — " +
                "discarding and re-running envelope-only (SPEC F81.5, trust-but-verify).",
                personaPick.Media.MediaId, DescribeEnvelopeViolation(personaPick, envelope));
        }

        var (candidate, degradationStep) =
            await SelectEnvelopeLadderAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        if (candidate is not null)
            LogPerPickDebugLine(candidate, degradationStep);
        return candidate;
    }

    /// <summary>
    /// SPEC F81.6 rung 0: never lets a fault in <paramref name="personaPickProvider"/> escape as a
    /// faulted pick. A <see langword="null"/> result (the ordinary "no persona opinion" outcome) is
    /// silent — only a thrown exception (including a timeout an implementation surfaces as one) logs
    /// a WARN and degrades to <see langword="null"/> here, which <see cref="SelectEnvelopeAwareCandidateAsync"/>
    /// then routes to the envelope-only ladder.
    /// </summary>
    async Task<RotationCandidate?> TryPersonaPickAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct)
    {
        try
        {
            return await personaPickProvider.TryPickAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Persona pick layer faulted — degrading to the envelope-only rotation-scored pick " +
                "(SPEC F81.6, rung 0).");
            return null;
        }
    }

    /// <summary>
    /// SPEC F81.6's degradation ladder: rotation, then energy, then genres, then the plain
    /// pre-envelope query — never silence. Each relaxed rung queries
    /// <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/> fresh (by-construction filtering, SPEC
    /// F81.4) rather than fetching wider and post-filtering in C# (F81.2).
    ///
    /// <para>
    /// A rung that has nothing left to give — the rotation window is already unconstrained (no
    /// recent ids, no artist separation), or the envelope's own energy band/genre list is already at
    /// its loosest value — is skipped without querying or logging: re-issuing an identical query
    /// would just reproduce the SAME null, and a WARN claiming a relaxation that did not actually
    /// narrow anything would mislead an operator reading the log (this matters in exactly the
    /// over-constrained-genre case PLAN T62's acceptance targets: energy is left at its default
    /// unconstrained band there, so a real "relaxing energy" WARN never fires — only the two rungs
    /// that actually gave something up do). Every rung that DOES fire logs one loud WARN naming
    /// exactly what gave way, in order, before the next rung is even attempted.
    /// </para>
    ///
    /// <para>
    /// An empty <paramref name="scope"/> is the same "nothing left to give" case one level up: every
    /// <see cref="IMediaCatalog"/> method's own contract is default-deny (no access, no SQL issued)
    /// regardless of rotation/energy/genre, so once rung 1's call has made that contract visible via
    /// a null return, none of rungs 2-4 or the terminal query below can do anything rung 1 didn't
    /// already do — they are skipped rather than repeating the identical no-op call three more times.
    /// </para>
    /// </summary>
    async Task<(RotationCandidate? Candidate, string DegradationStep)> SelectEnvelopeLadderAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct)
    {
        // Rung 1: the common case — full envelope, full rotation preference.
        var candidate = await catalog.GetEnvelopeCandidateAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        if (candidate is not null) return (candidate, DegradationStepNone);
        if (scope.IsEmpty) return (null, DegradationStepNone);

        // Rung 2: relax ROTATION first (hygiene, not law) — the SAME envelope, queried with no
        // rotation-window/artist-separation preference at all rather than widening genre/energy.
        if (orderedRecentIds.Count > 0 || artistSeparation > 0)
        {
            logger.LogWarning(
                "Envelope-constrained pool empty — relaxing the rotation window (anti-repeat + " +
                "artist-separation) before any envelope law bends (SPEC F81.6).");
            candidate = await catalog.GetEnvelopeCandidateAsync(scope, [], 0, envelope, ct);
            if (candidate is not null) return (candidate, DegradationStepRotation);
        }

        // Rung 3: relax ENERGY — the genre allow-list stays; the energy band widens to
        // Unconstrained (skipped if it already was). Rotation stays relaxed from rung 2.
        var energyRelaxed = envelope;
        if (envelope.EnergyRange != EnergyRange.Unconstrained)
        {
            energyRelaxed = envelope with { EnergyRange = EnergyRange.Unconstrained };
            logger.LogWarning(
                "Envelope-constrained pool still empty with rotation relaxed — relaxing the energy " +
                "band to unconstrained (SPEC F81.6).");
            candidate = await catalog.GetEnvelopeCandidateAsync(scope, [], 0, energyRelaxed, ct);
            if (candidate is not null) return (candidate, DegradationStepEnergy);
        }

        // Rung 4: relax GENRES — the last envelope knob to give way (skipped if the allow-list was
        // already empty). Energy and rotation stay relaxed from rungs 2/3.
        if (energyRelaxed.Genres.Count > 0)
        {
            var genresRelaxed = energyRelaxed with { Genres = [] };
            logger.LogWarning(
                "Envelope-constrained pool still empty with energy relaxed — relaxing the genre " +
                "allow-list to admit every genre (SPEC F81.6).");
            candidate = await catalog.GetEnvelopeCandidateAsync(scope, [], 0, genresRelaxed, ct);
            if (candidate is not null) return (candidate, DegradationStepGenres);
        }

        // Terminal: the plain pre-envelope query — SPEC F81.6's never-silence floor. Its own F41.1
        // tiering still applies (a repeated-recent/repeated-artist relaxation logs via GetNextAsync's
        // existing checks on whatever this returns); a null here means the playable pool itself is
        // empty (F41.2's genuine drain), which GetNextAsync's own WARN already names.
        logger.LogWarning(
            "Envelope-constrained pool still empty with every envelope/rotation knob relaxed — " +
            "falling back to the base playable query with no envelope at all (SPEC F81.6, " +
            "never-silence).");
        candidate = await catalog.GetRotationCandidateAsync(scope, orderedRecentIds, artistSeparation, ct);
        return (candidate, DegradationStepTerminal);
    }

    /// <summary>
    /// SPEC F81.5's full re-check — both legs must pass for a rung-0 persona pick to be trusted.
    /// </summary>
    static bool SatisfiesEnvelope(RotationCandidate candidate, SegmentEnvelope envelope) =>
        SatisfiesEnvelopeGenre(candidate.Media, envelope) && SatisfiesEnvelopeEnergy(candidate.Energy, envelope);

    /// <summary>
    /// SPEC F81.5's re-check, genre half: empty allow-list admits everything; a non-empty list
    /// requires a case-insensitive match, and an untagged (<see langword="null"/> <see cref="MediaReference.Genre"/>)
    /// track never satisfies a non-empty list — mirrors <c>MediaRepository.GetEnvelopeCandidateAsync</c>'s
    /// own by-construction predicate exactly (SPEC F81.1), so a genre-conforming catalog pick can
    /// never spuriously fail this re-check.
    /// </summary>
    static bool SatisfiesEnvelopeGenre(MediaReference media, SegmentEnvelope envelope) =>
        envelope.Genres.Count == 0 ||
        (media.Genre is not null &&
            envelope.Genres.Any(g => string.Equals(g, media.Genre, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// SPEC F81.5's re-check, energy half (T62 review carry-over, PLAN T64) — mirrors
    /// <c>MediaRepository.GetEnvelopeCandidateAsync</c>'s own energy-band WHERE predicate exactly
    /// (SPEC F81.4): a <see langword="null"/> <see cref="RotationCandidate.Energy"/> always passes
    /// (enrichment lag must never silence a pick) — only a real, out-of-band percentile fails.
    /// </summary>
    static bool SatisfiesEnvelopeEnergy(double? energy, SegmentEnvelope envelope) =>
        energy is null || (energy >= envelope.EnergyRange.Min && energy <= envelope.EnergyRange.Max);

    /// <summary>Names which leg(s) of <see cref="SatisfiesEnvelope"/> a discarded persona pick violated, for the WARN.</summary>
    static string DescribeEnvelopeViolation(RotationCandidate candidate, SegmentEnvelope envelope)
    {
        var reasons = new List<string>(2);
        if (!SatisfiesEnvelopeGenre(candidate.Media, envelope)) reasons.Add("genre");
        if (!SatisfiesEnvelopeEnergy(candidate.Energy, envelope)) reasons.Add("energy");
        return string.Join("+", reasons);
    }

    /// <summary>
    /// SPEC F82.6 — the one per-pick debug line: envelope id, pool size, the winning pick's top-3
    /// scores, which taste rules fired, the exploration flag, and which degradation rung (SPEC F81.6)
    /// actually supplied the pick. Fires on EVERY music pick — persona-off included — so the ladder's
    /// own degradation step is always visible, mirroring the <c>LiquidsoapControl</c> per-command
    /// convention (a per-tick line belongs at Debug, not Information — SPEC F82.6's own "per-pick"
    /// framing puts it in the same high-frequency bucket).
    /// <paramref name="candidate"/>'s <see cref="RotationCandidate.PersonaPick"/> is null for every
    /// envelope-only ladder pick (including the common case where no persona is even active) — the
    /// pool/top3/firedRules/exploration fields all read as empty/false in that case, never omitted
    /// from the line.
    /// </summary>
    void LogPerPickDebugLine(RotationCandidate candidate, string degradationStep)
    {
        var diagnostics = candidate.PersonaPick;
        var topScores = diagnostics is null
            ? ""
            : string.Join(", ", diagnostics.TopScores.Select(s => s.ToString("F3", CultureInfo.InvariantCulture)));
        var firedRules = diagnostics is null
            ? ""
            : string.Join("; ", diagnostics.FiredRules.Select(FormatFiredRule));

        logger.LogDebug(
            "Pick — envelope={EnvelopeId} pool={PoolSize} top3=[{TopScores}] firedRules=[{FiredRules}] " +
            "exploration={IsExploration} degradation={DegradationStep}",
            EnvelopeId, diagnostics?.PoolSize ?? 0, topScores, firedRules,
            diagnostics?.IsExploration ?? false, degradationStep);
    }

    /// <summary>One short "what:weight" summary per fired taste rule for the debug line — not a full serialization.</summary>
    static string FormatFiredRule(TasteRule rule) =>
        $"{rule.Predicate.Artist ?? rule.Predicate.Genre ?? rule.Predicate.Tag ?? "any"}:{rule.Weight.ToString("F2", CultureInfo.InvariantCulture)}";

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
        // wall-clock slip"). Reads the SAME injected clock SelectMusicCandidateAsync compares
        // NextDue against (SPEC F74.3) — one clock for both halves of this seam, never a mix of a
        // real and a fake one.
        foreach (var deferral in deferralQueue.TryDequeueDue(timeProvider.GetUtcNow()))
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
