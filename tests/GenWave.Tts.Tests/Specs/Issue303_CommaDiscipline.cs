// gh-#303 — "Commas are killing me!": the unnatural pauses in on-air TTS trace back to the LLM
// writing grammatically correct clause-heavy copy. Both engines honor every comma with a stumble
// the ear reads as hesitation, and gh-#292's comma-before-vocative ("hats, folks") is the same
// fault at its most audible.
//
// BDD specification — xUnit. The fix is prompt-side, and its shape matters as much as its
// presence:
//   1. The ban rides in the SHARED scaffold body, so persona-voiced and persona-less copy are
//      held to it equally.
//   2. It carries TWO escape hatches, not one. A real clause break becomes a sentence (gh-#116
//      then renders that boundary as true 0.6s silence on the Kokoro path); a run-together phrase
//      simply loses the comma. Collapsing these into "always split" would trade a 0.2s stumble
//      for a 0.6s gap — worse for exactly the vocative case gh-#292 is about.
//   3. The one-or-two-sentence cap survives: "start a new sentence" must not read as license to
//      write more copy, which would feed gh-#277's over-MaxCopyChars misses.
//   4. The rule states itself without commas, because prompt text is style the model imitates.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureCommaDiscipline
{
    const string CommaRule =
        "Keep each sentence short. Do not use commas. A comma makes the voice stumble mid-line. " +
        "When two ideas need separating end the sentence and start a new one. " +
        "When the words should run together leave the comma out entirely.";

    const string PersonaSection = "Soul: a washed-up 90s radio jock chasing one more big break.";

    public static class ScenarioTheBanRidesInTheSharedScaffold
    {
        [Fact]
        public static void The_personaless_prompt_carries_the_comma_rule()
        {
            // Given/When the neutral system prompt is built
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null);

            // Then the rule is present
            Assert.Contains(CommaRule, prompt, StringComparison.Ordinal);
        }

        [Fact]
        public static void The_persona_voiced_prompt_carries_the_same_rule()
        {
            var prompt = LlmPromptBuilder.BuildSystemPrompt(PersonaSection);

            Assert.Contains(CommaRule, prompt, StringComparison.Ordinal);
        }

        [Fact]
        public static void The_one_or_two_sentence_cap_survives_alongside_it()
        {
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null);

            Assert.Contains("exactly one or two sentences", prompt, StringComparison.Ordinal);
        }
    }

    public static class ScenarioBothEscapeHatchesStay
    {
        [Fact]
        public static void A_real_clause_break_is_sent_to_a_new_sentence()
        {
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null);

            Assert.Contains(
                "When two ideas need separating end the sentence and start a new one",
                prompt, StringComparison.Ordinal);
        }

        [Fact]
        public static void A_run_together_phrase_just_loses_the_comma()
        {
            // The gh-#292 vocative case: "take off your hats folks" must NOT become two sentences
            var prompt = LlmPromptBuilder.BuildSystemPrompt(personaSection: null);

            Assert.Contains(
                "When the words should run together leave the comma out entirely",
                prompt, StringComparison.Ordinal);
        }
    }

    public static class ScenarioTheRuleObeysItself
    {
        [Fact]
        public static void The_comma_rule_contains_no_commas()
        {
            // Prompt text is style the model imitates — a rule against commas that leans on them
            // argues both ways. This locks the intent against a well-meaning future edit.
            Assert.DoesNotContain(',', CommaRule);
        }
    }
}
