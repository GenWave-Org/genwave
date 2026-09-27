// STORY-484 — The schema journal (gh-#868 · SPEC F211.3–F211.4 · PLAN T591, T592)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration). RED at plan time: every fact is
// [Fact(Skip = Pending)] with a loud body — remove the Skip only in the task that makes it green.
// Each Given comment names the arrange the scenario needs.
// Entry point: the real ./migrate.sh against a throwaway compose db (T591); the Postgres journal
// reader via DatabaseCollection (T592).

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureSchemaJournal
{
    const string PendingScript = "pending: T591 — migrate.sh journal (STORY-484)";
    const string PendingReader = "pending: T592 — ISchemaJournal reader (STORY-484)";

    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrateShOnAPreF211Db
    {
        // Given: a throwaway compose db with no station.schema_migration; GW_VERSION=v5.14.0; ./migrate.sh

        /// <summary>AC1 — station.schema_migration exists with (script pk, applied_at not null, app_version null-able)</summary>
        [Fact(Skip = PendingScript)]
        public void CreatesTheJournal() => Assert.Fail(PendingScript);

        /// <summary>AC2 — one row per db/*-migration.sh, keyed by file name</summary>
        [Fact(Skip = PendingScript)]
        public void OneRowPerScript() => Assert.Fail(PendingScript);

        /// <summary>AC3 — every row's app_version is "v5.14.0"</summary>
        [Fact(Skip = PendingScript)]
        public void StampsTheAppVersion() => Assert.Fail(PendingScript);
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrateShRunsTwice
    {
        // Given: the same db, ./migrate.sh run a second time

        /// <summary>AC4 — the row count is unchanged</summary>
        [Fact(Skip = PendingScript)]
        public void RowCountUnchanged() => Assert.Fail(PendingScript);

        /// <summary>AC4 — applied_at on 49-ad-spot-auto-rerender-marker-migration.sh moved forward</summary>
        [Fact(Skip = PendingScript)]
        public void AppliedAtMovesForward() => Assert.Fail(PendingScript);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshInit(DatabaseFixture db)
    {
        // Given: the fixture's freshly initialised db (db/06 ran)

        /// <summary>AC5 — station.schema_migration exists (gh-#618 fresh-init mirror)</summary>
        [Fact(Skip = PendingScript)]
        public void TableExists() => Assert.Fail($"{PendingScript} {db}");
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAppliedIsTheHighestJournalledNumber(DatabaseFixture db)
    {
        // Given: journal rows for 47-, 48- and 49-*-migration.sh; the reader's GetAppliedAsync

        /// <summary>AC7 — Applied is 49</summary>
        [Fact(Skip = PendingReader)]
        public void AppliedIs49() => Assert.Fail($"{PendingReader} {db}");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioNoGwVersion
    {
        // Given: a throwaway compose db; GW_VERSION unset; ./migrate.sh

        /// <summary>AC8 — app_version is null on every row</summary>
        [Fact(Skip = PendingScript)]
        public void AppVersionIsNull() => Assert.Fail(PendingScript);
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioAFailedScript
    {
        // Given: a temp db/ copy (Support/TempDir) with a 50-*-migration.sh that exits 1; ./migrate.sh

        /// <summary>AC9 — no row for the failed script</summary>
        [Fact(Skip = PendingScript)]
        public void FailedScriptNotJournalled() => Assert.Fail(PendingScript);

        /// <summary>AC9 — every script before it is journalled</summary>
        [Fact(Skip = PendingScript)]
        public void EarlierScriptsJournalled() => Assert.Fail(PendingScript);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnEmptyJournal(DatabaseFixture db)
    {
        // Given: station.schema_migration truncated; GetAppliedAsync

        /// <summary>AC10 — Applied is null</summary>
        [Fact(Skip = PendingReader)]
        public void AppliedIsNull() => Assert.Fail($"{PendingReader} {db}");
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNoJournalTable(DatabaseFixture db)
    {
        // Given: station.schema_migration dropped (pre-F211 db); GetAppliedAsync

        /// <summary>AC10 (STORY-485) — Applied is null, no throw (42P01 swallowed)</summary>
        [Fact(Skip = PendingReader)]
        public void AppliedIsNull() => Assert.Fail($"{PendingReader} {db}");
    }
}
