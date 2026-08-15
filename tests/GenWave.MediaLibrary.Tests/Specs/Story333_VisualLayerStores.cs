// STORY-333 — The visual layer's stores (db/37, anchored here for the DatabaseFixture;
// the one migration also serves STORY-332/337/339 — SPEC F128–F131, PLAN T290)
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection.
// Specs Skip-pinned until T290 (db/37 + stores) lands.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureVisualLayerStores
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init carries the four tables
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaAvatarIsAOneToOneExtension(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T290 — see docs/PLAN.md")]
        public void PersonaIdIsUniqueAndCascadesWithItsPersona()
        {
            // information_schema: station.persona_avatar.persona_id NOT NULL UNIQUE
            // REFERENCES station.persona(id) ON DELETE CASCADE.
            _ = db;
            Assert.Fail("pending T290");
        }

        [Fact(Skip = "Pending T290 — see docs/PLAN.md")]
        public void SourceIsCheckedToUploadOrCatalog()
        {
            // INSERT with source='weird' must fail the CHECK; 'upload' and 'catalog' pass.
            _ = db;
            Assert.Fail("pending T290");
        }

        [Fact(Skip = "Pending T290 — see docs/PLAN.md")]
        public void TokenIsUniqueAcrossFaces()
        {
            _ = db;
            Assert.Fail("pending T290");
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPackAndIconAndStationTablesExist(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T290 — see docs/PLAN.md")]
        public void AvatarPackItemsAreUniquePerPackAndCascade()
        {
            // (pack_id, name) UNIQUE; deleting the pack deletes its items.
            _ = db;
            Assert.Fail("pending T290");
        }

        [Fact(Skip = "Pending T290 — see docs/PLAN.md")]
        public void IconPackHoldsAJsonbDefinitionKeyedBySlug()
        {
            _ = db;
            Assert.Fail("pending T290");
        }

        [Fact(Skip = "Pending T290 — see docs/PLAN.md")]
        public void StationImageIsStructurallySingleRow()
        {
            // id int PRIMARY KEY DEFAULT 1 CHECK (id = 1): a second row cannot exist.
            _ = db;
            Assert.Fail("pending T290");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — migration discipline
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRerunningTheMigrationIsIdempotent(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T290 — see docs/PLAN.md")]
        public void SecondRunExitsSuccessfullyWithoutErrors()
        {
            _ = db;
            Assert.Fail("pending T290");
        }
    }
}
