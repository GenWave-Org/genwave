// STORY-541 — Pick the model by how varied it sounds (gh-#293 gh-#920 · SPEC F218 · PLAN T695)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files; see tests/GenWave.Memory.Tests/Specs/Story541_PickTheModelByHowVariedItSounds_Bench.cs for the map.

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePickTheModelByHowVariedItSoundsPrompt
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC2 — the real prompt
    // entry point: the `dotnet run --project tools/PatterBench` process (prompt it posts) vs LlmPromptBuilder
    public sealed class ScenarioTheRealPrompt
    {
        // Given a persona card and the fixture track list
        // When  a break prompt is built

        [Fact(Skip = "pending: T695 — see docs/PLAN.md")]
        public void ItIsByteEqualToLlmPromptBuilderSOutputForTheSameInputs() => Assert.Fail("pending: T695 — it is byte-equal to LlmPromptBuilder's output for the same inputs");
    }
}
