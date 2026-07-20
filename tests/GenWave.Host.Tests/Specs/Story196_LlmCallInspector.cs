// STORY-196 — LLM call inspector
//
// BDD specification — xUnit (SPEC F73.1–F73.3). Pending scaffold; /build-loop (PLAN T41)
// implements and removes Skip.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureLlmCallInspector
{
    private const string Pending = "pending — PLAN T41 (/build-loop)";

    public static class ScenarioRingContents
    {
        [Fact(Skip = Pending)]
        public static void Entries_carry_prompt_response_timing_status_and_mode_capped_at_ring_size()
        {
            // Given LLM calls made through the pipeline
            // When  the inspector endpoint is read as admin
            // Then  entries carry prompt, response, timing, status, and the mode active
            //       at call time, capped at ring size (F73.1)
            Assert.Fail(Pending);
        }
    }

    public static class SadPathAccessAndPersistence
    {
        [Fact(Skip = Pending)]
        public static void Inspector_is_unreachable_without_admin_on_both_listeners()
        {
            // Given no credentials and spectator mode on
            // When  the inspector endpoint is called on both listeners
            // Then  the response is 401/403/404 and never inspector content (F73.2)
            Assert.Fail(Pending);
        }

        [Fact(Skip = Pending)]
        public static void Ring_is_empty_after_restart_and_nothing_was_persisted()
        {
            // Given inspector entries in the ring
            // When  the host restarts
            // Then  the ring is empty and no entry was written to disk or database (F73.3)
            Assert.Fail(Pending);
        }
    }
}
