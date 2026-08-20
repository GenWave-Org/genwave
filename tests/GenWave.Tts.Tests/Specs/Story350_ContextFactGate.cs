// STORY-350 — Context copy can't invent facts (SPEC F138.1, F138.2, F138.4 · PLAN T329/T331/T335)
//
// BDD specification — xUnit. PENDING: every Specification is skipped until its task builds
// the behavior (the wizard-epic compile-clean-pending convention). /build-loop unskips per
// task; a body still failing after its task is a defect, not a pending.
//
// The gh-#434 aired exhibit is the pinned regression: facts "Edmonton: overcast, 15°C.
// Today's high 21°C, low 12°C." produced copy claiming "6 degrees below", "sunshine",
// and "today is saturday" — three fabrications, all aired. The gate is deterministic
// armor at the LlmCopyWriter seam: prompt asks (F138.5), checker enforces (F138.2),
// ladder degrades re-ask-once → template (F138.4), never silence (F107.6).

namespace GenWave.Tts.Tests.Specs;

public static class FeatureContextFactGate
{
    public static class ScenarioSupportedCopyPassesUntouched
    {
        // Given the gh-#434 fact block / When copy claims only overcast, 15, 21, or 12
        [Fact(Skip = "pending T329 — CopyClaims checker does not exist yet")]
        public static void Copy_with_only_supported_claims_passes_unchanged() =>
            Assert.Fail("pending T329: supported digits/conditions pass the checker with zero violations");

        [Fact(Skip = "pending T329 — CopyClaims checker does not exist yet")]
        public static void A_supported_claim_is_matched_case_insensitively() =>
            Assert.Fail("pending T329: 'Overcast' in copy matches 'overcast' in facts");
    }

    public static class ScenarioInventedClaimsAreCaught
    {
        // Given the same fact block / When the copy fabricates
        [Fact(Skip = "pending T329")]
        public static void An_unsupported_digit_run_is_reported() =>
            Assert.Fail("pending T329: '6 degrees below' against 15/21/12 facts yields a digit violation naming '6'");

        [Fact(Skip = "pending T329")]
        public static void An_unsupported_condition_word_is_reported() =>
            Assert.Fail("pending T329: 'sunshine' against overcast facts yields a condition violation");

        [Fact(Skip = "pending T329")]
        public static void An_unsupported_weekday_is_reported() =>
            Assert.Fail("pending T329: 'today is saturday' with no weekday in facts yields a weekday violation");
    }

    public static class ScenarioTheLadderDegrades
    {
        // Given a first completion that fails the gate (stub LLM serving poisoned copy
        // through the production LlmCopyWriter seam — the entry-point scenario)
        [Fact(Skip = "pending T331 — gate not wired at the LlmCopyWriter seam yet")]
        public static void Exactly_one_reask_is_issued() =>
            Assert.Fail("pending T331: the writer retries once, never more");

        [Fact(Skip = "pending T331")]
        public static void The_reask_prompt_names_the_violating_claim() =>
            Assert.Fail("pending T331: the retry prompt contains the rejected claim text");

        [Fact(Skip = "pending T331")]
        public static void A_failing_reask_lands_on_the_template() =>
            Assert.Fail("pending T331: second violation airs the deterministic template line (F107.6 — never silence)");

        [Fact(Skip = "pending T331")]
        public static void The_guard_line_rides_the_prompt() =>
            Assert.Fail("pending T331: the system prompt carries the comma-free weekday/daypart guard line (F138.5)");
    }

    public static class SadPathCheckerDiscipline
    {
        [Fact(Skip = "pending T329")]
        public static void The_checker_is_pure() =>
            Assert.Fail("pending T329: reflection shows a static class, no instance state, no I/O (the F68.6 posture)");

        [Fact(Skip = "pending T331")]
        public static void Budget_exhaustion_degrades_to_template_not_a_longer_hold() =>
            Assert.Fail("pending T331: an exhausted render budget skips the re-ask and airs the template");
    }
}
