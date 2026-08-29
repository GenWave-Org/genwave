// STORY-373 — I can install and tune Deep Cuts (SPEC F152.5–F152.7 · PLAN T362/T363)
//
// BDD specification — xUnit. PENDING until T362 (AC1/AC2/AC6/AC7) and T363 (AC4/AC5). Entry-point
// discipline: every fact drives the REAL production binary (WebApplicationFactory<Program>, the
// Story345/Story366 factory idiom over the ephemeral Postgres — Support/EphemeralStationDatabase) —
// PUT/GET /api/shows/{id}, GET /api/shows/{id}/rotation-pool, POST /api/shows/{slug}/import, and the
// break-prompt byte-identical pin (AC6) driven the T335/T352 way: the real prompt-building path
// against a scripted completions stub (Support/LlmCompletionsStub.cs — see
// Story364_TheGateRulesOnTheWire.cs's own arc shape). AC3 (the Shows page renders the rule/pool/relax
// card) is a Jest todo elsewhere — not this project. AC8 (the genwave-catalog repo's lint CI) lives in
// that repo, not this one.
namespace GenWave.Host.Tests.Specs;

public static class FeatureInstallAndTuneDeepCuts
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — install the rule, read the pool, keep the framing plain
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheEditorSavesTheRule
    {
        // Given a show, When PUT /api/shows/{id} carries envelope.rotation {maxPlays: 1}.
        [Fact(Skip = "pending T362 (STORY-373 AC1)")]
        public void StationShowEnvelopeHoldsIt() => Assert.Fail("pending T362");

        [Fact(Skip = "pending T362 (STORY-373 AC1)")]
        public void TheGetEchoesIt() => Assert.Fail("pending T362");
    }

    public sealed class ScenarioTheLivePoolSize
    {
        // Given a show with MaxPlays 0 and 6 never-aired playable rows, When GET /api/shows/{id}/rotation-pool.
        [Fact(Skip = "pending T362 (STORY-373 AC2)")]
        public void TheEligibleCountIsSix() => Assert.Fail("pending T362");

        [Fact(Skip = "pending T362 (STORY-373 AC2)")]
        public void TheSinceFieldIsAnEpoch() => Assert.Fail("pending T362");
    }

    public sealed class ScenarioManifestOneOneImportsTheRule
    {
        // Given a catalog show manifest with envelope.rotation {maxPlays: 0}, When POST /api/shows/{slug}/import runs.
        [Fact(Skip = "pending T363 (STORY-373 AC4)")]
        public void TheInstalledShowsEnvelopeCarriesTheRule() => Assert.Fail("pending T363");
    }

    public sealed class ScenarioOlderManifestsStillImport
    {
        // Given a 1.0 manifest with no envelope, When it is imported.
        [Fact(Skip = "pending T363 (STORY-373 AC5)")]
        public void TheShowInstallsWithANullRotationRule() => Assert.Fail("pending T363");
    }

    public sealed class ScenarioTheFramingIsTheFlavorLineOnly
    {
        // Given a Deep Cuts block on air, When a break's prompt is built.
        [Fact(Skip = "pending T362 (STORY-373 AC6)")]
        public void ThePromptIsByteIdenticalToAPlainShowsPrompt() => Assert.Fail("pending T362");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — bad rules refuse
    // ---------------------------------------------------------------------

    public sealed class ScenarioValidationRejectsUnboundedOrInvalidRules
    {
        // Given PUT with {maxPlays: -1}, {notAiredWithinDays: 0}, or {} (no bound), When saved.
        [Fact(Skip = "pending T362 (STORY-373 AC7)")]
        public void TheNegativeMaxPlaysIsFourHundredNamingTheField() => Assert.Fail("pending T362");

        [Fact(Skip = "pending T362 (STORY-373 AC7)")]
        public void TheZeroNotAiredWithinDaysIsFourHundredNamingTheField() => Assert.Fail("pending T362");

        [Fact(Skip = "pending T362 (STORY-373 AC7)")]
        public void TheUnboundedEmptyRuleIsFourHundredNamingTheField() => Assert.Fail("pending T362");
    }
}
