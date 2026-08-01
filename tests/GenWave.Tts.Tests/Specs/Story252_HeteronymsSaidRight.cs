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

using System.Net;
using System.Text.Json;
using GenWave.Tts.Tests.Fakes;
// GenWave.Tts.PronunciationRule (this file's ambient, unqualified `PronunciationRule` throughout)
// and GenWave.Core.Domain.PronunciationRule are two distinct mirrored types — see the mirror's own
// remarks — so a blanket `using GenWave.Core.Domain;` would silently rebind every existing
// unqualified `PronunciationRule` reference below to the wrong one. Aliasing only the three names
// this new scenario needs keeps the rest of the file's resolution untouched.
using TtsRenderContext = GenWave.Core.Domain.TtsRenderContext;
using SegmentKind = GenWave.Core.Domain.SegmentKind;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

public static class FeatureHeteronymsSaidRight
{
    public static class ScenarioKokoroReceivesBothMarkupForms
    {
        [Fact]
        public static void Sentence_pauses_still_ride_the_text()
        {
            var speech = KokoroSpeechMarkup.Render("One. Two.", PronunciationRuleSet.Empty, pauseSeconds: 0.6);

            Assert.Contains("[pause:0.6s]", speech, StringComparison.Ordinal);
        }

        [Fact]
        public static void A_matched_word_carries_its_phonemes()
        {
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/");
            var speech = KokoroSpeechMarkup.Render(
                "Here is MacLeod.", PronunciationRuleSet.Create([rule]), pauseSeconds: 0);

            Assert.Contains("[MacLeod](/məˈklaʊd/)", speech, StringComparison.Ordinal);
        }
    }

