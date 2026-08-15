namespace GenWave.Orchestration;

/// <summary>
/// SPEC F127.7's "never inside a break window" (STORY-328, PLAN T286) — the pure decider
/// <c>CrosstalkStockWorker</c>'s Host timer shell consults both before starting a generation attempt
/// and, periodically, while one is already in flight. Framework-free by construction (L1) — plain
/// <see cref="DateTimeOffset"/>/<see cref="TimeSpan"/> comparisons and one caller-supplied bool, so
/// the gating rule is unit-testable with no Host, no database, no ollama.
///
/// <para>
/// <b>The render fires on the FAR side of the transition, not near an item's END (PLAN T286 review
/// F1 — corrects this class's own original remarks).</b> On-air copy renders happen inside
/// <c>PlayoutFeeder.RefillAsync</c> (a 30s budget, serialized LLM+TTS), which
/// <c>PlayoutFeederService</c> calls immediately AFTER publishing the fresh
/// <c>NowPlayingSnapshot</c> for the item that just came on air (gh-#184's own ordering — publish
/// first so the UI never waits behind the render). So the hazard begins the INSTANT a track
/// transitions, not near its end: an end-of-item-only fence closes exactly when the render starts
/// and stands open for the idle middle of a long track, the opposite of what SPEC F127.7 needs.
/// <see cref="IsOpen"/> therefore gates on THREE independent faces, any one of which is enough to
/// block — see each numbered check's own remarks below.
/// </para>
///
/// <para>
/// <b>Why a time-based prediction, not the boundary/handoff machinery (build-time decision, T286).</b>
/// <c>Orchestrator</c>'s own boundary-fit/handoff-ceremony apparatus (<c>BoundaryFitPlan</c>,
/// <c>SpeechDeferralQueue</c>) already answers "when does a break approach" precisely — but reaching
/// it from a Host-side worker would be NEW Core↔Orchestration coupling of exactly the kind PLAN
/// T266/T267's own rejected alternative (ARCHITECTURE.md's boundary-ladder decision table: "a feeder
/// drain-completion callback... new Core↔Orchestration coupling for a case an honest floor already
/// bounds") already ruled out once, for the identical reason. This type instead answers a narrower,
/// Host-observable question from whatever a caller already has in hand: <c>NowPlayingSnapshot</c>'s
/// own <c>StartedAt</c>/<c>DurationMs</c>, published every feeder tick with zero extra I/O, plus (PLAN
/// T286 review F1) <c>OnAirRenderGate</c>'s own real in-flight signal.
/// </para>
/// </summary>
public static class CrosstalkBreakWindow
{
    /// <summary>
    /// The safety margin this class applies on BOTH sides of a render (PLAN T286 review F1 widens
    /// its original single, end-only use): before an on-air item's own estimated end, and after
    /// <see cref="RenderBudget"/> from the item's own start has elapsed. Sized to comfortably clear
    /// this worker's own watchdog poll interval and for cancellation to actually take effect — not
    /// tuned to the crosstalk generation's OWN duration, which is exactly the quantity
    /// <see cref="IsOpen"/> exists to never let run into either window at all.
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Is a break window open right now? <see langword="true"/> — fail-closed on every unknown
    /// state — whenever ANY of:
    /// <list type="number">
    /// <item><paramref name="refillInFlight"/> (PLAN T286 review F1) — the real signal: a genuine
    /// on-air <c>PlayoutFeeder.RefillAsync</c> render is running RIGHT NOW (<c>OnAirRenderGate</c>).
    /// Checked first — nothing else needs evaluating once this is true.</item>
    /// <item>The render-window-after-transition (predictive): <paramref name="onAirStartedAt"/> is
    /// <see langword="null"/> (no snapshot published yet), or fewer than
    /// <paramref name="renderBudget"/> + <see cref="Margin"/> have elapsed since it — comfortably
    /// covers the class remarks' own "render fires on the far side of the transition" hazard even
    /// when <c>OnAirRenderGate</c> has not (yet) observed it in flight.</item>
    /// <item>The end-of-item margin (imminent-transition): <paramref name="estimatedOnAirEndsAt"/> is
    /// <see langword="null"/> (an engine-initiated/foreign advance, or no tick has published a
    /// snapshot yet), or sits within <see cref="Margin"/> of <paramref name="now"/> — a late-running
    /// generation attempt must not itself spill into the NEXT transition's own render.</item>
    /// </list>
    /// A discard here costs nothing worse than a later stock-fill attempt (SPEC F127.7's own
    /// "opportunistic, off the clock" framing); airing crosstalk-generation latency against a genuine
    /// on-air render is the failure this exists to make structurally unreachable, so an unknown state
    /// is never treated as permission.
    /// </summary>
    public static bool IsOpen(
        DateTimeOffset now,
        DateTimeOffset? estimatedOnAirEndsAt,
        DateTimeOffset? onAirStartedAt,
        TimeSpan renderBudget,
        bool refillInFlight)
    {
        if (refillInFlight)
            return true;

        if (onAirStartedAt is not { } startedAt || now - startedAt <= renderBudget + Margin)
            return true;

        if (estimatedOnAirEndsAt is not { } endsAt || endsAt - now <= Margin)
            return true;

        return false;
    }
}
