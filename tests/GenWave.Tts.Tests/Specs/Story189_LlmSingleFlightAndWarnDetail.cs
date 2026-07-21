// STORY-189 — LLM calls single-flight with detailed failure logs (gh-#36)
//
// BDD specification — xUnit (SPEC F69.6–F69.7). Pending scaffold; /build-loop (PLAN T33)
// implements and removes Skip.

using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureLlmSingleFlight
{
    private const string Pending = "pending — PLAN T33 (/build-loop)";

    public static class ScenarioSerializedGenerations
    {
        [Fact(Skip = Pending)]
        public static void Concurrent_copy_renders_execute_sequentially()
        {
            // Given two concurrent copy render requests
            // When  their backend calls are traced
            // Then  the generations execute sequentially, never concurrently (F69.6)
            Assert.Fail(Pending);
        }
    }

    public static class SadPathFailureDetail
    {
        [Fact(Skip = Pending)]
        public static void Failure_warning_includes_exception_status_and_context()
        {
            // Given an LLM call failing with a status code or exception
            // When  the warning is logged
            // Then  it includes the exception type/status and the call context (F69.7)
            Assert.Fail(Pending);
        }
    }
}
