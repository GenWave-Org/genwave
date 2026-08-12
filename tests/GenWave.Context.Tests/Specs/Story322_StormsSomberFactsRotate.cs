// STORY-322 — Storms stay somber, facts rotate (gh-#468 · SPEC F125 · PLAN VQ-g, T271–T272)
//
// BDD specification — xUnit, pending until /build-loop turns them green. Two faults, one
// story: the somber vocabulary has no wind-storm family (a tornado touchdown aired as
// chill-morning color), and the vend path has no per-fact memory at all — BuildContent's
// chosen[0] is deterministic, so the SAME patter fact vends all day and the SAME 4-fact
// segment string every slot. Rotation moves selection to vend time over the airable list.
// One assertion per Fact; happy first; sad segregated. T273's wire acceptance (distinct
// facts audible over 3+ slots on a running stack) is a production check, not here.

namespace GenWave.Context.Tests.Specs;

public static class FeatureStormsSomberFactsRotate
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public static class ScenarioTheWindStormFamilyIsSomber
    {
        [Fact(Skip = "Pending T271 — see docs/PLAN.md")]
        public static void A_tornado_fact_is_filtered()
        {
            // Given a fact containing "tornado" with no casualty words
            // When  the tone gate runs
            // Then  the fact is filtered — the gh-#468 sighting can not recur
            Assert.Fail("pending T271");
        }

        [Fact(Skip = "Pending T271 — see docs/PLAN.md")]
        public static void Hurricane_cyclone_typhoon_and_blizzard_are_filtered_including_plurals()
        {
            Assert.Fail("pending T271");
        }

        [Fact(Skip = "Pending T271 — see docs/PLAN.md")]
        public static void The_match_stays_word_boundary_anchored()
        {
            // "blizzardry" (or any embedding) does not match — the existing posture.
            Assert.Fail("pending T271");
        }
    }

    public static class ScenarioThePatterLaneRotatesThroughTheAirableList
    {
        [Fact(Skip = "Pending T272 — see docs/PLAN.md")]
        public static void Successive_patter_slots_vend_facts_not_yet_aired_today()
        {
            // Given a day with multiple airable facts and successive patter slots
            // Then each slot vends an unaired fact, in list order — chosen[0] is dead
            Assert.Fail("pending T272");
        }

        [Fact(Skip = "Pending T272 — see docs/PLAN.md")]
        public static void ContextContent_carries_the_ordered_airable_list()
        {
            // The provider stops pre-choosing; the pipeline selects at vend time.
            Assert.Fail("pending T272");
        }
    }

    public static class ScenarioTheSegmentLaneRotatesItsWindow
    {
        [Fact(Skip = "Pending T272 — see docs/PLAN.md")]
        public static void Successive_segment_slots_advance_the_window_through_the_list()
        {
            // Given successive segment slots in one day
            // Then the 4-fact window advances rather than repeating the first four
            Assert.Fail("pending T272");
        }
    }

    public static class ScenarioRotationIsObservable
    {
        [Fact(Skip = "Pending T272 — see docs/PLAN.md")]
        public static void The_vend_log_line_names_the_chosen_fact_index_and_the_aired_set_size()
        {
            Assert.Fail("pending T272");
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioAnExhaustedPatterDaySkipsNeverRepeats
    {
        [Fact(Skip = "Pending T272 — see docs/PLAN.md")]
        public static void When_every_airable_fact_has_aired_the_patter_slot_is_skipped()
        {
            // Patter is optional color; a repeat is the exact complaint.
            Assert.Fail("pending T272");
        }
    }

    public static class ScenarioAnExhaustedSegmentDayWraps
    {
        [Fact(Skip = "Pending T272 — see docs/PLAN.md")]
        public static void When_the_window_has_consumed_the_list_it_wraps()
        {
            // A segment repeat hours later beats starving the lane.
            Assert.Fail("pending T272");
        }
    }

    public static class ScenarioARestartForgetsGracefully
    {
        [Fact(Skip = "Pending T272 — see docs/PLAN.md")]
        public static void A_fresh_aired_set_restarts_rotation_from_the_top()
        {
            // In-memory, day-scoped by ruling — durability deliberately not built.
            Assert.Fail("pending T272");
        }
    }
}
