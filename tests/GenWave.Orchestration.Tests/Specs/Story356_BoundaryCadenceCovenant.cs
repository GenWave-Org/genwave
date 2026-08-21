// STORY-356 — The boundary covenant holds by construction (SPEC F142 · PLAN T327)
//
// BDD specification — xUnit.
//
// The 2:05 handoff (gh-#300) was this invariant violated silently: nothing related the
// fit lookahead to SignOffLeadTime and the pull cadence, so a full unit could be planned
// inside the un-declinable window. Directions 1 (decline) and 2 (fit logging) shipped;
// this is direction 3 — the relationship becomes a bind-time law. Closes gh-#300.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBoundaryCadenceCovenant
{
    public static class ScenarioAViolatingConfigurationClampsUp
    {
        [Fact]
        public static void The_lookahead_clamps_up_to_cover_the_covenant()
        {
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.FromSeconds(7),
                signOffLeadTime: TimeSpan.FromSeconds(15),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            // 15s + 3s = 18s required, ceiled to the knob's one-whole-minute grain: 60s.
            Assert.Equal(TimeSpan.FromMinutes(1), result.BoundLookahead);
        }

        [Fact]
        public static void One_warn_names_all_three_values_and_the_clamp()
        {
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.FromSeconds(7),
                signOffLeadTime: TimeSpan.FromSeconds(15),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            // Distinct digits (7 / 15 / 3 / 60), chosen so none is a substring of another — a
            // false positive here (e.g. "15s" trivially containing "5s") would hide a mislabeled
            // term going out in the real WARN. The clamp is 60s (1 whole minute, the knob's own
            // grain) — NOT the raw 18s requirement (T327 review F2): BoundLookahead IS the value
            // that binds, by construction, so this fact and the applied clamp can never diverge.
            Assert.True(
                result.WarningMessage is { } message
                && message.Contains("7s", StringComparison.Ordinal)      // the configured lookahead
                && message.Contains("15s", StringComparison.Ordinal)     // SignOffLeadTime
                && message.Contains("3s", StringComparison.Ordinal)      // the worst-case pull gap
                && message.Contains("60s", StringComparison.Ordinal),    // the applied clamp
                $"expected the one WARN to name lookahead=7s, SignOffLeadTime=15s, pullGap=3s and " +
                $"clamp=60s, got: {result.WarningMessage}");
        }
    }

    public static class ScenarioSignOffLeadTimeIsAGenuineParameter
    {
        [Fact]
        public static void The_required_floor_tracks_whatever_sign_off_lead_time_it_is_given()
        {
            // Evaluate takes SignOffLeadTime as a parameter — it never reaches back into
            // Orchestrator itself (T327 review advisory) — pinned here at a value other than
            // today's shipped 15s so a future edit that reintroduces a hidden static read would
            // break this fact, not just the Host wiring that actually passes Orchestrator.SignOffLeadTime.
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.FromSeconds(7),
                signOffLeadTime: TimeSpan.FromSeconds(70),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            // 70s + 3s = 73s required, ceiled to the one-minute grain: 120s (2 minutes).
            Assert.Equal(TimeSpan.FromMinutes(2), result.BoundLookahead);
        }
    }

    public static class ScenarioTheClampsReachabilityBoundaryIsPinned
    {
        [Fact]
        public static void The_smallest_nonzero_knob_value_already_covers_the_covenant()
        {
            // T327 review FAIL-2: at today's terms (15s SignOffLeadTime + 3s pull gap = 18s
            // required, ceiled to the knob's own one-minute grain) boundRequired is EXACTLY the
            // smallest nonzero value Station:BoundaryBias:LookaheadMinutes's int storage can ever
            // represent — so the clamp is unreachable at ANY representable nonzero configuration,
            // not just today's shipped 10-minute default. This is that boundary, pinned: the
            // smallest possible nonzero configuredLookahead does NOT clamp.
            //
            // ScenarioSignOffLeadTimeIsAGenuineParameter's 70s case immediately above is this
            // fact's counterpart — proof the clamp DOES live once the required sum outgrows one
            // whole grain (73s ceils to a second grain multiple, 120s, which the smallest
            // representable value no longer covers).
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.FromMinutes(1),
                signOffLeadTime: TimeSpan.FromSeconds(15),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            Assert.False(result.WasClamped);
        }
    }

    public static class ScenarioASatisfyingConfigurationBindsSilently
    {
        [Fact]
        public static void No_clamp_is_applied()
        {
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.FromMinutes(10),
                signOffLeadTime: TimeSpan.FromSeconds(15),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            Assert.Equal(TimeSpan.FromMinutes(10), result.BoundLookahead);
        }

        [Fact]
        public static void No_warn_is_logged()
        {
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.FromMinutes(10),
                signOffLeadTime: TimeSpan.FromSeconds(15),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            Assert.Null(result.WarningMessage);
        }
    }

    public static class ScenarioTheDisabledKillSwitchIsNeverClamped
    {
        [Fact]
        public static void Zero_stays_zero_because_zero_is_the_kill_switch()
        {
            // Station:BoundaryBias:LookaheadMinutes = 0 is IBoundaryBiasProvider's documented
            // disable (T327 review F1): configured=0 here is LESS than the 18s the covenant would
            // otherwise require, so a covenant blind to "disabled" would clamp it UP to 60s —
            // silently turning a deliberately OFF bias back ON at the covenant's floor.
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.Zero,
                signOffLeadTime: TimeSpan.FromSeconds(15),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            Assert.True(
                result.BoundLookahead == TimeSpan.Zero
                && !result.WasClamped
                && result.WarningMessage is null,
                $"expected zero to pass through unclamped and unwarned, got BoundLookahead=" +
                $"{result.BoundLookahead}, WasClamped={result.WasClamped}, WarningMessage=" +
                $"{result.WarningMessage}");
        }
    }

    public static class SadPathTheShippedLadderIsUntouched
    {
        [Fact]
        public static void The_shipped_ten_minute_default_is_a_covenant_no_op()
        {
            // Gh300_DeclineTheFinalUnit.cs builds every incident's Orchestrator against
            // FakeBoundaryBiasProvider(TimeSpan.FromMinutes(10)) — the shipped
            // Station:BoundaryBias:LookaheadMinutes default (StationBoundaryBiasOptions). For
            // "rung=CeremonyOnly is byte-identical" to actually hold, the covenant must be a
            // true no-op at that exact configuration: this is the one assertion tying the two
            // files together without re-running the whole incident arrangement here.
            var result = BoundaryCadenceCovenant.Evaluate(
                configuredLookahead: TimeSpan.FromMinutes(10),
                signOffLeadTime: TimeSpan.FromSeconds(15),
                worstCasePullGap: TimeSpan.FromSeconds(3),
                grain: TimeSpan.FromMinutes(1));

            Assert.False(result.WasClamped);
        }
    }
}
