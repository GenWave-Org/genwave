// gh-#188 — llm: patter confabulated artist facts and parroted quirk example values.
//
// BDD specification — xUnit. Two prompt-side guardrails, both observed failing live on the demo
// box (Booth log, 2026-07-28):
//   1. The system scaffold's open embellishment license ("genuine knowledge") produced confident
//      fabrication from a small local model — an artist renamed on air (LaBarcaDeSua spoken as
//      "Barcarola"), invented origins ("rural Cuba", "a field day in Brooklyn's indie scene").
//      The license is now guarded: era/genre color yes, unprovided specifics no, names/titles
//      never altered.
//   2. Quirk inline examples exert gravitational pull — The Archivist's "invented catalog number:
//      'item four-seven-one-two'" aired that literal number break after break. A guidance line now
//      rides directly under the Quirks line, and ONLY when quirks are shown.

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePromptGuardrails
{
    static Persona BuildPersona() => new(1, "DJ Nova", "", "", "", DateTime.UtcNow, DateTime.UtcNow);

    static PersonaCard BuildCard(IReadOnlyList<string> quirks) =>
        new(
            SchemaVersion: 1,
            Name: "DJ Nova",
            Tagline: "",
            Soul: "A washed-up 90s radio jock chasing one more big break.",
            Quirks: quirks,
            Voice: new VoiceSpec(Engine: "", VoiceId: "", Pace: 1.0, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: []);

    public static class ScenarioEmbellishmentIsGuarded
    {
        [Fact]
        public static void The_scaffold_forbids_unprovided_facts_and_name_alterations()
        {
            // Given/When the persona-less system prompt is built
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null, maxCopyChars: 450);

            // Then the guarded license is present
            Assert.Contains("never state specific facts about the artist or track", prompt);
            Assert.Contains("never alter the artist's name or the track's title", prompt);
        }

        [Fact]
        public static void The_open_ended_genuine_knowledge_license_is_gone()
        {
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null, maxCopyChars: 450);

            // The pre-gh-#188 sentence invited fabrication — it must not resurface
            Assert.DoesNotContain("genuine knowledge", prompt);
        }
    }

    public static class ScenarioQuirkExamplesAreStyleOnly
    {
        [Fact]
        public static void A_prompt_that_shows_quirks_carries_the_example_guidance_beneath_them()
        {
            // Given a persona whose card carries quirks
            var section = LlmPromptBuilder.BuildPersonaSection(
                BuildPersona(), BuildCard(["Assigns every song an invented catalog number: 'item four-seven-one-two'"]));

            // Then the guidance rides directly under the Quirks line
            Assert.NotNull(section);
            var lines = section.Split('\n');
            var quirksIndex = Array.FindIndex(lines, line => line.StartsWith("Quirks:", StringComparison.Ordinal));
            Assert.True(quirksIndex >= 0, "expected a Quirks line");
            Assert.Equal(LlmPromptBuilder.QuirkExampleGuidance, lines[quirksIndex + 1]);
        }

        [Fact]
        public static void A_quirkless_prompt_stays_byte_identical_to_its_pre_guardrail_shape()
        {
            // Given a persona with no quirks at all
            var section = LlmPromptBuilder.BuildPersonaSection(BuildPersona(), BuildCard([]));

            // Then no guidance line appears — nothing to guard, nothing added
            Assert.NotNull(section);
            Assert.DoesNotContain(LlmPromptBuilder.QuirkExampleGuidance, section);
        }
    }
}
