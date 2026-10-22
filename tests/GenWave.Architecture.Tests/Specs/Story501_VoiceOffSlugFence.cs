// STORY-501 — Nothing ships, and no slug in code (gh-#614 · SPEC F214.16, F214.17 · PLAN T619)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.
// Law L12 (T619). AC3–AC5 live in GenWave.VoiceOff.Tests.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureNothingShipsAndNoSlugInCode
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the slug fence holds
    public sealed class ScenarioTheSlugFenceHolds
    {
        // Given every engines/*.json slug
        // When  tools/VoiceOff/**/*.cs and page.html are scanned (engines/, shims/, gauntlet/, RESULTS.md excluded)

        [Fact(Skip = "pending: T619 — see docs/PLAN.md")]
        public void NoSlugAppearsAsAWholeWord() => Assert.Fail("pending: T619 — no slug appears as a whole word");
    }

    // AC2 — no image builds the tool
    public sealed class ScenarioNoImageBuildsTheTool
    {
        // Given every src/**/Dockerfile and compose*.yaml
        // When  they are scanned

        [Fact(Skip = "pending: T619 — see docs/PLAN.md")]
        public void NoneMentionsVoiceOff() => Assert.Fail("pending: T619 — none mentions VoiceOff");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — the fence bites
    public sealed class ScenarioTheFenceBites
    {
        // Given a .cs file under tools/VoiceOff containing a slug from engines/*.json
        // When  the law runs

        [Fact(Skip = "pending: T619 — see docs/PLAN.md")]
        public void ItFailsNamingTheFileAndSlug() => Assert.Fail("pending: T619 — it fails naming the file and slug");
    }
}
