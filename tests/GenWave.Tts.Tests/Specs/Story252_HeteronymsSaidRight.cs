// STORY-252 — Heteronyms said right (gh-#161, gh-#211)
//
// SPEC F96 (engine speech markup) + F97.1/F97.2 (rule shape). kokoro-fastapi honours inline
// IPA — [Worcester](/wˈʊstər/) — alongside the [pause:Ns] markup gh-#116 already uses.
// piper-tts SPEAKS such tokens aloud, which is the entire reason markup is applied inside the
// engine adapter (below the F68 chokepoint and below the fallback router) rather than in copy,
// in a correction's replacement, or anywhere a different engine could receive it.
//
// The {pattern, word, ipa} split is what makes heteronyms expressible at all: `wind` in
// "wind down" and `wind` in "the wind" are one spelling with two pronunciations, so a rule
// keyed on the word alone has nothing to disambiguate on. GenWave does not infer part of speech.
//
// ⚠️ Heteronym parity on the piper-only topology is an open spike (T139), not a promise here.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureHeteronymsSaidRight
{
    public static class ScenarioKokoroReceivesBothMarkupForms
    {
        [Fact(Skip = "Pending T133 — see docs/PLAN.md")]
        public static void Sentence_pauses_still_ride_the_text()
        {
            // var speech = KokoroSpeechMarkup.Render("One. Two.", rules: [], pauseSeconds: 0.6);
            // Assert.Contains("[pause:0.6s]", speech, StringComparison.Ordinal);
            Assert.Fail("pending T133");
        }

        [Fact(Skip = "Pending T133 — see docs/PLAN.md")]
        public static void A_matched_word_carries_its_phonemes()
        {
            // var rule = new PronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/");
            // var speech = KokoroSpeechMarkup.Render("Here is MacLeod.", [rule], pauseSeconds: 0);
            // Assert.Contains("[MacLeod](/məˈklaʊd/)", speech, StringComparison.Ordinal);
            Assert.Fail("pending T133");
        }
    }

    public static class ScenarioThePatternWordSplitDisambiguates
    {
        [Fact(Skip = "Pending T135 — see docs/PLAN.md")]
        public static void A_single_word_pattern_defaults_its_word_to_itself()
        {
            // var rule = PronunciationRule.Parse(pattern: "MacLeod", word: null, ipa: "/məˈklaʊd/");
            // Assert.Equal("MacLeod", rule.Word);
            Assert.Fail("pending T135");
        }

        [Fact(Skip = "Pending T135 — see docs/PLAN.md")]
        public static void The_verb_reading_is_marked_in_its_own_context()
        {
            // Two rules, same word, different phonemes — the heteronym case (F97.2).
            // var speech = KokoroSpeechMarkup.Render("Wind down and feel the wind.", BothWindRules, 0);
            // Assert.Contains("[Wind](/wˈaɪnd/) down", speech, StringComparison.Ordinal);
            Assert.Fail("pending T135");
        }

        [Fact(Skip = "Pending T135 — see docs/PLAN.md")]
        public static void The_noun_reading_is_marked_in_its_own_context()
        {
            // Assert.Contains("the [wind](/wˈɪnd/)", speech, StringComparison.Ordinal);
            Assert.Fail("pending T135");
        }

        [Fact(Skip = "Pending T135 — see docs/PLAN.md")]
        public static void No_part_of_speech_inference_happens_anywhere()
        {
            // An unruled occurrence is never guessed at — matching is purely by pattern.
            // var speech = KokoroSpeechMarkup.Render("The wind blew.", rules: [], pauseSeconds: 0);
            // Assert.DoesNotContain("[", speech, StringComparison.Ordinal);
            Assert.Fail("pending T135");
        }
    }

    public static class ScenarioPiperNeverSeesMarkup
    {
        [Fact(Skip = "Pending T133 — see docs/PLAN.md")]
        public static void Pause_markup_is_stripped_before_the_piper_wire()
        {
            // var sent = PiperSpeechMarkup.Strip("Hello. [pause:0.6s] World.");
            // Assert.DoesNotContain("[pause:", sent, StringComparison.Ordinal);
            Assert.Fail("pending T133");
        }

        [Fact(Skip = "Pending T133 — see docs/PLAN.md")]
        public static void Pronunciation_markup_is_stripped_too()
        {
            // Assert.DoesNotContain("](/", PiperSpeechMarkup.Strip("[MacLeod](/məˈklaʊd/)"),
            //     StringComparison.Ordinal);
            Assert.Fail("pending T133");
        }

        [Fact(Skip = "Pending T133 — see docs/PLAN.md")]
        public static void The_spoken_words_themselves_survive_the_strip()
        {
            // Stripping removes the annotation, never the word it annotated.
            // Assert.Equal("MacLeod", PiperSpeechMarkup.Strip("[MacLeod](/məˈklaʊd/)"));
            Assert.Fail("pending T133");
        }
    }

    // -------------------------------------------------------------------------------------
    // ENTRY POINT — the production render path (F96.1). A unit-seam spec cannot prove the
    // markup is applied BELOW the chokepoint and the router; only a real render can.
    // -------------------------------------------------------------------------------------
    public static class ScenarioARealRenderCarriesTheMarkup
    {
        [Fact(Skip = "Pending T138 — see docs/PLAN.md")]
        public static void The_kokoro_request_body_carries_the_phonemes()
        {
            // Drive TtsSegmentSource through the composed production graph with a capturing
            // Kokoro handler; assert on what actually went out on the wire.
            Assert.Fail("pending T138");
        }

        [Fact(Skip = "Pending T138 — see docs/PLAN.md")]
        public static void The_same_copy_routed_to_piper_carries_none()
        {
            Assert.Fail("pending T138");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioUnsupportedOrUnmatchedMarkup
    {
        [Fact(Skip = "Pending T133 — see docs/PLAN.md")]
        public static void An_unsupported_form_is_removed_rather_than_failing_the_render()
        {
            // F96.4 — the words still air; a markup form an engine cannot honour is never fatal.
            Assert.Fail("pending T133");
        }

        [Fact(Skip = "Pending T135 — see docs/PLAN.md")]
        public static void A_rule_matching_nothing_leaves_the_text_byte_identical()
        {
            // var text = "Nothing here matches.";
            // Assert.Equal(text, KokoroSpeechMarkup.Render(text, [WindRule], pauseSeconds: 0));
            Assert.Fail("pending T135");
        }
    }
}
