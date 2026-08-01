// STORY-253 — Rules the operator can trust (gh-#284)
//
// SPEC F97.3–F97.6. Three things this pins, each of which has bitten before:
//
//   1. PRECEDENCE FLIPS. Shipped F71.7 has station corrections winning over card corrections
//      on an identical `from`. F97.4 reverses it — the persona wins — and the reversal covers
//      the WHOLE correction family, literal corrections included, because two precedence rules
//      over one merged surface is not a thing operators remember correctly six months on.
//      An operator who needs to override a bad imported rule edits the card, which import
//      already made a local copy of (F90).
//
//   2. OBSERVABILITY MOVES TO INFORMATION. F68.7 specified that a firing correction "logs at
//      debug". Debug does not reach the fleet log store at all, so "is my rule working?" has
//      been unanswerable in the field since it shipped. F97.5 amends it.
//
//   3. RULES RIDE WITH THE REQUEST. A segment can render across a segment boundary, after the
//      ambient on-air persona has already flipped to the incoming DJ — the exact failure
//      F92.2's HandoffContext exists to prevent. Resolved rules therefore travel on the
//      render context, never re-read from an accessor inside the adapter.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureRulesTheOperatorCanTrust
{
    public static class ScenarioTwoSourcesMerge
    {
        [Fact]
        public static void A_station_only_rule_survives_the_merge()
        {
            // Given a station-only rule and an empty card
            var station = PronunciationRuleSet.Create([new PronunciationRule("Reykjavík", "Reykjavík", "/ˈreɪkjaviːk/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(station, PronunciationRuleSet.Empty);

            // Then the station rule still fires
            Assert.Single(merged.Match("Live from Reykjavík tonight."));
        }

        [Fact]
        public static void A_card_only_rule_survives_the_merge()
        {
            // Given a card-only rule and an empty station set
            var card = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/məˈklaʊd/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(PronunciationRuleSet.Empty, card);

            // Then the card rule still fires
            Assert.Single(merged.Match("Say MacLeod now."));
        }

        [Fact]
        public static void Operator_input_is_escaped_before_it_becomes_a_matcher()
        {
            // F68.5 posture carried over (F97.3): a pattern containing a regex metacharacter — "."
            // would match ANY character if compiled unescaped — matches only its literal text.
            var rule = new PronunciationRule("9.5", "9.5", "/naɪn.../");
            var merged = PronunciationRuleSet.Merge(
                PronunciationRuleSet.Create([rule]), PronunciationRuleSet.Empty);

            // When matching text where an unescaped "." would ALSO match "9x5"
            var matches = merged.Match("The score was 9x5 not 9.5.");

            // Then only the literal "9.5" occurrence matches
            Assert.Single(matches);
        }

        [Fact]
        public static void Matching_is_bounded_by_a_timeout()
        {
            // Given a pathological backtracking pattern (Create always Regex.Escapes operator/card
            // text, which defangs catastrophic backtracking by construction — FromRawPattern is a
            // test-only seam that exercises the timeout-and-skip mechanism directly, mirroring
            // SpeechCorrectionSet.FromRawPattern)
            var merged = PronunciationRuleSet.Merge(
                PronunciationRuleSet.FromRawPattern(@"(a+)+$", "/x/"), PronunciationRuleSet.Empty);
            var input = new string('a', 35) + "!";

            // When matching runs against adversarial input
            var exception = Record.Exception(() => merged.Match(input));

            // Then matching aborts at the timeout without an unhandled exception
            Assert.Null(exception);
        }
    }

    public static class ScenarioThePersonaWins
    {
        [Fact]
        public static void The_card_rule_applies_on_an_identical_pattern_and_word()
        {
            // Given a station rule and a card rule for the same (pattern, word)
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/stationIpa/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("MACLEOD", "MACLEOD", "/cardIpa/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(station, card);

            // Then the card's phoneme is the one that fires
            Assert.Equal("/cardIpa/", Assert.Single(merged.Match("Say MacLeod now.")).Rule.Ipa);
        }

        [Fact]
        public static void The_shadowed_station_rule_is_not_also_applied()
        {
            // Given a station rule and a card rule sharing the identical (pattern, word)
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/stationIpa/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/cardIpa/")]);

            // When the two sources merge
            var merged = PronunciationRuleSet.Merge(station, card);

            // Then exactly one rule survives the conflict — not both, in either order
            Assert.Single(merged.Rules);
        }

        [Fact]
        public static void The_card_wins_the_F97_2_heteronym_flagship_case()
        {
            // Executed case from review — the entire reason the Pattern/Word split exists. The
            // station has a blanket rule for the bare word "read"; the card has a MORE SPECIFIC
            // rule scoped to the phrase "have read" that disambiguates the past-tense reading.
            // These are different (Pattern, Word) pairs, so an identity-only merge lets the
            // station's blanket rule claim the span first and the card's rule never fires. Card
            // rules ordered ahead of station rules fixes that: the card's more specific rule now
            // gets first crack at the text, exactly like an operator-authored "specific rule
            // first" ordering would.
            var station = PronunciationRuleSet.Create([new PronunciationRule("read", "read", "/stn/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("have read", "read", "/card/")]);

            var merged = PronunciationRuleSet.Merge(station, card);

            // Then the card's phoneme wins where its more specific pattern also matches
            Assert.Equal("/card/", Assert.Single(merged.Match("I have read it.")).Rule.Ipa);
        }

        [Fact]
        public static void The_flip_covers_literal_corrections_too()
        {
            // ⚠️ This REVERSES shipped F71.7 (SpeechCorrectionProvider.BuildMerged): a station
            // correction and a card correction for the same From
            var station = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod", "station-way")]);
            var cardCorrections = new List<SpeechCorrection> { new("MACLEOD", "card-way") };

            // When the merged set is built
            var merged = SpeechCorrectionProvider.BuildMerged(station, cardCorrections);
            var result = merged.Apply("MacLeod is on air.", out _);

            // Then the card correction wins, not the station one
            Assert.Equal("card-way is on air.", result);
        }
    }

    public static class ScenarioAFiringRuleIsVisibleInTheField
    {
        [Fact(Skip = "Pending T142 — see docs/PLAN.md")]
        public static void The_line_is_emitted_at_information_not_debug()
        {
            // The whole point: Debug never reaches Loki, so Debug is indistinguishable
            // from no logging at all.
            // Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information);
            Assert.Fail("pending T142");
        }

        [Fact(Skip = "Pending T142 — see docs/PLAN.md")]
        public static void The_line_names_the_rule_that_fired()
        {
            Assert.Fail("pending T142");
        }

        [Fact(Skip = "Pending T142 — see docs/PLAN.md")]
        public static void The_line_names_the_speech_kind()
        {
            Assert.Fail("pending T142");
        }

        [Fact(Skip = "Pending T142 — see docs/PLAN.md")]
        public static void That_rules_counter_increments()
        {
            Assert.Fail("pending T142");
        }
    }

    public static class ScenarioRulesRideWithTheRequest
    {
        [Fact(Skip = "Pending T137 — see docs/PLAN.md")]
        public static void A_boundary_crossing_render_uses_the_authoring_personas_rules()
        {
            // Arrange: segment authored for persona A; ambient accessor already answers B.
            // Assert the markup reflects A's rules — the HandoffContext lesson (F92.2).
            Assert.Fail("pending T137");
        }

        [Fact(Skip = "Pending T137 — see docs/PLAN.md")]
        public static void The_adapter_never_reads_an_ambient_persona_accessor()
        {
            // A throwing/never-configured accessor must not affect a render whose rules
            // were already resolved upstream.
            Assert.Fail("pending T137");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioPreviewsAndMalformedData
    {
        [Fact(Skip = "Pending T142 — see docs/PLAN.md")]
        public static void A_preview_never_increments_a_counter()
        {
            // Mirrors the existing F68.7 preview carve-out: previews are operator-explicit
            // and must not pollute on-air observability.
            Assert.Fail("pending T142");
        }

        [Fact(Skip = "Pending T142 — see docs/PLAN.md")]
        public static void A_preview_emits_no_information_line()
        {
            Assert.Fail("pending T142");
        }

        [Fact]
        public static void Malformed_rule_settings_degrade_to_an_empty_set()
        {
            // Given a station rule whose Word does not occur inside its own Pattern, and a card
            // rule with a blank Pattern — both malformed (F97.1), mirroring the existing
            // corrections-parsing degrade-not-throw posture (F68.5)
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "Rutherford", "/x/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("", "", "/y/")]);

            // When the two malformed sources merge
            var merged = PronunciationRuleSet.Merge(station, card);

            // Then no rule compiled from either side
            Assert.Empty(merged.Rules);
        }

        [Fact]
        public static void A_render_continues_unruled_when_the_set_is_empty()
        {
            // Given a merge of two empty rule sets (no station rules, no card rules)
            var merged = PronunciationRuleSet.Merge(PronunciationRuleSet.Empty, PronunciationRuleSet.Empty);

            // When matching runs against ordinary text
            var matches = merged.Match("The show goes on regardless.");

            // Then nothing is annotated — the render proceeds unruled
            Assert.Empty(matches);
        }
    }
}
