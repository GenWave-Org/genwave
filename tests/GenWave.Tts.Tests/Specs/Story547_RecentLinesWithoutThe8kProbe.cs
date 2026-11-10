// STORY-547 — Recent lines without the 8k probe (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T716)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: LlmCopyWriter.WriteAsync; the prompt is read from the fake LLM's captured request

namespace GenWave.Tts.Tests.Specs;

public static class FeatureRecentLinesWithoutThe8kProbe
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — budget off, block on
    public sealed class ScenarioBudgetOffBlockOn
    {
        // Given a memory budget of Off and a persona with 3 stored utterances
        // When  the on-air prompt is built

        [Fact(Skip = "pending: T716 — see docs/PLAN.md")]
        public void ItContainsTheRecentLinesDoNotReuseTheirPhrasingBlockWith3Lines() => Assert.Fail("pending: T716 — it contains the \"Recent lines, do not reuse their phrasing\" block with 3 lines");
    }

    // AC2 — six at most
    public sealed class ScenarioSixAtMost
    {
        // Given a persona with 9 stored utterances
        // When  the on-air prompt is built

        [Fact(Skip = "pending: T716 — see docs/PLAN.md")]
        public void TheBlockListsThe6Newest() => Assert.Fail("pending: T716 — the block lists the 6 newest");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC3 — no lines, no block
    public sealed class ScenarioNoLinesNoBlock
    {
        // Given a persona with no stored utterances
        // When  the on-air prompt is built

        [Fact(Skip = "pending: T716 — see docs/PLAN.md")]
        public void NoRecentLinesBlockIsPresent() => Assert.Fail("pending: T716 — no recent-lines block is present");
    }

    // AC4 — memory stays behind the budget
    public sealed class ScenarioMemoryStaysBehindTheBudget
    {
        // Given a memory budget of Off and a persona with stored memories
        // When  the on-air prompt is built

        [Fact(Skip = "pending: T716 — see docs/PLAN.md")]
        public void NoWhatYouRememberBlockIsPresent() => Assert.Fail("pending: T716 — no \"What you remember\" block is present");
    }
}
