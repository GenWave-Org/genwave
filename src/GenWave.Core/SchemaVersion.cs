namespace GenWave.Core;

/// <summary>
/// The schema-migration bar this build was compiled against (SPEC F211.4, STORY-484, PLAN T592,
/// gh-#868). <see cref="Expected"/> is the highest <c>NN</c> among <c>db/NN-*-migration.sh</c> in this
/// repo — a plain constant, bumped by hand in the same PR that adds a new migration script, deliberately
/// NOT computed by scanning the filesystem at runtime: a build whose author forgot the bump must fail
/// loud in CI, not silently self-correct. The architecture fact
/// <c>FeatureSchemaExpectedPin.ScenarioExpectedMatchesDb.ExpectedIsTheHighestMigration</c>
/// (GenWave.Architecture.Tests) reads <c>db/</c> itself and pins this constant equal to the highest file
/// it finds there, so a migration PR that forgets the bump goes red.
/// </summary>
public static class SchemaVersion
{
    /// <summary>The highest <c>NN</c> among <c>db/NN-*-migration.sh</c> this build was compiled against — currently <c>db/49-ad-spot-auto-rerender-marker-migration.sh</c>.</summary>
    public const int Expected = 49;
}
