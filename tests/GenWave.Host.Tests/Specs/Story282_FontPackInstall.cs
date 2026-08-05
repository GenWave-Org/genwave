// STORY-282 — Install a pack into the library (SPEC F104.5 · PLAN T198/T199)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureFontPackInstall
{
    public sealed class ScenarioInstallIsTransactionalAndHashPinned
    {
        [Fact(Skip = "pending T199 (STORY-282 AC1)")]
        public void ARealInstallPostVerifiesEverySha256AgainstTheIndex() { }

        [Fact(Skip = "pending T199 (STORY-282 AC1)")]
        public void TheUpsertOfPackAndFacesIsOneTransactionWithNoPartialState() { }
    }

    public sealed class ScenarioProvenanceRecordsTheDoor
    {
        [Fact(Skip = "pending T199 (STORY-282 AC2)")]
        public void ImportedFromCarriesTheCatalogSlugAndImportedAtTheTime() { }
    }

    public sealed class ScenarioReinstallUpserts
    {
        [Fact(Skip = "pending T199 (STORY-282 AC3)")]
        public void InstallingTheSameSlugAgainReplacesRatherThanDuplicates() { }
    }

    public sealed class ScenarioRejectingBadInstalls
    {
        [Fact(Skip = "pending T199 (STORY-282 AC4)")]
        public void AnUnknownSlugRefusesWithNothingStored() { }

        [Fact(Skip = "pending T199 (STORY-282 AC4)")]
        public void ADisabledCatalogRefusesWithTheKillSwitchPosture() { }

        [Fact(Skip = "pending T199 (STORY-282 AC4)")]
        public void AHashMismatchRefusesFailClosedWithNothingStored() { }
    }
}
