namespace GenWave.Orchestration;

/// <summary>
/// SPEC F142 (STORY-356, PLAN T327, closes gh-#300) — the boundary-fit lookahead tail (the
/// <see cref="IBoundaryBiasProvider"/> window a Host binds from
/// <c>Station:BoundaryBias:LookaheadMinutes</c>) must cover <see cref="Orchestrator.SignOffLeadTime"/>
/// plus the worst-case gap between two feeder pulls, or the last pull before a boundary can miss the
/// window entirely — the 2:05 handoff (gh-#300 direction 3): a full unit planned inside
/// <c>[due − tail, due)</c> because nothing related the two.
///
/// <para>
/// Pure and framework-free by construction (same L1 posture as <see cref="CrosstalkBreakWindow"/>
/// one seam over) — every term this reasons from is a parameter, never a static reach-back, so the
/// Host-side <c>BoundaryCadenceCovenantPostConfigure</c> (the only caller) pins each one to the
/// value IT owns — the configured knob, <see cref="Orchestrator.SignOffLeadTime"/>, and the
/// feeder's own tick interval — with nothing hidden inside this seam.
/// </para>
///
/// <para>
/// <b>Why this stays even though the clamp is unreachable at every value the knob can represent
/// today (T327 review FAIL-2):</b> <c>SignOffLeadTime</c> (15s) + a 3s feeder pull gap = 18s
/// required, which <see cref="CeilToGrain"/> ceils to one whole minute — the SAME resolution
/// <c>Station:BoundaryBias:LookaheadMinutes</c>'s <c>int</c> storage already enforces on every
/// nonzero value it can hold. So <c>boundRequired</c> always equals exactly the smallest nonzero
/// value the knob can represent, and the clamp (<c>configuredLookahead &lt; boundRequired</c>)
/// would need <c>0 &lt; configuredLookahead &lt; boundRequired</c> to fire — no such <c>int</c>
/// minute exists. This is a property of the GRAIN-vs-SMALLEST-REPRESENTABLE-VALUE relationship in
/// the CODE PATH, not of today's 10-minute default: every nonzero minute value is equally
/// unreachable, not just the shipped one.
/// </para>
///
/// <para>
/// Of this type's three terms, only two future changes reopen that gap: <c>SignOffLeadTime</c> plus
/// the feeder's pull interval growing past one whole grain (&gt;60s today — a ~3.3× increase from
/// 18s) pushes <c>boundRequired</c> to a SECOND grain multiple while the smallest representable
/// value stays pinned at the first, separating the two; or the knob re-unitizing (e.g. seconds
/// instead of minutes) shrinking the smallest representable value below a still-1-minute grain. The
/// knob's FLOOR moving on its own cannot: <see cref="CeilToGrain"/> and "the smallest nonzero
/// representable value" both track the SAME grain, so they move together and stay equal as long as
/// the required sum fits inside it. This is NOT PLAN T326's F3 finding restated: that removal
/// deleted an <c>Expired</c> enum state proven genuinely UNREACHABLE by any code path, full stop.
/// This covenant's unreachability is CONDITIONAL — reachable the instant the required sum outgrows
/// its grain, or the grain stops matching the knob's storage resolution. Do not delete it as dead
/// code. <c>Evaluate(FromMinutes(1), 15s, 3s, grain 1min)</c> (this type's own Orchestration.Tests
/// spec) pins the unreachable floor; the same spec's 70s signOffLeadTime case (73s required, ceiled
/// to 120s) pins the reachable case once the terms outgrow a grain.
/// </para>
///
/// <para>
/// Zero is exempt by construction (F142 vs F74.3): <c>Station:BoundaryBias:LookaheadMinutes = 0</c>
/// is <see cref="IBoundaryBiasProvider"/>'s documented kill switch ("zero disables the bias
/// entirely") — a covenant built to protect the boundary-fit window must never turn a deliberately
/// DISABLED bias into one silently re-enabled at the covenant's floor. Modeled once, here, rather
/// than short-circuited at each call site.
/// </para>
/// </summary>
public static class BoundaryCadenceCovenant
{
    /// <summary>
    /// Evaluates the covenant for a candidate <paramref name="configuredLookahead"/> against
    /// <paramref name="signOffLeadTime"/> and <paramref name="worstCasePullGap"/> — both caller-owned
    /// and read fresh from the arguments, never from a static source (F142.2's "no new knobs" ruling
    /// covers the constants themselves, not how this seam receives them — see this type's own
    /// remarks). <paramref name="grain"/> is the configured knob's own resolution (one whole minute
    /// for <c>LookaheadMinutes</c>'s <c>int</c> storage): the clamp lands on a value that whole-number
    /// knob can actually represent, so the WARN this produces names the clamp that truly binds.
    ///
    /// <para>
    /// Never clamps down (fail-safe, F142.2): a lookahead already covering the covenant binds
    /// verbatim. <see cref="TimeSpan.Zero"/> is the one exception to that comparison, not a special
    /// case of it — it binds verbatim too, but because zero is not an under-covering lookahead, it
    /// is the feature switched off (see this type's own remarks).
    /// </para>
    ///
    /// <para>
    /// <paramref name="grain"/>, <paramref name="signOffLeadTime"/> and
    /// <paramref name="worstCasePullGap"/> are all validated BEFORE the
    /// <paramref name="configuredLookahead"/> zero early-return (T327 review A2 — consistent
    /// fail-fast regardless of which branch would otherwise run first); the latter two must be
    /// non-negative so <see cref="CeilToGrain"/>'s own "rounds UP" contract holds for every value it
    /// is ever called with (T327 review A3 — integer division floors negatives, so a negative input
    /// would silently break that contract instead of failing loud).
    /// </para>
    /// </summary>
    public static BoundaryCadenceCovenantResult Evaluate(
        TimeSpan configuredLookahead, TimeSpan signOffLeadTime, TimeSpan worstCasePullGap, TimeSpan grain)
    {
        if (grain <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(grain), grain, "grain must be positive.");
        if (signOffLeadTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(signOffLeadTime), signOffLeadTime, "signOffLeadTime must be non-negative.");
        if (worstCasePullGap < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(worstCasePullGap), worstCasePullGap, "worstCasePullGap must be non-negative.");

        if (configuredLookahead == TimeSpan.Zero)
            return new BoundaryCadenceCovenantResult(
                configuredLookahead, signOffLeadTime, worstCasePullGap, TimeSpan.Zero);

        var required = signOffLeadTime + worstCasePullGap;
        var boundRequired = CeilToGrain(required, grain);
        var bound = configuredLookahead < boundRequired ? boundRequired : configuredLookahead;
        return new BoundaryCadenceCovenantResult(configuredLookahead, signOffLeadTime, worstCasePullGap, bound);
    }

    /// <summary>
    /// Rounds <paramref name="value"/> UP to the nearest whole multiple of <paramref name="grain"/>,
    /// via integer tick arithmetic (never <see cref="Math.Ceiling(double)"/> — no floating-point
    /// rounding surprise between this and the value that actually gets bound). Private to
    /// <see cref="Evaluate"/>, its only caller, which validates both <paramref name="value"/>
    /// (non-negative) and <paramref name="grain"/> (positive) before ever reaching here — this "UP"
    /// contract only needs to hold for the non-negative inputs <see cref="Evaluate"/> guarantees.
    /// </summary>
    static TimeSpan CeilToGrain(TimeSpan value, TimeSpan grain)
    {
        var wholeUnits = value.Ticks / grain.Ticks;
        if (value.Ticks % grain.Ticks != 0)
            wholeUnits++;

        return TimeSpan.FromTicks(wholeUnits * grain.Ticks);
    }
}
