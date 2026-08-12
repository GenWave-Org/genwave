// STORY-323 — Auditions tell the truth (SPEC F126.1/.4 · PLAN VQ-h, T274)
//
// BDD specification — xUnit, pending until /build-loop turns them green. The seam audit's
// finding: POST /api/tts/preview deliberately bypasses TtsSegmentSource and renders
// WITHOUT pronunciation rules — an audition surface that lies about the thing it
// auditions, and the exact surface the IPA-UX ruling leans on. Entry-point discipline:
// the happy-path scenarios drive the real route through WebApplicationFactory<Program>
// (the Story234/Story279 idiom). AC3 — the fitness law (no production call site invokes
// the context-less SynthesizeAsync overload outside the normalizer/fallback relays) —
// deliberately lives in GenWave.Architecture.Tests as T277, not in this file: one home
// per law, beside the F105 laws it joins.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAuditionsTellTheTruth
{
    // ── HAPPY PATH (real route, WebApplicationFactory) ──────────────────────

    public static class ScenarioThePreviewRendersThroughTheResolvedRules
    {
        [Fact(Skip = "Pending T274 — see docs/PLAN.md")]
        public static void A_saved_rule_matching_the_preview_text_reaches_the_engine_request()
        {
            // Given a saved pronunciation rule matching the preview text
            // When  POST /api/tts/preview renders through the real route
            // Then  the fake engine's captured request carries the rule's IPA markup —
            //       the context overload, not the context-less bypass
            Assert.Fail("pending T274");
        }

        [Fact(Skip = "Pending T274 — see docs/PLAN.md")]
        public static void The_merge_is_the_same_station_union_persona_resolution_the_air_chain_uses()
        {
            Assert.Fail("pending T274");
        }
    }

    public static class ScenarioACandidateRuleLayersOverTheMerge
    {
        [Fact(Skip = "Pending T274 — see docs/PLAN.md")]
        public static void An_unsaved_candidate_rule_applies_on_top_of_the_resolved_merge()
        {
            // Given a preview request carrying an unsaved candidate rule
            // Then the editor auditions the exact rule being authored, before saving
            Assert.Fail("pending T274");
        }

        [Fact(Skip = "Pending T274 — see docs/PLAN.md")]
        public static void A_candidate_shadowing_a_saved_rule_wins_for_this_render_only()
        {
            // Layering means the candidate pre-empts the same (pattern, word) — and
            // nothing is persisted by a preview.
            Assert.Fail("pending T274");
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioAMalformedCandidateFailsTheRequestNotTheStation
    {
        [Fact(Skip = "Pending T274 — see docs/PLAN.md")]
        public static void A_blank_pattern_400s_naming_the_field()
        {
            Assert.Fail("pending T274");
        }

        [Fact(Skip = "Pending T274 — see docs/PLAN.md")]
        public static void No_render_runs_for_a_rejected_candidate()
        {
            Assert.Fail("pending T274");
        }
    }

    public static class ScenarioThePreviewStaysOwnerOnly
    {
        [Fact(Skip = "Pending T274 — see docs/PLAN.md")]
        public static void An_unauthenticated_caller_gets_the_existing_admin_surface_answer()
        {
            // No new exposure: the same policy posture the route already had.
            Assert.Fail("pending T274");
        }
    }
}
