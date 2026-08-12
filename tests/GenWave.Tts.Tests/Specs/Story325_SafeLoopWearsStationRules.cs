// STORY-325 — The safe loop wears station rules (SPEC F126.3 · PLAN VQ-h, T276)
//
// BDD specification — xUnit, pending until /build-loop turns them green. The seam audit's
// second bypass: SafeSegmentAuthor (the POST /api/safe-segments endpoint AND the boot
// seed — one code path) calls the context-less overload, so safe clips render with empty
// rules forever. The fix authors through the context overload with the STATION's resolved
// rules — the safe loop is the station's voice; persona rules never apply, pace stays 1.0.
// One assertion per Fact; happy first; sad segregated.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSafeLoopWearsStationRules
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public static class ScenarioAuthoringResolvesStationRules
    {
        [Fact(Skip = "Pending T276 — see docs/PLAN.md")]
        public static void The_render_goes_through_the_context_overload_with_station_rules()
        {
            // Given saved station pronunciation rules
            // When  a safe segment is authored
            // Then  the fake engine's captured context carries the station's resolved set
            Assert.Fail("pending T276");
        }

        [Fact(Skip = "Pending T276 — see docs/PLAN.md")]
        public static void The_boot_seed_takes_the_same_path()
        {
            // One code path (the class's own documented posture) — the seed's clip
            // carries the rules too.
            Assert.Fail("pending T276");
        }

        [Fact(Skip = "Pending T276 — see docs/PLAN.md")]
        public static void Pace_stays_the_default()
        {
            // The station has no VoiceSpec.Pace — 1.0 by construction.
            Assert.Fail("pending T276");
        }
    }

    public static class ScenarioPersonaRulesDoNotApply
    {
        [Fact(Skip = "Pending T276 — see docs/PLAN.md")]
        public static void Only_station_rules_are_resolved_for_a_safe_authoring()
        {
            // Given persona-scoped rules exist
            // Then none of them reach the captured context — the safe loop is the
            // station's voice.
            Assert.Fail("pending T276");
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioALaterRuleChangeDoesNotRewriteHistory
    {
        [Fact(Skip = "Pending T276 — see docs/PLAN.md")]
        public static void An_existing_clip_is_unchanged_when_a_rule_is_saved()
        {
            // The stale-cue posture: a safe clip is a persisted catalog row;
            // re-authoring is the fix, stated at the endpoint.
            Assert.Fail("pending T276");
        }
    }
}
