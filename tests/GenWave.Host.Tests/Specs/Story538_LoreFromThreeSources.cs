// STORY-538 — Lore from three sources (gh-#293 gh-#920 · SPEC F218 · PLAN T705)
//
// BDD specification — xUnit. AC2–AC4 here; AC1 in tests/GenWave.Core.Tests/Specs/Story538_LoreFromThreeSources.cs; AC5 in tests/GenWave.Memory.Tests/Specs/Story538_PendingLoreIsSilent.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Host.Tests.Specs;

public static class FeatureLoreFromThreeSources
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC2 — I add one
    public sealed class ScenarioIAddOne
    {
        // entry point: WebApplicationFactory<Program> POST api/personas/{id}/memory
        // Given POST api/personas/{id}/memory with kind lore and text
        // When  it succeeds

        [Fact(Skip = "pending: T705 — see docs/PLAN.md")]
        public void ARowKindLoreSourceAuthoredExists() => Assert.Fail("pending: T705 — a row kind lore, source authored exists");
    }

    // AC3 — The LLM drafts five
    public sealed class ScenarioTheLLMDraftsFive
    {
        // Given POST api/personas/{id}/memory/suggest and a fake LLM returning five lines
        // When  it completes

        [Fact(Skip = "pending: T705 — see docs/PLAN.md")]
        public void FiveRowsKindLoreSourceDraftedApprovedAtNullExist() => Assert.Fail("pending: T705 — five rows kind lore, source drafted, approved_at null exist");
    }

    // AC4 — Approve makes it real
    public sealed class ScenarioApproveMakesItReal
    {
        // Given a drafted row
        // When  PATCH sets approved

        [Fact(Skip = "pending: T705 — see docs/PLAN.md")]
        public void ApprovedAtIsSet() => Assert.Fail("pending: T705 — approved_at is set");

        [Fact(Skip = "pending: T705 — see docs/PLAN.md")]
        public void TheRowIsEligibleForRecall() => Assert.Fail("pending: T705 — the row is eligible for recall");
    }
}
