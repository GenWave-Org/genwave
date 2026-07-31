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
        // Two rules, same word, different phonemes — the heteronym case (F97.2). The markup
        // renderer (KokoroSpeechMarkup.Render) doesn't exist yet — T133 — so these specs assert
        // the equivalent fact one layer down, at the matcher: it locates the right occurrence of
        // "wind" with the right phonemes, which is exactly what T133's renderer will wrap in
        // [word](/ipa/).
        const string VerbIpa = "/wˈaɪnd/";
        const string NounIpa = "/wˈɪnd/";
        static readonly PronunciationRule WindDownRule = new("wind down", "wind", VerbIpa);
        static readonly PronunciationRule TheWindRule = new("the wind", "wind", NounIpa);
        static readonly PronunciationRuleSet BothWindRules =
            PronunciationRuleSet.Create([WindDownRule, TheWindRule]);
        const string Text = "Wind down and feel the wind.";

        [Fact]
        public static void A_single_word_pattern_defaults_its_word_to_itself()
        {
            var rule = PronunciationRule.Parse(pattern: "MacLeod", word: null, ipa: "/məˈklaʊd/");

            Assert.Equal("MacLeod", rule.Word);
        }

        [Fact]
        public static void The_verb_reading_is_marked_in_its_own_context()
        {
            var matches = BothWindRules.Match(Text);

            Assert.Contains(
                matches, m => Text.Substring(m.Index, m.Length) == "Wind" && m.Rule.Ipa == VerbIpa);
        }

        [Fact]
        public static void The_noun_reading_is_marked_in_its_own_context()
        {
            var matches = BothWindRules.Match(Text);

            Assert.Contains(
                matches, m => Text.Substring(m.Index, m.Length) == "wind" && m.Rule.Ipa == NounIpa);
        }

        [Fact]
        public static void An_unruled_reading_of_a_ruled_word_is_never_guessed_at()
        {
            // Matching is purely by pattern (F97.2): a set carrying only the noun-context rule
            // must never infer the verb reading for the "Wind down" occurrence just because the
            // word is spelled the same — GenWave never infers part of speech. Using
            // PronunciationRuleSet.Empty here would prove nothing (an empty set finds nothing "by
            // construction", per its own doc) — this asserts the real behaviour of a non-empty
            // set against text holding both readings.
            var matches = PronunciationRuleSet.Create([TheWindRule]).Match(Text);

            var match = Assert.Single(matches);
            Assert.Equal("wind", Text.Substring(match.Index, match.Length));
        }
    }

    public static class ScenarioOverlappingRulesResolveByPrecedence
    {
        // Overlapping rules on the same span (F97.3): "wind down"/VERB is authored before the
        // general "wind"/GENERAL fallback. The earlier rule wins the whole overlapping span
        // outright — the later rule's overlapping occurrence never survives to be emitted
        // alongside it. Emitting both is the one option that cannot work: T133's renderer would
        // have to double-annotate one span. This mirrors SpeechCorrectionSet's compose-by-order
        // behaviour even though this type never rewrites text.
        const string VerbIpa = "/wˈaɪnd/";
        const string GeneralIpa = "/wˈɪnd/";
        static readonly PronunciationRule WindDownRule = new("wind down", "wind", VerbIpa);
        static readonly PronunciationRule GeneralWindRule = new("wind", "wind", GeneralIpa);
        static readonly PronunciationRuleSet Rules =
            PronunciationRuleSet.Create([WindDownRule, GeneralWindRule]);
        const string Text = "Wind down and feel the wind.";

        [Fact]
        public static void The_earlier_specific_rule_wins_the_overlapping_span()
        {
            var matches = Rules.Match(Text);

            var atWindDown = Assert.Single(matches, m => m.Index == 0);
            Assert.Equal(VerbIpa, atWindDown.Rule.Ipa);
        }

        [Fact]
        public static void The_later_general_rule_does_not_also_claim_that_same_span()
        {
            var matches = Rules.Match(Text);

            Assert.DoesNotContain(matches, m => m.Index == 0 && m.Rule.Ipa == GeneralIpa);
        }

        [Fact]
        public static void The_general_rule_still_fires_where_no_earlier_rule_claimed_the_span()
        {
            var matches = Rules.Match(Text);

            Assert.Contains(
                matches, m => Text.Substring(m.Index, m.Length) == "wind" && m.Rule.Ipa == GeneralIpa);
        }

        [Fact]
        public static void Matches_come_back_ordered_left_to_right_regardless_of_rule_order()
        {
            var matches = Rules.Match(Text);

            Assert.Equal([0, Text.LastIndexOf("wind", StringComparison.OrdinalIgnoreCase)],
                matches.Select(m => m.Index));
        }
    }

    public static class ScenarioCreateCompilesWhatItCan
    {
        [Fact]
        public static void A_word_omitted_from_deserialized_data_still_defaults_to_the_pattern()
        {
            // Create is the real ingest path (Parse's only caller today is a test); a rule
            // shaped like deserialized {"Pattern":"MacLeod","Ipa":"..."} with no Word must not be
            // silently dropped — F97.1's whole point is that MacLeod needs no surrounding context.
            var rule = new PronunciationRule(Pattern: "MacLeod", Word: null!, Ipa: "/məˈklaʊd/");

            var matches = PronunciationRuleSet.Create([rule]).Match("Here is MacLeod.");

            var match = Assert.Single(matches);
            Assert.Equal("MacLeod", "Here is MacLeod.".Substring(match.Index, match.Length));
        }

        [Fact]
        public static void A_word_not_inside_its_own_pattern_is_dropped_from_matching()
        {
            var malformed = new PronunciationRule("wind down", "gust", "/xxx/");

            var matches = PronunciationRuleSet.Create([malformed]).Match("Wind down now.");

            Assert.Empty(matches);
        }

        [Fact]
        public static void A_dropped_rule_is_still_visible_via_what_compiled()
        {
            // This set stays pure — no logging here (F68.6) — but a caller answering "did my
            // rule compile?" (T142's rule-hit counters, T144's rules API) needs to see the gap
            // rather than have it vanish with no observability at all (F97.5).
            var good = new PronunciationRule("wind down", "wind", "/wˈaɪnd/");
            var malformed = new PronunciationRule("wind down", "gust", "/xxx/");

            var compiled = PronunciationRuleSet.Create([good, malformed]);

            Assert.Equal([good], compiled.Rules);
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

        [Fact]
        public static void A_rule_matching_nothing_leaves_the_text_byte_identical()
        {
            // KokoroSpeechMarkup.Render doesn't exist yet (T133); assert the matcher-level
            // equivalent — a rule whose pattern isn't present reports no matches, so a renderer
            // built on top of this has nothing to annotate and the text passes through untouched.
            var rules = PronunciationRuleSet.Create([new PronunciationRule("wind", "wind", "/wˈɪnd/")]);

            var matches = rules.Match("Nothing here matches.");

            Assert.Empty(matches);
        }
    }
}
