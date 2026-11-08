// STORY-540 — I can see and shape a DJ's memory (gh-#293 gh-#920 · SPEC F218 · PLAN T706)
//
// BDD specification — xUnit. AC1, AC2, AC5 here; AC3/AC4 are Jest: admin-ui/__specs__/persona-memory-section.spec.tsx.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Host.Tests.Specs;

public static class FeatureICanSeeAndShapeADjsMemory
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — List by kind
    public sealed class ScenarioListByKind
    {
        // entry point: WebApplicationFactory<Program> GET api/personas/{id}/memory
        // Given rows of kinds lore, episode and life
        // When  GET api/personas/{id}/memory

        [Fact(Skip = "pending: T706 — see docs/PLAN.md")]
        public void EachRowCarriesKindSourceTextAiredCountPendingAndForgottenFlags() => Assert.Fail("pending: T706 — each row carries kind, source, text, aired count, pending and forgotten flags");
    }

    // AC2 — The actions
    public sealed class ScenarioTheActions
    {
        // Given a listed row
        // When  PATCH text / PATCH approved / PATCH forgotten / DELETE

        [Fact(Skip = "pending: T706 — see docs/PLAN.md")]
        public void TheRowSTextChanges() => Assert.Fail("pending: T706 — the row's text changes");

        [Fact(Skip = "pending: T706 — see docs/PLAN.md")]
        public void ApprovedAtIsSet() => Assert.Fail("pending: T706 — approved_at is set");

        [Fact(Skip = "pending: T706 — see docs/PLAN.md")]
        public void ForgottenAtIsSet() => Assert.Fail("pending: T706 — forgotten_at is set");

        [Fact(Skip = "pending: T706 — see docs/PLAN.md")]
        public void TheRowIsGone() => Assert.Fail("pending: T706 — the row is gone");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — No public surface
    public sealed class ScenarioNoPublicSurface
    {
        // Given the spectator role
        // When  GET api/personas/{id}/memory

        [Fact(Skip = "pending: T706 — see docs/PLAN.md")]
        public void TheResponseIs403() => Assert.Fail("pending: T706 — the response is 403");
    }
}
