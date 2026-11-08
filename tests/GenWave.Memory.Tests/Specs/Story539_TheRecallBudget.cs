// STORY-539 — The recall budget (gh-#293 gh-#920 · SPEC F218 · PLAN T700)
//
// BDD specification — xUnit. AC4/AC5 only; the other ACs live in tests/GenWave.Tts.Tests/Specs/Story539_TheEightKFloor.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Memory.Tests.Specs;

public static class FeatureTheRecallBudget
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC4 — The recall budget
    public sealed class ScenarioTheRecallBudget
    {
        // entry point: IMemoryRecallSource.RecallAsync over the in-memory store
        // Given 5 lore, 10 episodes and 10 life rows, none recalled recently
        // When  the prompt is built

        [Fact(Skip = "pending: T700 — see docs/PLAN.md")]
        public void ItCarries2Lore3EpisodesNewestFirstAnd3LifeRows() => Assert.Fail("pending: T700 — it carries 2 lore, 3 episodes (newest first) and 3 life rows");

        [Fact(Skip = "pending: T700 — see docs/PLAN.md")]
        public void EachRecalledRowSAiredCountHasIncrementedBeforeRender() => Assert.Fail("pending: T700 — each recalled row's aired_count has incremented before render");
    }

    // AC5 — Not twice in two days
    public sealed class ScenarioNotTwiceInTwoDays
    {
        // Given an episode recalled 47 hours ago
        // When  the prompt is built

        [Fact(Skip = "pending: T700 — see docs/PLAN.md")]
        public void ThatEpisodeIsNotRecalled() => Assert.Fail("pending: T700 — that episode is not recalled");
    }
}
