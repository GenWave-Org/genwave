namespace GenWave.Orchestration;

using System.Globalization;
using Microsoft.Extensions.Logging;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The music pick ladder (F112, STORY-295, PLAN T218) — extracted out of <see cref="Orchestrator"/>
/// verbatim: request rung, persona rung, trust-but-verify, and the SPEC F81.6 degradation ladder, in
/// that order. <see cref="Orchestrator.GetNextAsync"/> calls <see cref="SelectMusicCandidateAsync"/>
/// once per unit; nothing in this class reads unit-assembly state (cadence, previous track, unit
/// count) — <c>Orchestrator.BuildBoundaryFit</c> still owns that accounting and stays put, handing
/// this class the already-built <see cref="BoundaryFitPlan"/> as a plain method argument, never
/// constructor state.
///
/// <para>
/// Every music pick consults <paramref name="requestFulfillmentSource"/> FIRST (SPEC F87.6, rung -1: a
/// live pending listener request short-circuits the pick entirely, ahead of persona ranking and the
/// envelope-only ladder alike — a no-op today unless a host binds the real
/// <c>RequestFulfillmentProvider</c>, PLAN T90) — see <see cref="SelectMusicCandidateAsync"/>'s own
/// remarks for exactly where that single consultation sits relative to its boundary-bias resample
/// loop (PLAN T90 review: the rung must run ONCE per pick, never once per sample). Failing that, every
/// pick is envelope-aware (SPEC F81, STORY-212): <see cref="SelectEnvelopeAwareCandidateAsync"/> is the
/// seam that has replaced the direct <see cref="IMediaCatalog.GetRotationCandidateAsync"/> call sites
/// below. It tries <paramref name="personaPickProvider"/> first (rung 0 — a no-op today, SPEC F81.2:
/// playout never depends on the persona layer existing; PLAN T64 wires a real ranker in), then falls
/// back to the by-construction-filtered <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/> with
/// <paramref name="envelopeProvider"/>'s live envelope (read fresh per pick, same F30.1 discipline).
/// Whatever either source returns is re-checked against the envelope (trust-but-verify, SPEC F81.5)
/// before this policy trusts it. When the envelope-constrained pool is genuinely empty, a
/// degradation ladder (SPEC F81.6) relaxes rotation, then energy, then genres — each rung logging a
/// loud WARN naming what gave way — before falling back to the plain
/// <see cref="IMediaCatalog.GetRotationCandidateAsync"/> query as the final never-silence rung. See
/// <see cref="SelectEnvelopeAwareCandidateAsync"/>'s own remarks for the full rung order below rung -1.
/// </para>
///
/// <para>
/// House provider discipline (unchanged by the move): <paramref name="envelopeProvider"/> is read
/// fresh on every pick — never cached in a field beyond the null-coalesced default below — mirroring
/// every other live-settings seam this codebase threads through (SPEC F30.1).
/// </para>
/// </summary>
public sealed class MusicSelectionPolicy(
    IMediaCatalog catalog,
    ILogger<MusicSelectionPolicy> logger,
    IEnvelopeProvider? envelopeProvider = null,
    IPersonaPickProvider? personaPickProvider = null,
    IRequestFulfillmentSource? requestFulfillmentSource = null)
{
    /// <summary>
    /// How many independent rotation-tiered samples <see cref="SelectMusicCandidateAsync"/> draws
    /// when the boundary-bias window is active (SPEC F74.3) — enough to see a few distinct tier-1
    /// rows in even a modest library without turning every biased pick into a database hot loop.
    /// </summary>
    const int BoundarySampleAttempts = 5;

    /// <summary>
    /// gh-#254 — the expected crossfade overlap a track's tail loses into whatever follows it:
    /// the engine's energy-aware crossfade runs GW_XFADE_MIN..GW_XFADE_MAX (2..8s shipped
    /// defaults), decided per-seam inside genwave.liq where this control plane cannot see it, so
    /// the midpoint is the honest single number. A judged constant, not a live knob: its ±3s
    /// worst-case error is well inside even the tightest fit tolerance <c>Orchestrator.BuildBoundaryFit</c>
    /// computes.
    /// </summary>
    static readonly TimeSpan ExpectedCrossfadeTrim = TimeSpan.FromSeconds(5);

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
    // codebase (e.g. GenWave.Tts.TtsSegmentSource). Moved verbatim from Orchestrator (F112).
    readonly IEnvelopeProvider envelopeProvider = envelopeProvider ?? StationDefaultEnvelopeProvider.Instance;
    readonly IPersonaPickProvider personaPickProvider = personaPickProvider ?? NoOpPersonaPickProvider.Instance;
    readonly IRequestFulfillmentSource requestFulfillmentSource =
        requestFulfillmentSource ?? NoOpRequestFulfillmentSource.Instance;

    /// <summary>
    /// gh-#300 — below this much room left in front of a boundary, no music unit belongs there at
    /// all: the ceremony itself becomes the unit (SPEC F111.1's <see cref="BoundaryOutcome.CeremonyOnly"/>
    /// rung). Moved here verbatim from <c>Orchestrator.MusicUnitFloor</c> (PLAN T234, SPEC F112.3 —
    /// the ladder's own authority, so <c>Orchestrator.ShouldDeclineFinalUnit</c> now reads THIS
    /// constant rather than keeping a second copy of the same number).
    ///
    /// <para>
    /// <b>Where the number comes from.</b> Planning a track of length L into D of remaining room
    /// lands the ceremony <c>L - D</c> LATE; declining lands it <c>D</c> EARLY. Declining is
    /// therefore the better trade exactly while <c>D &lt; L / 2</c>. With a typical unit around
    /// three minutes that break-even sits at ninety seconds, and this is that number. Above it the
    /// gh-#254 fit keeps its existing least-late behavior (SPEC F111.1's <see cref="BoundaryOutcome.Straddle"/>
    /// rung as of gh-#320/PLAN T234), which is still the right answer there.
    /// </para>
    ///
    /// <para>
    /// The 2:05 incident sat far below this line — the queued audio already ran PAST the boundary,
    /// so <c>desired</c> was deeply negative and every candidate was hopeless. A judged constant in
    /// the spirit of <see cref="ExpectedCrossfadeTrim"/> and <c>Orchestrator.SignOffLeadTime</c>, not
    /// a live knob: gh-#300's own fit logging is what makes promoting it to one an argument from
    /// field data rather than taste, and that data does not exist yet.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan MusicFloor = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Picks the next music candidate — SPEC F41.1/F41.3 tiering is unchanged and still governs
    /// which candidates are even eligible — and, when <paramref name="fit"/> is non-null (a pending
    /// deferral is due strictly in the future within the boundary-bias provider's lookahead
    /// window — SPEC F74.3, STORY-198), softly biases that pick toward whichever sampled candidate's
    /// end lands closest to the boundary — a full duration FIT as of gh-#254, no longer a raw
    /// duration-vs-due comparison. See <c>Orchestrator.BuildBoundaryFit</c> for the accounting
    /// (queued-ahead drift, this unit's own pre-music patter, crossfade trim, and the break's
    /// expected patter via the gh-#253 estimator) and for how tolerance widens with the estimator's
    /// confidence tier.
    ///
    /// <para>
    /// <b>F112/STORY-295:</b> <paramref name="fit"/> arrives as a plain argument — built and owned by
    /// <c>Orchestrator.BuildBoundaryFit</c>, which reads unit-assembly state (cadence, previous track,
    /// unit count) this class deliberately never sees. <paramref name="boundaryFitLog"/> is
    /// <see cref="Orchestrator"/> itself (it implements <see cref="IBoundaryFitLog"/> explicitly, PLAN
    /// T234 — see that interface's own remarks for why a named interface replaced the delegate this
    /// parameter used to be), so every outcome line this method logs still lands on the SAME
    /// Information sink <c>Orchestrator.TryServeCeremonyOnlyUnitAsync</c>'s own "declined" line uses —
    /// one boundary-fit log, one owner, regardless of which class decided the outcome. A
    /// <see langword="null"/> <paramref name="boundaryFitLog"/> (every construction site that never
    /// scripts one, including every pre-T234 test) degrades to <see cref="NoOpBoundaryFitLog"/> —
    /// the same optional-seam idiom <see cref="envelopeProvider"/>/<see cref="personaPickProvider"/>/
    /// <see cref="requestFulfillmentSource"/> already follow.
    /// </para>
    ///
    /// <para>
    /// <b>SPEC F111.1 (gh-#320, PLAN T234):</b> the return value's <see cref="BoundaryOutcome"/> names
    /// which rung of the ladder this pick resolved to — <see cref="BoundaryOutcome.Fit"/> when a
    /// sample lands within tolerance (the win rule below, unchanged since gh-#254); otherwise
    /// <see cref="BoundaryOutcome.Straddle"/> when <see cref="BoundaryFitPlan.DesiredEffectiveLength"/>
    /// still clears <see cref="MusicFloor"/>, or <see cref="BoundaryOutcome.CeremonyOnly"/> when it
    /// does not — the SAME floor <c>Orchestrator.ShouldDeclineFinalUnit</c> already gates its own
    /// pre-emptive decline on, now with one authority for the comparison. Reported here, and read by
    /// <c>Orchestrator.GetNextAsync</c>'s straddle branch as of PLAN T235 — but
    /// <see cref="BoundaryOutcome.Straddle"/> ALONE never forces anything there (T235 review findings
    /// F1/F5): <see cref="MusicSelectionResult.CrossesBoundary"/>, computed alongside this outcome, is
    /// the honest answer to whether THIS pick's own effective length actually reaches the boundary —
    /// off-tolerance-but-above-the-floor says nothing about that by itself, since a pick can miss
    /// tolerance by running too SHORT just as easily as too long.
    /// </para>
    ///
    /// <para>
    /// The peek and the fit build itself moved UP to <see cref="Orchestrator.GetNextAsync"/> in
    /// gh-#300, which needs the very same fit one decision earlier — to ask whether a music unit
    /// belongs in front of the boundary at all (<c>Orchestrator.ShouldDeclineFinalUnit</c>). One fit
    /// per unit, built once, read twice; this method's four ex-parameters (cadence, identity, unit DJ
    /// name, queued-ahead) existed only to feed that build and went with it.
    /// </para>
    ///
    /// <para>
    /// <b>Degenerate-pick guard (gh-#254):</b> landing within the fit's tolerance is a WIN, not a
    /// leaderboard — the FIRST rotation-tiered random sample inside the window is returned as-is
    /// (sampling stops early), so approaches with plenty of well-fitting tracks keep their natural
    /// variety instead of converging on whichever single track scores 0.0s every hour. Min-diff
    /// engages only when NO sample lands inside the tolerance; and because it ranks only the up-to-
    /// <see cref="BoundarySampleAttempts"/> random, rotation/envelope/posture-constrained samples of
    /// THIS pick (never a widened or re-predicated pool query), even an inevitable-overshoot
    /// approach picks the least-late of a rotating random handful — bounded repetition by
    /// construction, with the anti-repeat window still applying on top.
    /// </para>
    ///
    /// <para>
    /// <b>Rung -1 sits HERE, above the sampler (SPEC F87.6, PLAN T90 review carry-over):</b>
    /// <see cref="TryFulfillPendingRequestAsync"/> is consulted exactly ONCE per pick, before the
    /// due/bias branch below even runs, and a hit short-circuits the ENTIRE pick — bias loop
    /// included. It used to live one level down, inside <see cref="SelectEnvelopeAwareCandidateAsync"/>,
    /// which the bias loop below calls up to <see cref="BoundarySampleAttempts"/> times per pick; a
    /// non-idempotent CAS-stamp-and-publish side effect run from inside a method whose whole contract
    /// is "safe to resample" is a bug two ways at once — a stamped request's track could still lose
    /// the timing comparison and never air while narrated fulfilled, and every extra sample stamped
    /// (and published) one more pending request that should have waited its turn. Hoisting the
    /// consultation up here makes <see cref="SelectEnvelopeAwareCandidateAsync"/> a pure, freely
    /// repeatable sampler again and gives every pick exactly one CAS attempt, full stop, regardless of
    /// whether the bias loop below ever activates.
    /// </para>
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
    internal async Task<MusicSelectionResult> SelectMusicCandidateAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        BoundaryFitPlan? fit,
        IBoundaryFitLog? boundaryFitLog,
        CancellationToken ct)
    {
        var log = boundaryFitLog ?? NoOpBoundaryFitLog.Instance;

        // Rung -1, once per pick (SPEC F87.6, PLAN T90 review) — see this method's own remarks for
        // why this sits above the bias branch rather than inside the sampler it guards. No boundary
        // ladder governs a request short-circuit (SPEC F111.1) — None, same as no fit at all.
        var envelope = envelopeProvider.Current;
        if (await TryFulfillPendingRequestAsync(envelope, ct) is { } fulfilledCandidate)
            return new MusicSelectionResult(fulfilledCandidate, BoundaryOutcome.None, CrossesBoundary: false);

        // No in-window deferral to aim at (gh-#300 hoisted the peek and the fit build up to
        // GetNextAsync, which needs the same fit to decide whether a music unit belongs here at
        // all) — the no-imminent-boundary common case, exactly one catalog call as always.
        if (fit is null)
        {
            var plain = await SelectEnvelopeAwareCandidateAsync(scope, orderedRecentIds, artistSeparation, ct);
            return new MusicSelectionResult(plain, BoundaryOutcome.None, CrossesBoundary: false);
        }

        RotationCandidate? best = null;
        TimeSpan? bestDiff = null;
        TimeSpan? bestEffective = null;
        RotationCandidate? firstUnscored = null;
        var sampled = new List<TimeSpan>(BoundarySampleAttempts);

        for (var attempt = 0; attempt < BoundarySampleAttempts; attempt++)
        {
            var sample = await SelectEnvelopeAwareCandidateAsync(scope, orderedRecentIds, artistSeparation, ct);
            if (sample is null)
            {
                // Nothing sampled yet at all — a genuine drain (F41.2), not a bias artifact. Still
                // classified against the floor (SPEC F111.1): the ladder's rung names how much room
                // was left, independent of whether this particular draw found anything to fill it.
                if (best is null && firstUnscored is null)
                {
                    var drainedRung = ClassifyOffToleranceRung(fit);
                    log.Log(fit, "drained", drainedRung, sampled, chosenDiff: null);
                    return new MusicSelectionResult(null, drainedRung, CrossesBoundary: false);
                }

                break; // the pool emptied mid-sample; keep whatever was already sampled.
            }

            if (sample.Media.DurationMs is int durationMs)
            {
                // Effective on-air length: the measured duration minus the expected crossfade
                // overlap into whatever follows (gh-#254 — "minus crossfade trim").
                var effective = TimeSpan.FromMilliseconds(durationMs) - ExpectedCrossfadeTrim;
                var diff = (effective - fit.DesiredEffectiveLength).Duration();
                sampled.Add(effective);

                // Within tolerance is a WIN (gh-#254, ±30s widened by confidence) — keep THIS
                // rotation-tiered random sample and stop sampling: see this method's remarks for
                // why first-inside-the-window, not closest-fit, is the degenerate-pick guard. SPEC
                // F111.1's Fit rung, exactly — the ladder's top.
                if (diff <= fit.Tolerance)
                {
                    log.Log(fit, "win", BoundaryOutcome.Fit, sampled, diff);
                    return new MusicSelectionResult(sample, BoundaryOutcome.Fit, CrossesBoundary: false);
                }

                if (bestDiff is null || diff < bestDiff)
                {
                    best = sample;
                    bestDiff = diff;
                    bestEffective = effective;
                }
            }
            else
            {
                firstUnscored ??= sample;
            }
        }

        // No sample landed inside the tolerance. "least-late" is the min-diff pick; "unscored" is
        // the last-resort duration-less candidate that carried no score at all (F74.3 keeps it
        // eligible — an un-enriched row is never penalized for enrichment lag). Either way the ladder
        // rung is decided the SAME way (SPEC F111.1): Straddle with room to spare, CeremonyOnly
        // without it.
        var offToleranceRung = ClassifyOffToleranceRung(fit);

        // T235 review findings F1/F5 — "off tolerance, above the floor" (Straddle) says nothing on
        // its own about whether THIS pick actually crosses the boundary: a "least-late" candidate can
        // miss tolerance by running too SHORT just as easily as too long, and "unscored" never even
        // measured a length. Only a "least-late" pick whose own effective length reaches the boundary
        // itself (fit.UntilBoundary — the SAME instant BuildBoundaryFit reasoned about) is genuinely
        // going to still be airing when the SignOff would otherwise be due; a duration-less
        // firstUnscored pick never claims it (nothing was measured to compare).
        var crossesBoundary = bestEffective is { } effectiveLength && effectiveLength >= fit.UntilBoundary;

        log.Log(fit, best is not null ? "least-late" : "unscored", offToleranceRung, sampled, bestDiff);
        return new MusicSelectionResult(best ?? firstUnscored, offToleranceRung, crossesBoundary);
    }

    /// <summary>
    /// SPEC F111.1 (gh-#320, PLAN T234) — the ladder's rung once a pick has already missed tolerance:
    /// <see cref="BoundaryOutcome.Straddle"/> while <see cref="BoundaryFitPlan.DesiredEffectiveLength"/>
    /// still clears <see cref="MusicFloor"/>, else <see cref="BoundaryOutcome.CeremonyOnly"/> — reads
    /// <see cref="IsBelowFloor"/> (T234 review finding F3) rather than comparing
    /// <see cref="BoundaryFitPlan.DesiredEffectiveLength"/> against <see cref="MusicFloor"/> a second
    /// time by hand.
    /// </summary>
    static BoundaryOutcome ClassifyOffToleranceRung(BoundaryFitPlan fit) =>
        IsBelowFloor(fit) ? BoundaryOutcome.CeremonyOnly : BoundaryOutcome.Straddle;

    /// <summary>
    /// T234 review finding F3 — the ONE place <see cref="BoundaryFitPlan.DesiredEffectiveLength"/> is
    /// ever compared against <see cref="MusicFloor"/>. Before this method existed,
    /// <see cref="ClassifyOffToleranceRung"/> (<c>&gt;= MusicFloor ? Straddle : CeremonyOnly</c>) and
    /// <c>Orchestrator.ShouldDeclineFinalUnit</c> (<c>&lt; MusicSelectionPolicy.MusicFloor</c>) each
    /// hand-wrote what was supposed to be the SAME comparison as its own complement — two call sites a
    /// future edit to either one could silently drift out of sync with the other, exactly the drift
    /// the doc comment on the old <see cref="ClassifyOffToleranceRung"/> claimed could not happen.
    /// Both call sites now call this instead. <c>&gt;=</c> is the floor's own edge convention (SPEC
    /// F112.3): exactly at the floor is NOT below it, so a fit landing exactly on
    /// <see cref="MusicFloor"/> straddles rather than declines — pinned by
    /// <c>Story303_StraddleHandoff</c>'s own exact-floor fact.
    /// </summary>
    internal static bool IsBelowFloor(BoundaryFitPlan fit) => fit.DesiredEffectiveLength < MusicFloor;

    /// <summary>
    /// The envelope-aware pick seam (SPEC F81.2/F81.5/F81.6, STORY-212 T62) — every call site that
    /// used to go straight to <see cref="IMediaCatalog.GetRotationCandidateAsync"/> now goes through
    /// here instead. The live envelope (<paramref name="envelopeProvider"/>, read fresh — never
    /// cached — same F30.1 discipline every sibling provider follows) governs both rung 0 and the
    /// ladder below.
    ///
    /// <para>
    /// <b>Pure and freely repeatable (PLAN T90 review carry-over):</b> this method has NO side
    /// effects of its own beyond logging — <see cref="SelectMusicCandidateAsync"/>'s boundary-bias
    /// loop calls it up to <see cref="BoundarySampleAttempts"/> times per single pick to approximate
    /// sampling from a pool (see that method's own remarks), so re-running it must never do anything
    /// that is not safe to do 5 times over. The one-shot request-fulfillment consultation (SPEC
    /// F87.6, rung -1) does NOT live here for exactly that reason — it CAS-stamps a request row and
    /// publishes an event, neither of which is idempotent, so it is consulted exactly ONCE per pick,
    /// one level up in <see cref="SelectMusicCandidateAsync"/>, before this sampler ever runs.
    /// </para>
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
        // Captured alongside envelope, at the same read point (SPEC F91.7) — a resolver-backed
        // provider's Current/EnvelopeId are two independent reads of the same underlying snapshot,
        // mirroring IActivePersonaAccessor's own documented "two independent reads" shape; a
        // boundary landing in the narrow window between them degrades no worse than a
        // stale-but-consistent per-pick debug line.
        var envelopeId = envelopeProvider.EnvelopeId;

        var personaPick = await TryPersonaPickAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        if (personaPick is not null)
        {
            if (SatisfiesEnvelope(personaPick, envelope))
            {
                LogPerPickDebugLine(personaPick, DegradationStepNone, envelopeId);
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
            LogPerPickDebugLine(candidate, degradationStep, envelopeId);
        return candidate;
    }

    /// <summary>
    /// SPEC F87.6 rung -1: never lets a fault in <see cref="requestFulfillmentSource"/> escape as a
    /// faulted pick — mirrors <see cref="TryPersonaPickAsync"/>'s own catch posture one rung down. A
    /// fulfillment is logged at Information (request id, matched-vs-vibe, the media id) rather than
    /// riding the per-pick Debug line below: that line's envelope/pool/topScores/firedRules vocabulary
    /// describes a persona/envelope-ladder pick, not a request short-circuit, and would read as an
    /// empty, confusing line for this rung.
    ///
    /// <para>
    /// Called from <see cref="SelectMusicCandidateAsync"/> — ONE level above
    /// <see cref="SelectEnvelopeAwareCandidateAsync"/>'s own boundary-bias resample loop, never from
    /// inside it (PLAN T90 review carry-over) — exactly ONCE per pick regardless of whether that loop
    /// activates: the CAS-stamp-and-publish this triggers is a one-shot side effect (SPEC F87.6),
    /// never safe to repeat across a bias loop's up-to-<see cref="BoundarySampleAttempts"/> resamples.
    /// </para>
    /// </summary>
    async Task<RotationCandidate?> TryFulfillPendingRequestAsync(SegmentEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var fulfillment = await requestFulfillmentSource.TryFulfillAsync(envelope, ct);
            if (fulfillment is null) return null;

            logger.LogInformation(
                "Fulfilling pending request {RequestId} ({Kind}) with track {MediaId} (SPEC F87.6).",
                fulfillment.RequestId, fulfillment.WasVibe ? "vibe" : "matched", fulfillment.Candidate.Media.MediaId);
            return fulfillment.Candidate;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Request fulfillment layer faulted — degrading to the normal pick chain (SPEC F87.6, " +
                "mirrors F81.6's rung-0 catch posture).");
            return null;
        }
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
    /// SPEC F82.6/F91.7 — the one per-pick debug line: envelope id, pool size, the winning pick's
    /// top-3 scores, which taste rules fired, the exploration flag, and which degradation rung (SPEC
    /// F81.6) actually supplied the pick. Fires on EVERY music pick — persona-off included — so the
    /// ladder's own degradation step is always visible, mirroring the <c>LiquidsoapControl</c>
    /// per-command convention (a per-tick line belongs at Debug, not Information — SPEC F82.6's own
    /// "per-pick" framing puts it in the same high-frequency bucket).
    /// <paramref name="candidate"/>'s <see cref="RotationCandidate.PersonaPick"/> is null for every
    /// envelope-only ladder pick (including the common case where no persona is even active) — the
    /// pool/top3/firedRules/exploration fields all read as empty/false in that case, never omitted
    /// from the line. <paramref name="envelopeId"/> is <see cref="envelopeProvider"/>'s own
    /// <see cref="IEnvelopeProvider.EnvelopeId"/> — <c>"segment:{id}"</c> for a live schedule segment,
    /// the station-default sentinel for a gap (SPEC F91.7) — read once by the caller alongside the
    /// envelope itself, never re-read here.
    /// </summary>
    void LogPerPickDebugLine(RotationCandidate candidate, string degradationStep, string envelopeId)
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
            envelopeId, diagnostics?.PoolSize ?? 0, topScores, firedRules,
            diagnostics?.IsExploration ?? false, degradationStep);
    }

    /// <summary>One short "what:weight" summary per fired taste rule for the debug line — not a full serialization.</summary>
    static string FormatFiredRule(TasteRule rule) =>
        $"{rule.Predicate.LabelOr("any")}:{rule.Weight.ToString("F2", CultureInfo.InvariantCulture)}";
}
