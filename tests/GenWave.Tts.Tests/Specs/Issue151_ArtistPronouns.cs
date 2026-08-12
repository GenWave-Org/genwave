// gh-#151 — llm: patter must use they/their for artists — never infer gender from a name.
//
// BDD specification — xUnit. Observed live on the demo box: the DJ said "it's off HIS
// self-titled EP" about an artist whose gender no metadata ever stated — inferred from a French
// first name. The system scaffold now pins the rule: they/them/their unless the provided
// metadata explicitly states pronouns; a name is never evidence of gender.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureArtistPronouns
{
    public static class ScenarioPronounRuleRidesEverySystemPrompt
    {
        [Fact]
        public static void The_persona_less_prompt_pins_they_them_their_for_artists()
        {
            // Given/When the persona-less system prompt is built
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null, maxCopyChars: 450);

            // Then the pronoun rule is present
            Assert.Contains("they/them/their", prompt);
        }

        [Fact]
        public static void The_persona_less_prompt_forbids_inferring_gender_from_a_name()
        {
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null, maxCopyChars: 450);

            Assert.Contains("never infer gender from a name", prompt);
        }

        [Fact]
        public static void A_persona_voiced_prompt_carries_the_same_rule()
        {
            // Given a system prompt with an active persona section appended
            var prompt = LlmPromptBuilder.BuildSystemPrompt("Style: bubbly, energetic, expressive", maxCopyChars: 450);

            // Then the pronoun rule rides along unchanged — persona or not
            Assert.Contains("they/them/their", prompt);
            Assert.Contains("never infer gender from a name", prompt);
        }
    }
}
