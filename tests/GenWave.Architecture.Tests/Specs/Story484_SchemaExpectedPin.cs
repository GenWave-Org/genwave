// STORY-484 — The schema journal (gh-#868 · SPEC F211.4 · PLAN T592)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureSchemaExpectedPin
{
    const string Pending = "pending: T592 — SchemaVersion.Expected pin (STORY-484)";

    public sealed class ScenarioExpectedMatchesDb
    {
        // Given: the highest NN among db/NN-*-migration.sh in the repo

        /// <summary>AC6 — SchemaVersion.Expected equals it (a migration PR that forgets the bump is red)</summary>
        [Fact(Skip = Pending)]
        public void ExpectedIsTheHighestMigration() => Assert.Fail(Pending);
    }

    public sealed class ScenarioMigrationsAreNotEdited
    {
        // Given: `git diff origin/main -- db/*-migration.sh` on the branch

        /// <summary>AC11 — no existing migration script changed (the journal lives in migrate.sh)</summary>
        [Fact(Skip = Pending)]
        public void NoMigrationScriptChanged() => Assert.Fail(Pending);
    }
}
