// STORY-143 — Whole-track energy for every measured row (Epic X / SPEC F47, closes gitea-#190).
//
// BDD specification — xUnit (real Postgres via the DatabaseFixture). track_energy is a STORED
// generated column — the facts here prove DDL behavior, not C# writes; the constants mirror
// FfmpegEnergyAnalyzer.MinLufs/MaxLufs (F47.1) and the migration comment cross-references them.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTrackEnergyGeneratedColumn
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Inserts a library.media row with the given integrated_lufs (or NULL) and returns its id.</summary>
    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path, double? integratedLufs)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO library.media (path, format, size_bytes, mtime, state, library_id, integrated_lufs)
              VALUES (@path, 'flac', 1024, now(), 'ready', 1, @integratedLufs)
              RETURNING id",
            new { path, integratedLufs });
    }

    static async Task<double?> TrackEnergyOfAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<double?>(
            "SELECT track_energy FROM library.media WHERE id = @id", new { id });
    }

    static async Task SetIntegratedLufsAsync(DatabaseFixture db, long id, double? integratedLufs)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE library.media SET integrated_lufs = @integratedLufs WHERE id = @id",
            new { id, integratedLufs });
    }

    /// <summary>
    /// Executes the real <c>db/10-enrichment2-migration.sh</c> against the test database by piping
    /// the script file to <c>bash -s</c> inside the compose testdb container via the fixture.
    /// </summary>
    static void RunMigrationScript(DatabaseFixture db)
    {
        var scriptPath = Path.Combine(db.RepoRoot, "db", "10-enrichment2-migration.sh");
        db.RunFileInContainer(scriptPath);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — derivation values
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyDerivesFromIntegratedLoudness(DatabaseFixture db)
    {
        [Fact]
        public async Task AMidRangeLoudnessMapsIntoTheUnitInterval()
        {
            // -21 LUFS is the midpoint of [MinLufs -36, MaxLufs -6]: (-21 + 36) / 30 = 0.5.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/test/energy-mid.flac", integratedLufs: -21.0);

            var energy = await TrackEnergyOfAsync(db, id);

            Assert.NotNull(energy);
            Assert.Equal(0.5, energy.Value, precision: 10);
        }

        [Fact]
        public async Task AHotMasterClampsToOne()
        {
            // 0.0 LUFS is well above MaxLufs (-6): (0 + 36) / 30 = 1.2, clamped down to 1.0.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/test/energy-hot.flac", integratedLufs: 0.0);

            var energy = await TrackEnergyOfAsync(db, id);

            Assert.NotNull(energy);
            Assert.Equal(1.0, energy.Value, precision: 10);
        }

        [Fact]
        public async Task TheScaleMatchesIntroOutroEnergySemantics()
        {
            // The endpoints match FfmpegEnergyAnalyzer.MinLufs/MaxLufs exactly: -36 -> 0.0, -6 -> 1.0
            // — the same [0,1] normalization range as intro/outro energy (F47.3).
            await db.ResetAsync();
            var lowId = await InsertMediaRowAsync(db, "/test/energy-min.flac", integratedLufs: -36.0);
            var highId = await InsertMediaRowAsync(db, "/test/energy-max.flac", integratedLufs: -6.0);

            var low = await TrackEnergyOfAsync(db, lowId);
            var high = await TrackEnergyOfAsync(db, highId);

            Assert.NotNull(low);
            Assert.NotNull(high);
            Assert.Equal(0.0, low.Value, precision: 10);
            Assert.Equal(1.0, high.Value, precision: 10);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — instant backfill + re-derivation
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheCatalogBackfillsInstantly(DatabaseFixture db)
    {
        [Fact]
        public async Task PreExistingMeasuredRowsHaveEnergyImmediatelyAfterMigration()
        {
            // Simulate a pre-Epic-X DB: drop track_energy, seed an already-measured row, then run
            // the migration. Adding a STORED generated column computes it for existing rows within
            // the same DDL statement — no claim loop, no analyzer pass (F47.2).
            await db.ResetAsync();
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("ALTER TABLE library.media DROP COLUMN IF EXISTS track_energy");

            var id = await InsertMediaRowAsync(db, "/test/energy-preexisting.flac", integratedLufs: -21.0);

            RunMigrationScript(db);

            var energy = await TrackEnergyOfAsync(db, id);
            Assert.NotNull(energy);
            Assert.Equal(0.5, energy.Value, precision: 10);
        }

        [Fact]
        public async Task RewritingIntegratedLufsReDerivesTheEnergy()
        {
            // A loudness (re-)enrichment rewrites integrated_lufs directly — no separate write to
            // track_energy is needed; the generated column re-derives on the next read (F47.2).
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/test/energy-rewrite.flac", integratedLufs: -21.0);
            var before = await TrackEnergyOfAsync(db, id);
            Assert.NotNull(before);
            Assert.Equal(0.5, before.Value, precision: 10);

            await SetIntegratedLufsAsync(db, id, -6.0);

            var after = await TrackEnergyOfAsync(db, id);
            Assert.NotNull(after);
            Assert.Equal(1.0, after.Value, precision: 10);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unmeasured / gated rows
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnmeasuredAndGatedRowsStayHonest(DatabaseFixture db)
    {
        [Fact]
        public async Task AnUnmeasuredRowHasNullEnergy()
        {
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/test/energy-unmeasured.flac", integratedLufs: null);

            var energy = await TrackEnergyOfAsync(db, id);

            Assert.Null(energy);
        }

        [Fact]
        public async Task AGatedMeasurementYieldsZeroNotNull()
        {
            // -70 LUFS is FfmpegEnergyAnalyzer.GateFloor exactly; the boundary itself gates to 0.0.
            await db.ResetAsync();
            var id = await InsertMediaRowAsync(db, "/test/energy-gated.flac", integratedLufs: -70.0);

            var energy = await TrackEnergyOfAsync(db, id);

            Assert.NotNull(energy);
            Assert.Equal(0.0, energy.Value, precision: 10);
        }
    }
}
