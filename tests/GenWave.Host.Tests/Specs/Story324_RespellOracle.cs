// STORY-324 — The respell oracle (SPEC F126.2 · PLAN VQ-h, T278)
//
// BDD specification — xUnit, pending until /build-loop turns them green. espeak-ng vendored
// in the api image as a respell→IPA oracle: the operator types "muh-KLOWD", an owner-only
// endpoint derives candidate IPA, the STORY-323 audition confirms it. Argv-only invocation
// (no shell — the Process.Start injection class is structurally absent), never on a render
// path. Entry-point discipline: scenarios drive the real route through
// WebApplicationFactory<Program> with the oracle binary faked at its adapter seam. The
// T280 wire acceptance (derive→audition→save→next-spoken-line in a real browser) is a
// production check, deliberately not represented here.

namespace GenWave.Host.Tests.Specs;

public static class FeatureRespellOracle
{
    // ── HAPPY PATH (real route, WebApplicationFactory) ──────────────────────

    public static class ScenarioARespellingDerivesCandidateIpa
    {
        [Fact(Skip = "Pending T278 — see docs/PLAN.md")]
        public static void The_derive_endpoint_returns_candidate_ipa_for_a_respelling()
        {
            // Given espeak-ng is present (faked at the adapter seam)
            // When  the owner posts a respelling
            // Then  candidate IPA returns
            Assert.Fail("pending T278");
        }

        [Fact(Skip = "Pending T278 — see docs/PLAN.md")]
        public static void The_invocation_is_argv_only_with_no_shell()
        {
            // The adapter's captured invocation is a ProcessStartInfo ArgumentList —
            // never a composed shell string.
            Assert.Fail("pending T278");
        }

        [Fact(Skip = "Pending T278 — see docs/PLAN.md")]
        public static void The_endpoint_is_owner_only()
        {
            Assert.Fail("pending T278");
        }
    }

    public static class ScenarioTheOracleNeverSitsOnARenderPath
    {
        [Fact(Skip = "Pending T278 — see docs/PLAN.md")]
        public static void No_on_air_render_reaches_the_oracle_adapter()
        {
            // The F90.8 DI-closure-walk idiom: the playout/render object graph does not
            // contain the oracle seam.
            Assert.Fail("pending T278");
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioAnImageWithoutEspeakDegradesToHiding
    {
        [Fact(Skip = "Pending T278 — see docs/PLAN.md")]
        public static void The_derive_endpoint_answers_501_when_the_binary_is_absent()
        {
            // The assist hides (T279's UI half keys off this); raw-IPA authoring and the
            // STORY-323 audition loop stand alone.
            Assert.Fail("pending T278");
        }
    }

    public static class ScenarioInputIsCappedAndInert
    {
        [Fact(Skip = "Pending T278 — see docs/PLAN.md")]
        public static void An_over_length_respelling_400s()
        {
            Assert.Fail("pending T278");
        }

        [Fact(Skip = "Pending T278 — see docs/PLAN.md")]
        public static void Shell_metacharacters_reach_the_adapter_as_inert_argv_data()
        {
            // "$(rm -rf /)" is a weird respelling, not a command — captured verbatim as
            // one argument.
            Assert.Fail("pending T278");
        }
    }
}