    // T134 (docs/PLAN.md): TtsRenderContext now carries Rules, and KokoroTtsSynthesizer's
    // context-aware overload reads them — proven here one layer below
    // ScenarioKokoroReceivesBothMarkupForms above, which only exercises KokoroSpeechMarkup.Render
    // directly. This drives the REAL adapter (a capturing HttpMessageHandler standing in for
    // Kokoro) so the assertion is on what actually reaches the wire, not on the C# property
    // accessor a `with` expression exercises. No caller resolves a REAL rule set onto a context
    // yet (T137's job) — this test builds the context by hand, the way T137 eventually will.
    public static class ScenarioARuleOnTheContextReachesTheAdapter
    {
        [Fact]
        public static async Task A_rule_on_the_render_context_reaches_the_kokoro_wire()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var requests = new List<string>();
                var handler = new FakeHttpMessageHandler(async (request, ct) =>
                {
                    requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent([1, 2, 3, 4]),
                    };
                });
                var synth = new KokoroTtsSynthesizer(
                    new HttpClient(handler),
                    new TestOptionsMonitor<TtsOptions>(
                        new TtsOptions { CacheRoot = cacheRoot, Format = "wav", SentencePauseSeconds = 0 }));
                var context = new TtsRenderContext("Here is MacLeod.", "af_heart", SegmentKind.LeadIn)
                    with { Rules = [new ContextPronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/")] };

                await synth.SynthesizeAsync(context, CancellationToken.None);

                var input = JsonDocument.Parse(Assert.Single(requests)).RootElement.GetProperty("input").GetString();
                Assert.Contains("[MacLeod](/məˈklaʊd/)", input ?? "", StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    // Composition-ordering guard (T133 review, F3): pronunciation matches and pause insertion
    // points are computed independently against the ORIGINAL text, so neither the pause pass's
    // sentence-boundary regex nor its abbreviation guard ever runs over already-annotated text.
    public static class ScenarioPauseCompositionProtectsAnnotations
    {
        [Fact]
        public static void An_internal_period_inside_ipa_notation_never_gets_a_pause_spliced_into_it()
        {
            // The IPA carries its own syllable-separator ".", and an unrelated real sentence
            // boundary follows later in the text — the annotation must stay intact and the real
            // pause must still land in its own, correct place.
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/ˈmæk. laʊd/");
            var speech = KokoroSpeechMarkup.Render(
                "MacLeod is here. Wind is next.", PronunciationRuleSet.Create([rule]), pauseSeconds: 0.6);

            Assert.Equal("[MacLeod](/ˈmæk. laʊd/) is here. [pause:0.6s] Wind is next.", speech);
        }

        [Fact]
        public static void A_pause_that_would_land_inside_an_annotated_word_lands_right_after_it_instead()
        {
            // The rule's own word ("live.") carries the sentence-ending period — gh-#116's pause
            // for that boundary must survive the annotation, not be lost or spliced mid-token.
            var rule = new PronunciationRule("live.", "live.", "/laɪv/");
            var speech = KokoroSpeechMarkup.Render(
                "We are live. Thanks for listening.", PronunciationRuleSet.Create([rule]), pauseSeconds: 0.6);

            Assert.Equal("We are [live.](/laɪv/) [pause:0.6s] Thanks for listening.", speech);
        }

        [Fact]
        public static void Annotating_inside_a_dotted_abbreviation_never_defeats_its_pause_guard()
        {
            // Wrapping the "m" of "9 a.m." must not shift the abbreviation guard's lookback and
            // wrongly add a pause mid-abbreviation; the real pause after "tonight." must still land.
            var rule = new PronunciationRule("a.m", "m", "/ɛm/");
            var speech = KokoroSpeechMarkup.Render(
                "Doors at 9 a.m. tonight. Bring water", PronunciationRuleSet.Create([rule]), pauseSeconds: 0.6);

            Assert.Equal("Doors at 9 a.[m](/ɛm/). tonight. [pause:0.6s] Bring water", speech);
        }

        [Fact]
        public static void A_pause_tag_already_present_in_the_source_text_is_never_corrupted_or_duplicated()
        {
            // gh-#116 regression (T133 round-3 review, F6/F7): the SOURCE text can already carry a
            // literal "[pause:Ns]" substring — an operator correction's replacement, verbatim,
            // reaching the renderer untouched. Recovering insertion points by diffing
            // InsertSentencePauses' output against its input cannot tell that literal substring
            // apart from a real insertion, corrupting the tag around it; taking the offsets
            // directly (KokoroPauseMarkup.SentencePauseOffsets) finds only the one real sentence
            // boundary — after "there." — and leaves the pre-existing literal tag untouched.
            var speech = KokoroSpeechMarkup.Render(
                "Hi [pause:0.6s] there. More text here.", PronunciationRuleSet.Empty, pauseSeconds: 0.6);

            Assert.Equal("Hi [pause:0.6s] there. [pause:0.6s] More text here.", speech);
        }

        [Fact]
        public static void Two_sentence_boundaries_inside_one_annotated_span_collapse_to_one_pause()
        {
            // F8 REVERSED (T133 round-4 review): a rule matching "one. two." verbatim spans TWO
            // natural sentence boundaries; SnapOutsideAnnotations relocates both to the same
            // position right after the annotation. A [pause:Ns] tag is audible digital silence on
            // the kokoro-fastapi wire — two tags back to back at the SAME seam SUM rather than
            // coexist, so emitting both would double the source's 0.6s gap into 1.2s of dead air
            // at a seam that only ever had one. Coincident relocated offsets collapse to ONE tag,
            // the same "one boundary, one tag" rule KokoroPauseMarkup itself applies to a maximal
            // [.!?…]+ run — an operator who fuses "one. two." into a single annotation has
            // deliberately removed the interior boundary between them. The pause after "three." —
            // a distinct, unrelated seam — still survives untouched.
            var rule = new PronunciationRule("one. two.", "one. two.", "/X/");
            var speech = KokoroSpeechMarkup.Render(
                "Say one. two. three. done.", PronunciationRuleSet.Create([rule]), pauseSeconds: 0.6);

            Assert.Equal("Say [one. two.](/X/) [pause:0.6s] three. [pause:0.6s] done.", speech);
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

        [Fact]
        public static void A_rule_with_no_ipa_is_dropped_rather_than_reaching_the_matcher()
        {
            // T133/T137 review finding (P1): deserialized operator/card JSON that simply omits
            // "ipa" ({"Pattern":"MacLeod","Word":"MacLeod"}) binds a literal null here at runtime
            // despite Ipa's non-nullable declaration — System.Text.Json does not enforce that at
            // bind time. Left uncompiled, this can never reach KokoroSpeechMarkup and dereference it.
            var rule = new PronunciationRule(Pattern: "MacLeod", Word: "MacLeod", Ipa: null!);

            var matches = PronunciationRuleSet.Create([rule]).Match("Here is MacLeod.");

            Assert.Empty(matches);
        }

        [Fact]
        public static void A_rule_with_no_ipa_never_crashes_a_paused_render()
        {
            // The adversarial shape from the T137 review, proven at the render seam. The text
            // carries an INTERIOR sentence boundary ("MacLeod. And now...", not just a trailing one)
            // — without it, KokoroPauseMarkup.SentencePauseOffsets never reports an offset at all
            // (the trailing-sentender rule never tags the LAST boundary in the text), so
            // KokoroSpeechMarkup.ShiftForAnnotations' shift loop short-circuits on an empty offset
            // list and the crashing line is never reached, at ANY pauseSeconds — a spec built on a
            // trailing-only boundary passes on both the fixed AND the unfixed code and pins nothing.
            // With a real interior boundary: at pauseSeconds 0 the malformed rule never gets this far
            // (Compose's pauseOffsets short-circuits to [] before any offset is computed, so the
            // unfixed code renders "[MacLeod]()" happily); at the SHIPPED DEFAULT (0.6) the unfixed
            // code throws a NullReferenceException dereferencing the dropped rule's null Ipa inside
            // KokoroSpeechMarkup.ShiftForAnnotations the moment a real sentence pause needs to be
            // positioned relative to the annotation. PronunciationRuleSet.Create's ipa guard is what
            // stops the rule from ever reaching Match in the first place, so the fixed code never
            // reaches that line at all. Verified by mutation: reverting Create's blank/malformed-ipa
            // guards makes this spec fail with the expected NullReferenceException; restoring them
            // makes it pass again.
            var rule = new PronunciationRule(Pattern: "MacLeod", Word: "MacLeod", Ipa: null!);
            var rules = PronunciationRuleSet.Create([rule]);

            var exception = Record.Exception(() => KokoroSpeechMarkup.Render(
                "Here is MacLeod. And now the news.", rules, pauseSeconds: 0.6));

            Assert.Null(exception);
        }

        [Fact]
        public static void A_rule_whose_ipa_contains_a_close_paren_is_dropped_rather_than_reaching_the_matcher()
        {
            // T133/T137 review finding (P2): kokoro-fastapi's own [word](ipa) markup parser closes
            // the annotation at the FIRST ")" it sees — an ipa carrying one (parenthesized
            // optional-segment notation is legitimate IPA, e.g. an operator importing a card that
            // marks an optional phoneme) would truncate the token early and leak the remainder as
            // spoken text on the wire. Left uncompiled, this rule can never reach the composer.
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/m(ə)ˈklaʊd/");

            var matches = PronunciationRuleSet.Create([rule]).Match("Here is MacLeod.");

            Assert.Empty(matches);
        }

        [Fact]
        public static void A_rule_whose_ipa_contains_a_close_paren_never_corrupts_the_markup_token()
        {
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/m(ə)ˈklaʊd/");
            var rules = PronunciationRuleSet.Create([rule]);

            var speech = KokoroSpeechMarkup.Render("Here is MacLeod.", rules, pauseSeconds: 0);

            // The rule never compiled, so the text renders unannotated — never a truncated
            // "[MacLeod](/m(ə)" token with "ˈklaʊd/)" spilling out as spoken text after it.
            Assert.Equal("Here is MacLeod.", speech);
        }

        [Fact]
        public static void A_rule_whose_ipa_contains_an_open_bracket_is_dropped_rather_than_reaching_the_matcher()
        {
            // T137 review finding (P1): kokoro-fastapi's own [pause:Ns] markup (gh-#116) is honored
            // anywhere it appears in the request text, not only outside a pronunciation annotation's
            // parens — an ipa carrying a "[" (e.g. the "[" half of an imported card's
            // "/mə[pause:600s]klaʊd/") risks splicing a literal digital-silence directive onto the
            // wire the moment this rule's annotation renders. Left uncompiled, this rule can never
            // reach the composer.
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/mə[klaʊd/");

            var matches = PronunciationRuleSet.Create([rule]).Match("Here is MacLeod.");

            Assert.Empty(matches);
        }

        [Fact]
        public static void A_rule_whose_ipa_contains_an_open_bracket_never_corrupts_the_markup_token()
        {
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/mə[klaʊd/");
            var rules = PronunciationRuleSet.Create([rule]);

            var speech = KokoroSpeechMarkup.Render("Here is MacLeod.", rules, pauseSeconds: 0);

            // The rule never compiled, so the text renders unannotated — never a
            // "[MacLeod](/mə[klaʊd/)" token that could seed a literal [pause:Ns] directive on the wire.
            Assert.Equal("Here is MacLeod.", speech);
        }

        [Fact]
        public static void A_rule_whose_ipa_contains_a_close_bracket_is_dropped_rather_than_reaching_the_matcher()
        {
            // T137 review finding (P1), the "]" half of the same risk: an ipa carrying a "]" (e.g.
            // the close of an imported card's "/mə[pause:600s]klaʊd/") is just as capable of closing
            // out a [pause:Ns] directive once spliced onto the wire. Left uncompiled, this rule can
            // never reach the composer.
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/məklaʊd]/");

            var matches = PronunciationRuleSet.Create([rule]).Match("Here is MacLeod.");

            Assert.Empty(matches);
        }

        [Fact]
        public static void A_rule_whose_ipa_contains_a_close_bracket_never_corrupts_the_markup_token()
        {
            var rule = new PronunciationRule("MacLeod", "MacLeod", "/məklaʊd]/");
            var rules = PronunciationRuleSet.Create([rule]);

            var speech = KokoroSpeechMarkup.Render("Here is MacLeod.", rules, pauseSeconds: 0);

            // The rule never compiled, so the text renders unannotated — never a
            // "[MacLeod](/məklaʊd]/)" token that could seed a literal [pause:Ns] directive on the wire.
            Assert.Equal("Here is MacLeod.", speech);
        }
    }

    public static class ScenarioPiperNeverSeesMarkup
    {
        [Fact]
        public static void Pause_markup_is_stripped_before_the_piper_wire()
        {
            var sent = PiperSpeechMarkup.Strip("Hello. [pause:0.6s] World.");

            Assert.DoesNotContain("[pause:", sent, StringComparison.Ordinal);
        }

        [Fact]
        public static void Pronunciation_markup_is_stripped_too()
        {
            Assert.DoesNotContain("](/", PiperSpeechMarkup.Strip("[MacLeod](/məˈklaʊd/)"),
                StringComparison.Ordinal);
        }

        [Fact]
        public static void The_spoken_words_themselves_survive_the_strip()
        {
            // Stripping removes the annotation, never the word it annotated.
            Assert.Equal("MacLeod", PiperSpeechMarkup.Strip("[MacLeod](/məˈklaʊd/)"));
        }
    }

    // T133 review (F1/F2): a nested paren inside the annotation, or a nested/malformed bracket
    // shape, must never demote a real [word](annotation) token to "delete the word, leave the
    // annotation" — and nothing bracket-shaped may survive to the Piper wire regardless of nesting.
    public static class ScenarioPiperGuardHandlesNestingAndSpacing
    {
        [Fact]
        public static void A_nested_paren_inside_the_annotation_never_orphans_the_word()
        {
            var sent = PiperSpeechMarkup.Strip("Now playing [Blue Monday](New Order (1983)) next.");

            Assert.Equal("Now playing Blue Monday next.", sent);
        }

        [Fact]
        public static void Parenthesized_ipa_notation_survives_as_the_bare_word()
        {
            Assert.Equal("MacLeod", PiperSpeechMarkup.Strip("[MacLeod](/mə(k)laʊd/)"));
        }

        [Fact]
        public static void A_nested_bracket_pair_leaves_no_open_bracket_on_the_wire()
        {
            // A single non-recursive pass would leave "[ac]" behind (F2) — the fixpoint loop
            // resolves it fully; no [...]-shaped remnant may survive (F96.3).
            var sent = PiperSpeechMarkup.Strip("[a[b]c]");

            Assert.DoesNotContain('[', sent);
        }

        [Fact]
        public static void A_nested_bracket_pair_leaves_no_close_bracket_on_the_wire()
        {
            var sent = PiperSpeechMarkup.Strip("[a[b]c]");

            Assert.DoesNotContain(']', sent);
        }

        [Fact]
        public static void A_doubly_bracketed_token_leaves_no_open_bracket_on_the_wire()
        {
            var sent = PiperSpeechMarkup.Strip("[[MacLeod]](/x/)");

            Assert.DoesNotContain('[', sent);
        }

        [Fact]
        public static void A_doubly_bracketed_token_leaves_no_close_bracket_on_the_wire()
        {
            var sent = PiperSpeechMarkup.Strip("[[MacLeod]](/x/)");

            Assert.DoesNotContain(']', sent);
        }

        [Fact]
        public static void A_space_before_the_annotation_still_composes_as_one_token()
        {
            // A space between the closing bracket and the annotation must not orphan the
            // annotation, which would otherwise be spoken verbatim as raw notation text.
            Assert.Equal("MacLeod", PiperSpeechMarkup.Strip("[MacLeod] (/x/)"));
        }
    }

    // T133 review round 3 (F5/F9): whitespace tolerance in the annotation match must never let an
    // unrelated parenthetical resurrect a [pause:Ns] directive as spoken content, and a malformed
    // (unbalanced) annotation attempt must never cost the word it was attempting to annotate.
    public static class ScenarioPiperGuardNeverMisclassifiesAToken
    {
        [Fact]
        public static void A_pause_directive_is_never_spoken_just_because_a_parenthetical_follows_it()
        {
            // Before the fix, MarkupTokenRx's whitespace-tolerant annotation group swallowed the
            // trailing "(a classic)" as this token's annotation, which promoted the directive
            // itself — "pause:0.6s" — to a KEPT word: Piper would have spoken it aloud.
            var sent = PiperSpeechMarkup.Strip("Up next [pause:0.6s] (a classic) from 1983.");

            Assert.DoesNotContain("pause:0.6s", sent, StringComparison.Ordinal);
        }

        [Fact]
        public static void An_annotation_across_a_newline_is_never_treated_as_belonging_to_the_token()
        {
            // A newline is not the "at most one non-newline space" the annotation group tolerates
            // — the parenthetical on the next line was never part of this bracket token.
            var sent = PiperSpeechMarkup.Strip("[MacLeod]\n(unrelated)");

            Assert.Contains("(unrelated)", sent, StringComparison.Ordinal);
        }

        [Fact]
        public static void An_unbalanced_annotation_still_keeps_the_word_it_was_annotating()
        {
            // "(/x/" never closes — the annotation group cannot match it — but the attempt (a "("
            // immediately after "]") still proves this was authored as a word, not a bare
            // directive: the word must survive even though the broken annotation can't be stripped.
            var sent = PiperSpeechMarkup.Strip("[MacLeod](/x/");

            Assert.StartsWith("MacLeod", sent, StringComparison.Ordinal);
        }

        [Fact]
        public static void An_unbalanced_annotation_across_a_space_still_keeps_the_word()
        {
            var sent = PiperSpeechMarkup.Strip("[MacLeod] (/x/");

            Assert.StartsWith("MacLeod", sent, StringComparison.Ordinal);
        }
    }

    // T133 round-4 review: MarkupTokenRx's balancing group is the one recursive regex in this
    // assembly with no bound of its own — repeated unclosed annotations force pathological
    // backtracking well past the 250ms every other literal-regex rule set in this project is
    // held to, and Strip loops it up to 64 times on the 24/7 feeder path (gh-#184's exact failure
    // mode: a multi-second stall in production).
    public static class ScenarioPiperGuardNeverStallsOnAdversarialInput
    {
        [Fact]
        public static void A_pathological_run_of_unclosed_annotations_never_hangs_or_throws()
        {
            // Repeated "[a](x" with no closing paren anywhere forces MarkupTokenRx's balancing
            // group into exactly the backtracking blowup the 250ms match timeout exists to bound
            // (mirrors LiteralRegexPosture's own timeout elsewhere in this assembly). F96.4:
            // markup removal is never a render failure, so a timed-out pass must fall through
            // rather than fault the whole render.
            var input = string.Concat(Enumerable.Repeat("[a](x", 4000));

            var exception = Record.Exception(() => PiperSpeechMarkup.Strip(input));

            Assert.Null(exception);
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
        [Fact]
        public static void An_unsupported_form_is_removed_rather_than_failing_the_render()
        {
            // F96.4 — the words still air; a markup form an engine cannot honour is never fatal.
            // piper-tts understands NEITHER pause tags NOR IPA overrides, so any arbitrary
            // bracket-shaped form reaching PiperSpeechMarkup.Strip is unsupported by construction —
            // it is removed, not thrown on, and the surrounding words survive. Removing a token
            // from mid-sentence leaves a doubled space behind ("Hello" + "" + " World." before
            // collapsing) — Strip runs after SpeechText's own whitespace-collapse pass, so it
            // collapses that back down to one space itself rather than pinning the artifact.
            var sent = PiperSpeechMarkup.Strip("Hello [emphasis:strong] World.");

            Assert.Equal("Hello World.", sent);
        }

        [Fact]
        public static void A_rule_matching_nothing_leaves_the_text_byte_identical()
        {
            // T133 is here now — assert at the render level, the fact this spec's name promises: a
            // rule whose pattern isn't present in the text has nothing to annotate, so the composed
            // Kokoro renderer returns the text byte-identical (no pause markup requested either).
            var rules = PronunciationRuleSet.Create([new PronunciationRule("wind", "wind", "/wˈɪnd/")]);
            const string Text = "Nothing here matches.";

            var speech = KokoroSpeechMarkup.Render(Text, rules, pauseSeconds: 0);

            Assert.Equal(Text, speech);
        }
    }
}
