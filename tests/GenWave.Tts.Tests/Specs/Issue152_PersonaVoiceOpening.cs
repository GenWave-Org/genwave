// gh-#152 — llm: "personality-neutral" boilerplate contradicts persona Style in the same prompt.
//
// BDD specification — xUnit. The old system prompt opened "You are a personality-neutral radio
// DJ..." and then appended a persona section ending in "Style: bubbly, energetic, expressive" —
// the two cancelled each other. The neutral framing now applies ONLY when there is no persona
// section; with one, the opening line directs the model to write in the persona's voice instead.
// (The issue's Admin-UI half — exposing the prompt — is out of scope here and stays open.)

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePersonaVoiceOpening
{
    const string PersonaSection = "Style: bubbly, energetic, expressive";

    public static class ScenarioNoPersonaKeepsTheNeutralOpening
    {
        [Fact]
        public static void The_persona_less_prompt_opens_personality_neutral()
        {
            // Given/When the persona-less system prompt is built
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null);

            // Then the neutral framing is present
            Assert.Contains("personality-neutral", prompt);
        }

        [Fact]
        public static void The_persona_less_prompt_never_points_at_a_persona_below()
        {
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null);

            // No persona section exists, so nothing may direct the model at one
            Assert.DoesNotContain("voice of the persona described below", prompt);
        }
    }

    public static class ScenarioPersonaSwapsTheOpeningForItsOwnVoice
    {
        [Fact]
        public static void A_persona_voiced_prompt_drops_the_neutral_boilerplate()
        {
            // Given a system prompt with an active persona section appended
            var prompt = LlmPromptBuilder.BuildSystemPrompt(PersonaSection);

            // Then the contradiction is gone — neutral framing never rides with a Style line
            Assert.DoesNotContain("personality-neutral", prompt);
        }

        [Fact]
        public static void A_persona_voiced_prompt_directs_the_model_at_the_personas_voice()
        {
            var prompt = LlmPromptBuilder.BuildSystemPrompt(PersonaSection);

            Assert.Contains("write every word in the voice of the persona described below", prompt);
            Assert.Contains(PersonaSection, prompt);
        }
    }
}
