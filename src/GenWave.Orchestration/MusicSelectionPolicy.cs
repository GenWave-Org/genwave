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

    /// <summary>
    /// SPEC F152.4 (STORY-372, PLAN T361) — the rotation relax ladder's top step: R3 (the predicate
    /// dropped entirely). Also the value <see cref="RotationCandidate.RotationRelax"/> pins to when
    /// even R3's own rung-0 attempt yields nothing and the existing SPEC F81.6 ladder supplies the
    /// pick instead (never-silence by construction — see <see cref="SelectRotationRelaxedCandidateAsync"/>'s
    /// own remarks).
    /// </summary>
    const int MaxRotationRelaxStep = 3;

    /// <summary>SPEC F152.4's R2 rung — the bottom DECILE of play_count, i.e. the 10th percentile.</summary>
    const double BottomDecileQuantile = 0.1;

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
    /// sample lands within tolerance (the win rule below, unchanged since gh-#254); otherwise whatever
    /// <see cref="BoundaryFitPlan.ClassifyOffToleranceRung"/> resolves against <see cref="MusicFloor"/>
    /// (SPEC F124.1 widens that classifier to treat a queue crossing the boundary as
    /// <see cref="BoundaryOutcome.Straddle"/> too — see <see cref="BoundaryFitPlan.IsBelowFloor"/>'s own
    /// remarks for why <c>Orchestrator.ShouldDeclineFinalUnit</c> never needed to widen its OWN,
    /// floor-only condition to match, and <c>Orchestrator.TryServeCeremonyOnlyUnitAsync</c>'s remarks
    /// for how its decline path stays in agreement with this same classifier without this method ever
    /// running for a queue-crossing handoff fit). Reported here, and read by
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

        // MED-3 (T361 review) — one SPEC F152.4 quantile cache per PICK, shared across every
        // resample attempt the bias loop below draws (up to BoundarySampleAttempts): the underlying
        // pool cannot change mid-pick (nothing writes between resamples), so recomputing the R2
        // percentile_disc read on every attempt was both wasteful (up to 5 extra DB round trips) and
        // pointless (identical answer every time). See RotationQuantileCache's own remarks.
        var quantileCache = new RotationQuantileCache();

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
            var plain = await SelectEnvelopeAwareCandidateAsync(scope, orderedRecentIds, artistSeparation, quantileCache, ct);
            LogRotationRelaxedOnce(plain);
            return new MusicSelectionResult(plain, BoundaryOutcome.None, CrossesBoundary: false);
        }

        RotationCandidate? best = null;
        TimeSpan? bestDiff = null;
        TimeSpan? bestEffective = null;
        RotationCandidate? firstUnscored = null;
        var sampled = new List<TimeSpan>(BoundarySampleAttempts);

        for (var attempt = 0; attempt < BoundarySampleAttempts; attempt++)
        {
            var sample = await SelectEnvelopeAwareCandidateAsync(scope, orderedRecentIds, artistSeparation, quantileCache, ct);
            if (sample is null)
            {
                // Nothing sampled yet at all — a genuine drain (F41.2), not a bias artifact. Still
                // classified against the floor (SPEC F111.1): the ladder's rung names how much room
                // was left, independent of whether this particular draw found anything to fill it.
                if (best is null && firstUnscored is null)
                {
                    // SPEC F124.1 (PLAN T266): CrossesBoundary is not hard-false here just because
                    // nothing NEW was sampled — a queued tail that alone already spans the boundary
                    // crosses regardless, the same union computed below for the off-tolerance branch.
                    var drainedRung = fit.ClassifyOffToleranceRung(MusicFloor);
                    log.Log(fit, "drained", drainedRung, sampled, chosenDiff: null);
                    return new MusicSelectionResult(null, drainedRung, fit.QueuedTailCrossesBoundary);
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
                    LogRotationRelaxedOnce(sample);
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
        // rung is decided the SAME way (SPEC F111.1, widened by SPEC F124.1 — see
        // BoundaryFitPlan.ClassifyOffToleranceRung's own remarks): Straddle with room to spare OR a
        // queued tail that alone already spans the boundary, CeremonyOnly with neither.
        var offToleranceRung = fit.ClassifyOffToleranceRung(MusicFloor);

        // T235 review findings F1/F5 — "off tolerance, above the floor" (Straddle) says nothing on
        // its own about whether THIS pick actually crosses the boundary: a "least-late" candidate can
        // miss tolerance by running too SHORT just as easily as too long, and "unscored" never even
        // measured a length. Only a "least-late" pick whose own effective length reaches the boundary
        // itself (fit.UntilBoundary — the SAME instant BuildBoundaryFit reasoned about) is genuinely
        // going to still be airing when the SignOff would otherwise be due; a duration-less
        // firstUnscored pick never claims it (nothing was measured to compare).
        //
        // SPEC F124.1 (PLAN T266) widens this to a UNION, never a replacement: the queued tail alone
        // spanning the boundary (QueuedTailCrossesBoundary) is a SECOND, independent way this pick's
        // unit crosses — the picked candidate can be short (or even unmeasured, the firstUnscored
        // case) and the unit still crosses because of what is ALREADY queued ahead of it. Checked
        // first since it needs no duration comparison at all.
        var crossesBoundary = fit.QueuedTailCrossesBoundary
            || (bestEffective is { } effectiveLength && effectiveLength >= fit.UntilBoundary);

        log.Log(fit, best is not null ? "least-late" : "unscored", offToleranceRung, sampled, bestDiff);
        var chosen = best ?? firstUnscored;
        LogRotationRelaxedOnce(chosen);
        return new MusicSelectionResult(chosen, offToleranceRung, crossesBoundary);
    }

    /// <summary>MED-3 (T361 review) — the ONE place <see cref="LogRotationRelaxed"/> fires per pick,
    /// called after <see cref="SelectMusicCandidateAsync"/> has settled on its FINAL winning
    /// candidate (whichever of up to <see cref="BoundarySampleAttempts"/> resamples that turned out
    /// to be) — never once per resample attempt, and never for a candidate that was sampled but not
    /// chosen. A <see langword="null"/> candidate or a zero <see cref="RotationCandidate.RotationRelax"/>
    /// (R0, or no rotation predicate at all) logs nothing.</summary>
    void LogRotationRelaxedOnce(RotationCandidate? candidate)
    {
        if (candidate?.RotationRelax is int relaxStep && relaxStep > 0)
            LogRotationRelaxed(relaxStep);
    }

    // Round-1 review finding F4 (PLAN T267): the off-tolerance classification (ClassifyOffToleranceRung),
    // the floor comparison it reads (IsBelowFloor), and the queue-crossing predicate ahead of it
    // (QueuedTailCrossesBoundary) all moved onto BoundaryFitPlan itself — pure functions of that
    // record's own fields, called as fit.ClassifyOffToleranceRung(MusicFloor) below, never duplicated
    // by hand here or at Orchestrator's own decline path. See BoundaryFitPlan's own remarks for the
    // full ruling (including the STORY-320 AC4 null-estimate degrade and why Orchestrator.ShouldDeclineFinalUnit
    // never needed to widen its own floor-only condition to match).

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
    ///
    /// <para>
    /// <b>SPEC F152.4 rotation relax ladder (STORY-372, PLAN T361) sits AHEAD of everything above,
    /// as an outer loop.</b> When <c>envelope.Rotation</c> is non-null,
    /// <see cref="SelectRotationRelaxedCandidateAsync"/> takes over entirely — it owns rung 0 AND an
    /// un-relaxed envelope-only attempt at every R step (HIGH-1, T361 review — see
    /// <see cref="TryRotationStepAsync"/>'s own remarks for why rung 0 alone is not enough), plus the
    /// SPEC F81.6 ladder, relaxing the F152 predicate through R0→R3 before either. When
    /// <c>envelope.Rotation</c> is null (no show/block ever set one), this method's own body below is
    /// BYTE-IDENTICAL to its pre-T361 shape — no outer loop, no <c>RotationRelax</c> stamped (STORY-372
    /// AC10) — <see cref="TryRungZeroAsync"/> is the same rung-0-plus-F81.5-recheck logic this method
    /// always ran, only extracted so <see cref="TryRotationStepAsync"/> can share it byte-for-byte at
    /// every relax step rather than duplicating it. <paramref name="quantileCache"/> is created ONCE
    /// per <see cref="SelectMusicCandidateAsync"/> pick and threaded through unchanged — MED-3 (T361
    /// review): the R2 rung's own quantile read must never re-query per boundary-bias resample
    /// attempt.
    /// </para>
    /// </summary>
    async Task<RotationCandidate?> SelectEnvelopeAwareCandidateAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        RotationQuantileCache quantileCache,
        CancellationToken ct)
    {
        var envelope = envelopeProvider.Current;
        // Captured alongside envelope, at the same read point (SPEC F91.7) — a resolver-backed
        // provider's Current/EnvelopeId are two independent reads of the same underlying snapshot,
        // mirroring IActivePersonaAccessor's own documented "two independent reads" shape; a
        // boundary landing in the narrow window between them degrades no worse than a
        // stale-but-consistent per-pick debug line.
        var envelopeId = envelopeProvider.EnvelopeId;

        // SPEC F152.4 — an outer loop, run ONLY when a rotation predicate is actually in force. With
        // no predicate this branch is never taken and the method below is untouched (STORY-372 AC10's
        // "byte-identical no-rotation path"). The pattern match hands the ladder a non-null
        // RotationPredicate directly — no null-forgiving operator needed on the other side.
        if (envelope.Rotation is { } rotation)
        {
            return await SelectRotationRelaxedCandidateAsync(
                scope, orderedRecentIds, artistSeparation, envelope, rotation, envelopeId, quantileCache, ct);
        }

        var personaPick = await TryRungZeroAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        if (personaPick is not null)
        {
            LogPerPickDebugLine(personaPick, DegradationStepNone, envelopeId);
            return personaPick;
        }

        var (candidate, degradationStep) =
            await SelectEnvelopeLadderAsync(scope, orderedRecentIds, artistSeparation, envelope, ct);
        if (candidate is not null)
            LogPerPickDebugLine(candidate, degradationStep, envelopeId);
        return candidate;
    }

    /// <summary>
    /// SPEC F81.6 rung 0 — the persona pick over <see cref="IMediaCatalog.GetEnvelopeCandidatePoolAsync"/>'s
    /// pool query, with the SPEC F81.5 trust-but-verify re-check inline. The ONE attempt shared,
    /// byte-for-byte, by <see cref="SelectEnvelopeAwareCandidateAsync"/>'s own no-rotation path and
    /// every rung (R0–R3) of <see cref="SelectRotationRelaxedCandidateAsync"/>'s SPEC F152.4 ladder —
    /// extracted at PLAN T361 so both read the identical "no persona opinion, or a discarded violation"
    /// outcome against whichever <paramref name="stepEnvelope"/> the caller is currently trying,
    /// rather than two hand-kept copies drifting apart. Returns <see langword="null"/> for "nothing at
    /// THIS envelope" — the caller decides what runs next.
    /// </summary>
    async Task<RotationCandidate?> TryRungZeroAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope stepEnvelope,
        CancellationToken ct)
    {
        var personaPick = await TryPersonaPickAsync(scope, orderedRecentIds, artistSeparation, stepEnvelope, ct);
        if (personaPick is null) return null;

        if (SatisfiesEnvelope(personaPick, stepEnvelope)) return personaPick;

        logger.LogWarning(
            "Persona pick {MediaId} violated the segment envelope on re-check ({Violation}) — " +
            "discarding and re-running envelope-only (SPEC F81.5, trust-but-verify).",
            personaPick.Media.MediaId, DescribeEnvelopeViolation(personaPick, stepEnvelope));
        return null;
    }

    /// <summary>
    /// SPEC F152.4's per-rung attempt (HIGH-1, T361 review): rung 0 (persona pick + F81.5 recheck,
    /// via <see cref="TryRungZeroAsync"/>) FIRST, then — when rung 0 has no opinion at all (no
    /// persona bound at all, the DEFAULT <see cref="NoOpPersonaPickProvider"/> binding; F91
    /// music-only segments; a persona-resolve fault; or a real ranker that simply declined) — the
    /// un-relaxed envelope-only pick (<see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/>) at THIS
    /// step's own <paramref name="stepEnvelope"/>, by-construction filtered to its genre/energy/
    /// rotation predicate (SPEC F81.4, F152.2) with NO F81.6 relaxation. Without this second leg,
    /// every persona-less pick — the common case, since <see cref="NoOpPersonaPickProvider"/> is the
    /// default — skipped every R step trivially (rung 0 alone always answers null with no persona
    /// bound) and fell straight through to <see cref="SelectEnvelopeLadderAsync"/> with the F152
    /// predicate already dropped: Deep Cuts silently became ordinary rotation, and T359's own
    /// by-construction predicate inside <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/> was
    /// unreachable in production. No separate F81.5 re-check applies to the envelope-only leg — its
    /// own WHERE clause already conforms the row to <paramref name="stepEnvelope"/> by construction,
    /// the same trust <see cref="SelectEnvelopeLadderAsync"/>'s own rung 1 places in it.
    /// </summary>
    async Task<RotationCandidate?> TryRotationStepAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope stepEnvelope,
        CancellationToken ct)
    {
        if (await TryRungZeroAsync(scope, orderedRecentIds, artistSeparation, stepEnvelope, ct) is { } personaPick)
            return personaPick;

        return await catalog.GetEnvelopeCandidateAsync(scope, orderedRecentIds, artistSeparation, stepEnvelope, ct);
    }

    /// <summary>
    /// SPEC F152.4 (STORY-372, PLAN T361) — the rotation relax ladder: <see cref="SelectEnvelopeAwareCandidateAsync"/>
    /// calls here ONLY when <paramref name="envelope"/> carries a <see cref="SegmentEnvelope.Rotation"/>
    /// predicate, entirely AHEAD of the SPEC F81.6 ladder below it (ARCHITECTURE.md's "Rotation
    /// predicate WHERE (Deep Cuts) — relax ladder R0→R3 BEFORE the F81.6 rungs"). Each rung tries the
    /// SAME two-legged attempt <see cref="TryRotationStepAsync"/> already runs (persona rung 0, THEN
    /// the un-relaxed envelope-only pick — HIGH-1, T361 review) — NEVER the F81.6 rotation-window/
    /// energy/genre rungs, which stay untouched until every relax step here has failed:
    /// <list type="bullet">
    /// <item><b>R0</b> — the predicate exactly as the show configured it.</item>
    /// <item><b>R1</b> — <c>MaxPlays + 1</c> and <c>NotAiredWithinDays / 2</c> (floored at 1 day),
    /// leaving whichever bound the show never set alone (still null).</item>
    /// <item><b>R2</b> — <c>MaxPlays</c> narrowed to the bottom decile of <c>play_count</c> across the
    /// envelope's own genre/energy-constrained pool (<see cref="IMediaCatalog.GetPlayCountQuantileAsync"/>,
    /// memoized per pick by <paramref name="quantileCache"/> — MED-3; the rotation predicate itself
    /// deliberately excluded from THAT read — LOW-2, the caller hands it <c>envelope with { Rotation =
    /// null }</c> so a third-party override can never make R2 circular by reading it back) — skipped
    /// outright when the catalog has nothing to compute a percentile over (a pre-F152 implementer's
    /// DIM default, or a genuinely empty pool), never a fabricated <c>MaxPlays: 0</c>. Also skipped
    /// (LOW-1) when the computed decile could not possibly admit anything R0 didn't already rule out
    /// (<c>p10 &lt;= rotation.MaxPlays</c> — a strict subset of that already-failed step; defensive:
    /// R0 having failed already guarantees every observed play_count exceeds <c>rotation.MaxPlays</c>,
    /// so this comparison guards a future change to the ladder's own ordering rather than a path
    /// today's R0/R1 semantics actually reach).</item>
    /// <item><b>R3</b> — the predicate dropped entirely, still trying the two-legged attempt first.</item>
    /// </list>
    /// If R3's own attempt ALSO yields nothing, this falls through to the existing
    /// <see cref="SelectEnvelopeLadderAsync"/> (rotation-window → energy → genres → the terminal
    /// pre-envelope query) with R3's predicate-dropped envelope — that ladder's own terminal rung never
    /// returns null (SPEC F81.6's never-silence floor), so <see cref="RotationCandidate.RotationRelax"/>
    /// pins to 3 unconditionally once execution reaches this point, regardless of which F81.6 rung
    /// actually supplied the pick (STORY-372 AC9: "never an unstamped R3").
    ///
    /// <para>
    /// MED-3 (T361 review): this method itself never logs the SPEC F152.4 relax notice any more —
    /// <see cref="LogRotationRelaxedOnce"/> is the ONE place that fires,
    /// after the boundary-bias resampler (if any) has settled on its final winning candidate, reading
    /// the step straight off <see cref="RotationCandidate.RotationRelax"/> rather than this method
    /// logging once per resample attempt.
    /// </para>
    /// </summary>
    async Task<RotationCandidate?> SelectRotationRelaxedCandidateAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        RotationPredicate rotation,
        string envelopeId,
        RotationQuantileCache quantileCache,
        CancellationToken ct)
    {
        if (await TryRotationStepAsync(scope, orderedRecentIds, artistSeparation, envelope, ct) is { } r0)
            return FinishRotationStep(r0, relaxStep: 0, envelopeId);

        var r1Envelope = envelope with
        {
            Rotation = new RotationPredicate(
                MaxPlays: rotation.MaxPlays is int maxPlays ? maxPlays + 1 : null,
                NotAiredWithinDays: rotation.NotAiredWithinDays is int days ? Math.Max(1, days / 2) : null),
        };
        if (await TryRotationStepAsync(scope, orderedRecentIds, artistSeparation, r1Envelope, ct) is { } r1)
            return FinishRotationStep(r1, relaxStep: 1, envelopeId);

        // LOW-2 (T361 review): Rotation explicitly nulled on the envelope handed to the quantile
        // read — the CONTRACT already says implementations must ignore it (see
        // IMediaCatalog.GetPlayCountQuantileAsync's own remarks), but a caller-side null makes that
        // unambiguous even for a third-party override that reads envelope.Rotation in general.
        var bottomDecile = await quantileCache.GetOrComputeAsync(
            catalog, scope, envelope with { Rotation = null }, BottomDecileQuantile, ct);
        var skipR2 = rotation.MaxPlays is int r0MaxPlays && bottomDecile <= r0MaxPlays; // LOW-1
        if (bottomDecile is int p10 && !skipR2)
        {
            var r2Envelope = envelope with { Rotation = new RotationPredicate(MaxPlays: p10) };
            if (await TryRotationStepAsync(scope, orderedRecentIds, artistSeparation, r2Envelope, ct) is { } r2)
                return FinishRotationStep(r2, relaxStep: 2, envelopeId);
        }

        var r3Envelope = envelope with { Rotation = null };
        if (await TryRotationStepAsync(scope, orderedRecentIds, artistSeparation, r3Envelope, ct) is { } r3)
            return FinishRotationStep(r3, relaxStep: 3, envelopeId);

        // Every attempt across R0..R3 came back empty — fall through to the existing F81.6 ladder
        // with the predicate fully dropped (R3's envelope), RotationRelax pinned to 3 regardless of
        // which F81.6 rung the ladder itself resolves to (never-silence by construction: that
        // ladder's own terminal rung never returns null).
        var (ladderCandidate, degradationStep) =
            await SelectEnvelopeLadderAsync(scope, orderedRecentIds, artistSeparation, r3Envelope, ct);
        if (ladderCandidate is null) return null;

        var stamped = ladderCandidate with { RotationRelax = MaxRotationRelaxStep };
        LogPerPickDebugLine(stamped, degradationStep, envelopeId);
        return stamped;
    }

    /// <summary>Stamps <paramref name="relaxStep"/> onto the winning candidate and fires the SAME
    /// F82.6 per-pick debug line every other pick already logs. MED-3 (T361 review): no longer logs
    /// the SPEC F152.4 relax notice itself — <see cref="SelectMusicCandidateAsync"/> does that once,
    /// after its own resampler has settled on a final winner, reading the step off the returned
    /// candidate's own <see cref="RotationCandidate.RotationRelax"/>.</summary>
    RotationCandidate FinishRotationStep(RotationCandidate candidate, int relaxStep, string envelopeId)
    {
        var stamped = candidate with { RotationRelax = relaxStep };
        LogPerPickDebugLine(stamped, DegradationStepNone, envelopeId);
        return stamped;
    }

    /// <summary>SPEC F152.4's relax notice, generic — LOW-3 (T361 review): no station-specific
    /// branding (the show's own name is not a seam this class carries; STORY-373/T362 is where a
    /// Shows-page-facing surface, if any, would add one). Fired exactly once per pick by
    /// <see cref="LogRotationRelaxedOnce"/>, never once per relax
    /// attempt.</summary>
    void LogRotationRelaxed(int relaxStep) =>
        logger.LogInformation(
            "Rotation rule relaxed to step {RotationRelax} (reason: pool empty at R{PreviousStep}) (SPEC F152.4).",
            relaxStep, relaxStep - 1);

    /// <summary>
    /// MED-3 (T361 review) — memoizes SPEC F152.4's R2 quantile read
    /// (<see cref="IMediaCatalog.GetPlayCountQuantileAsync"/>) across
    /// <see cref="SelectMusicCandidateAsync"/>'s up-to-<see cref="BoundarySampleAttempts"/> resample
    /// attempts for the SAME pick: the underlying pool cannot change mid-pick (nothing writes between
    /// resamples), so recomputing it on every attempt was both wasteful (up to 5 extra DB round trips
    /// per pick) and pointless (an identical answer every time). A single instance is created ONCE
    /// per <see cref="SelectMusicCandidateAsync"/> call and threaded through every resample attempt —
    /// never a field on <see cref="MusicSelectionPolicy"/> itself, so nothing leaks between
    /// concurrent picks or survives past the one that created it.
    /// </summary>
    sealed class RotationQuantileCache
    {
        bool computed;
        int? value;

        public async Task<int?> GetOrComputeAsync(
            IMediaCatalog catalog, LibraryScope scope, SegmentEnvelope envelope, double quantile, CancellationToken ct)
        {
            if (!computed)
            {
                value = await catalog.GetPlayCountQuantileAsync(scope, envelope, quantile, ct);
                computed = true;
            }

            return value;
        }
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
    /// SPEC F82.6/F91.7/F151.4 — the one per-pick debug line: envelope id, pool size, the winning
    /// pick's top-3 scores, the SAME Top-K's top-3 nudges (SPEC F151.4, STORY-371, PLAN T370), which
    /// taste rules fired, the exploration flag, and which degradation rung (SPEC F81.6) actually
    /// supplied the pick. Fires on EVERY music pick — persona-off included — so the ladder's own
    /// degradation step is always visible, mirroring the <c>LiquidsoapControl</c> per-command
    /// convention (a per-tick line belongs at Debug, not Information — SPEC F82.6's own "per-pick"
    /// framing puts it in the same high-frequency bucket).
    /// <paramref name="candidate"/>'s <see cref="RotationCandidate.PersonaPick"/> is null for every
    /// envelope-only ladder pick (including the common case where no persona is even active) — the
    /// pool/top3/nudges/firedRules/exploration fields all read as empty/false in that case, never
    /// omitted from the line. <paramref name="envelopeId"/> is <see cref="envelopeProvider"/>'s own
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
        // SPEC F151.4 (STORY-371, PLAN T370) — TopScores' own index-aligned sibling, empty for the
        // SAME envelope-only/persona-off case TopScores itself reads as empty.
        var topNudges = diagnostics is null
            ? ""
            : string.Join(", ", diagnostics.TopNudges.Select(n => n.ToString("F2", CultureInfo.InvariantCulture)));
        var firedRules = diagnostics is null
            ? ""
            : string.Join("; ", diagnostics.FiredRules.Select(FormatFiredRule));

        logger.LogDebug(
            "Pick — envelope={EnvelopeId} pool={PoolSize} top3=[{TopScores}] nudges=[{TopNudges}] " +
            "firedRules=[{FiredRules}] exploration={IsExploration} degradation={DegradationStep}",
            envelopeId, diagnostics?.PoolSize ?? 0, topScores, topNudges, firedRules,
            diagnostics?.IsExploration ?? false, degradationStep);
    }

    /// <summary>One short "what:weight" summary per fired taste rule for the debug line — not a full serialization.</summary>
    static string FormatFiredRule(TasteRule rule) =>
        $"{rule.Predicate.LabelOr("any")}:{rule.Weight.ToString("F2", CultureInfo.InvariantCulture)}";
}
