// STORY-254 — A place to edit how the DJ says it (gh-#284)
//
// SPEC F97.3, F100.3. Pronunciation rules are operator data (F68.5 posture) but today the
// only way to author them is a JSON blob in settings. This is the surface: rows, not a blob.
//
// Two things make it more than a CRUD page:
//
//   1. THE MERGE IS VISIBLE. Rules come from the station setting AND the active persona card,
//      with the persona winning (F97.4). An operator staring at a station rule that is being
//      shadowed by a card rule, with no indication of why it isn't working, is exactly the
//      confusion this surface exists to prevent.
//   2. HIT COUNTS LAND HERE. F100.3 rules that facts go where the operator is already
//      looking rather than into a new panel — so "is my rule firing?" is answered on the row
//      itself.
//
// The API is driven through the production endpoint (WebApplicationFactory), not the
// controller class: a rules list that works in a unit test and 404s in production is the
// failure mode this file guards against.

namespace GenWave.Host.Tests.Specs;

public static class FeaturePronunciationRulesSurface
{
    public static class ScenarioTheListIsFirstClass
    {
        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void The_endpoint_returns_rules_as_rows()
        {
            // GET the real route through WebApplicationFactory<Program>.
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void A_rule_can_be_created()
        {
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void A_rule_can_be_edited()
        {
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void A_rule_can_be_removed()
        {
            Assert.Fail("pending T144");
        }
    }

    public static class ScenarioTheMergedViewShowsWhichSourceWon
    {
        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void Each_row_names_its_source()
        {
            // station | persona — the operator must be able to see where a rule came from.
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void A_shadowed_station_rule_is_marked_as_not_in_effect()
        {
            // The confusion this surface exists to prevent (F97.4).
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void Each_row_carries_its_hit_count()
        {
            Assert.Fail("pending T144");
        }
    }

    public static class ScenarioTheAdminUiRendersIt
    {
        [Fact(Skip = "Pending T145 — see docs/PLAN.md")]
        public static void Rules_render_as_editable_rows_rather_than_a_json_blob()
        {
            Assert.Fail("pending T145");
        }

        [Fact(Skip = "Pending T145 — see docs/PLAN.md")]
        public static void A_shadowed_rule_is_visibly_not_in_effect()
        {
            Assert.Fail("pending T145");
        }
    }

    // -------------------------------------------------------------------------------------
    // ENTRY POINT — the live claim (F68.5): a saved rule affects the very next spoken line.
    // -------------------------------------------------------------------------------------
    public static class ScenarioASavedRuleIsLive
    {
        [Fact(Skip = "Pending T146 — see docs/PLAN.md")]
        public static void The_next_render_after_a_save_reflects_the_new_rule()
        {
            // Save through the real endpoint, then render — with no restart in between.
            Assert.Fail("pending T146");
        }

        [Fact(Skip = "Pending T146 — see docs/PLAN.md")]
        public static void No_process_restart_is_required()
        {
            Assert.Fail("pending T146");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioInvalidRules
    {
        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void An_empty_pattern_is_rejected()
        {
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void Malformed_ipa_is_rejected()
        {
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T144 — see docs/PLAN.md")]
        public static void A_rejected_rule_is_not_persisted()
        {
            // Nothing half-saved: the surface rejects, the store is untouched.
            Assert.Fail("pending T144");
        }

        [Fact(Skip = "Pending T145 — see docs/PLAN.md")]
        public static void The_offending_field_is_highlighted_in_place()
        {
            Assert.Fail("pending T145");
        }
    }
}
