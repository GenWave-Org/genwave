// STORY-331 — The shelf gains the visual kinds (SPEC F128.1/.2, F130.6 · PLAN T292)
//
// BDD specification — xUnit. App-side kind admission only: AC4 (catalog CI rules +
// the likeness attestation) is genwave-catalog CI acceptance, not app xUnit (the
// STORY-314/316 precedent). Specs Skip-pinned until T292 lands. T292 also RECORDS
// the ordering finding (does the SHIPPED validator tolerate persona assets[]?) —
// a finding, not a fact; it gates T311's catalog merges.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheShelfGainsTheVisualKinds
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the two new kinds and the persona face asset
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnAvatarPackEntryIsAdmitted
    {
        [Fact(Skip = "Pending T292 — see docs/PLAN.md")]
        public void TheIndexEntrySurvivesValidationWithItsKind()
        {
            // Index carrying kind:"avatar" (manifest+meta+assets[]) validates; the
            // entry lists in GET /api/catalog/index with kind == "avatar".
            Assert.Fail("pending T292");
        }

        [Fact(Skip = "Pending T292 — see docs/PLAN.md")]
        public void TheDetailProjectionCarriesTheItemNames()
        {
            Assert.Fail("pending T292");
        }
    }

    public sealed class ScenarioAnIconPackEntryIsAdmitted
    {
        [Fact(Skip = "Pending T292 — see docs/PLAN.md")]
        public void TheIndexEntrySurvivesValidationWithItsKind()
        {
            Assert.Fail("pending T292");
        }
    }

    public sealed class ScenarioAPersonaEntryMayCarryExactlyOneFace
    {
        [Fact(Skip = "Pending T292 — see docs/PLAN.md")]
        public void OneAvatarAssetValidatesAndProjectsOnTheDetail()
        {
            // persona entry + assets:[ "<slug>.avatar.png" ] → valid; detail exposes it.
            Assert.Fail("pending T292");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — skip rules and the one-face rule
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownKindsStillSkipNotReject
    {
        [Fact(Skip = "Pending T292 — see docs/PLAN.md")]
        public void AFutureKindEntryIsSkippedAndKnownKindsStillList()
        {
            // F103.4 held: kind:"hologram" skips; personas/themes/fonts/shows/avatar/icon list.
            Assert.Fail("pending T292");
        }
    }

    public sealed class ScenarioAPersonaEntryWithTwoAssetsRejects
    {
        [Fact(Skip = "Pending T292 — see docs/PLAN.md")]
        public void TheRejectionNamesTheOneFaceRule()
        {
            Assert.Fail("pending T292");
        }
    }
}
