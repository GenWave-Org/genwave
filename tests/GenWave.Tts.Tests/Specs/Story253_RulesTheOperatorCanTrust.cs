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
        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void A_station_only_rule_survives_the_merge()
        {
            // var merged = PronunciationProvider.BuildMerged(stationRules, cardRules);
            // Assert.Contains(merged, r => r.Pattern == "Reykjavík");
            Assert.Fail("pending T136");
        }

        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void A_card_only_rule_survives_the_merge()
        {
            Assert.Fail("pending T136");
        }

        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void Operator_input_is_escaped_before_it_becomes_a_matcher()
        {
            // F68.5 posture carried over: a pattern containing regex metacharacters matches
            // literally rather than compiling into a pattern of its own.
            Assert.Fail("pending T136");
        }

        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void Matching_is_bounded_by_a_timeout()
        {
            Assert.Fail("pending T136");
        }
    }

    public static class ScenarioThePersonaWins
    {
        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void The_card_rule_applies_on_an_identical_pattern_and_word()
        {
            // Given a station rule and a card rule for the same (pattern, word):
            // Assert.Equal(cardRule.Ipa, merged.Single(r => r.Pattern == "MacLeod").Ipa);
            Assert.Fail("pending T136");
        }

        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void The_shadowed_station_rule_is_not_also_applied()
        {
            // Exactly one rule survives for a conflicting key — not both, in either order.
            Assert.Fail("pending T136");
        }

        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void The_flip_covers_literal_corrections_too()
        {
            // ⚠️ This REVERSES shipped F71.7 (SpeechCorrectionProvider.BuildMerged), whose
            // existing specs assert station-wins and must be updated in the same task.
            Assert.Fail("pending T136");
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

        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void Malformed_rule_settings_degrade_to_an_empty_set()
        {
            // Never throws at DI time — the existing corrections-parsing posture.
            Assert.Fail("pending T136");
        }

        [Fact(Skip = "Pending T136 — see docs/PLAN.md")]
        public static void A_render_continues_unruled_when_the_set_is_empty()
        {
            Assert.Fail("pending T136");
        }
    }
}
