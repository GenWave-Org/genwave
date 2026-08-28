// gh-#620 — Thinking-capable models return empty copy via /v1/chat/completions: the reasoning control
//
// BDD specification — xUnit. Pure value checks over GenWave.Core.Llm.ReasoningEffort — the ONE
// vocabulary the Llm:ReasoningEffort setting, its validator, and every completions poster share.
// The wire-level facts (the field on the request, the WARN naming a reasoning-only reply) live
// beside their posters: Gh620_ReasoningEffortOnTheWire.cs in Tts.Tests and MediaLibrary.Tests,
// Gh620_ReasoningEffortSetting.cs in Host.Tests.
using GenWave.Core.Llm;

namespace GenWave.Core.Tests.Specs;

public static class FeatureReasoningEffortContract
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the vocabulary
    // ---------------------------------------------------------------------

    public sealed class ScenarioVocabulary
    {
        [Fact]
        public void The_default_is_none()
        {
            Assert.Equal("none", ReasoningEffort.Default);
        }

        [Fact]
        public void Accepted_lists_the_five_values_in_admin_order()
        {
            Assert.Equal(["none", "low", "medium", "high", "omit"], ReasoningEffort.Accepted);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("low")]
        [InlineData("medium")]
        [InlineData("high")]
        [InlineData("omit")]
        [InlineData("NONE")]
        [InlineData(" High ")]
        public void Every_accepted_value_is_valid_case_insensitively(string value)
        {
            Assert.True(ReasoningEffort.IsValid(value));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the wire mapping
    // ---------------------------------------------------------------------

    public sealed class ScenarioToWire
    {
        [Fact]
        public void None_goes_on_the_wire_as_none()
        {
            Assert.Equal("none", ReasoningEffort.ToWire("none"));
        }

        [Fact]
        public void An_effort_level_is_normalised_to_lowercase()
        {
            Assert.Equal("high", ReasoningEffort.ToWire(" HIGH "));
        }

        [Fact]
        public void Omit_means_no_field_at_all()
        {
            Assert.Null(ReasoningEffort.ToWire("omit"));
        }

        [Fact]
        public void Omit_is_recognised_case_insensitively()
        {
            Assert.Null(ReasoningEffort.ToWire("Omit"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — values the validator never lets through (env-sourced garbage)
    // ---------------------------------------------------------------------

    public sealed class ScenarioGarbageFailsSafe
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("maximum")]
        [InlineData("none,low")]
        [InlineData("1")]
        public void An_unrecognised_value_is_not_valid(string? value)
        {
            Assert.False(ReasoningEffort.IsValid(value));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("maximum")]
        public void An_unrecognised_value_falls_back_to_the_default_on_the_wire_never_the_raw_string(string? value)
        {
            // The shipped backend (Ollama) would 400 on an unknown effort string; the safe
            // fallback for a value the settings API never accepted is the posture that fixed #620.
            Assert.Equal(ReasoningEffort.Default, ReasoningEffort.ToWire(value));
        }
    }
}
