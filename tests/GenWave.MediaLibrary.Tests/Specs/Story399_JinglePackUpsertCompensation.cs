// STORY-399/401 — JinglePackRepository.UpsertAsync's own station-first-with-compensation ordering
// (SPEC F165.5, PLAN T414 review round 2 finding F5)
//
// UpsertAsync writes station.jingle_pack FIRST, then the real single-schema library_svc
// drop-and-upsert transaction SECOND (see that method's own remarks for why this order, not the
// reverse). If the library write throws, the station row must be compensated back to whatever it
// held before this call — restored, or deleted if there was none — before the original exception is
// rethrown, so the two sides of the db/22 schema boundary can never durably disagree about which
// install is actually live.
//
// Forcing the library write to throw needs no fault injection: db/45's own library.media.jingle_role
// CHECK ('bed', 'sting', 'station_id') already refuses any other value, so an asset carrying
// Role: "bogus" makes AssetUpsertSql throw on its own, behind the ALREADY-committed station row —
// exactly the failure window this fact exists to prove is compensated, not left torn.

using System.Text.Json.Nodes;
using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureJinglePackUpsertStationCompensation
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static JinglePackRepository Repo(DatabaseFixture db) =>
        new(
            new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            new Lazy<NpgsqlDataSource>(() => db.DataSource),
            NullLogger<JinglePackRepository>.Instance);

    static async Task<long> CreateLibraryAsync(DatabaseFixture db, string tag)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into library.library (name) values (@name) returning id", new { name = $"jingle-comp-{tag}" });
    }

    /// <summary>A well-formed asset, distinguished only by <paramref name="title"/> and
    /// <paramref name="role"/> — <c>"bogus"</c> is not a member of db/45's own
    /// <c>library.media.jingle_role</c> CHECK set, so the insert throws once this asset's own turn in
    /// AssetUpsertSql's loop comes up.</summary>
    static JinglePackAssetInput Asset(long libraryId, string title, string role, string path) => new(
        new AuthoredMediaInsert(
            Path: path,
            Format: "wav",
            LibraryId: libraryId,
            SizeBytes: 1024,
            Mtime: DateTime.UtcNow,
            Tags: new AudioTags(Artist: "Compensation Test Pack", Title: title),
            Loudness: new GenWave.Core.Domain.Loudness(IntegratedLufs: -16, TruePeakDbtp: -1, Measurable: true),
            Cue: new CuePoints(CueInSec: 0, CueOutSec: 4),
            Energy: new EnergyPoints(IntroEnergy: 0.5, OutroEnergy: 0.5),
            DurationMs: 4000,
            SampleRate: null,
            Channels: null,
            BitrateKbps: null,
            Kind: ImagingKind.Jingle),
        role);

    // ---------------------------------------------------------------------
    // F5 — a fresh slug's failed upsert leaves NO station row and NO library rows
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshSlugsFailedUpsertLeavesNothingBehind(DatabaseFixture db)
    {
        const string Slug = "compensation-fresh-slug";

        [Fact]
        public async Task TheCallThrowsAndNeitherTheStationRowNorAnyLibraryRowSurvives()
        {
            await db.ResetJinglePackAsync();
            await db.ResetAsync();
            var repo = Repo(db);
            var libraryId = await CreateLibraryAsync(db, "fresh");

            var assets = new[] { Asset(libraryId, "Bogus Role Bed", "bogus", "/authored/jingle-packs/compensation-fresh-slug/bed.wav") };

            await Assert.ThrowsAsync<PostgresException>(() =>
                repo.UpsertAsync(Slug, """{"packName":"Compensation Test"}""", "compensation-fresh-slug-catalog-entry", assets, CancellationToken.None));

            await using var stationConn = await db.StationDataSource.OpenConnectionAsync();
            var stationRowExists = await stationConn.ExecuteScalarAsync<long?>(
                "select id from station.jingle_pack where slug = @slug", new { slug = Slug }) is not null;
            Assert.False(stationRowExists, "a fresh slug's compensation must delete the station row the failed call itself committed");

            await using var libraryConn = await db.DataSource.OpenConnectionAsync();
            var libraryRowCount = await libraryConn.ExecuteScalarAsync<long>(
                "select count(*) from library.media where pack_slug = @slug", new { slug = Slug });
            Assert.Equal(0, libraryRowCount);
        }
    }

    // ---------------------------------------------------------------------
    // F5 — a reinstall's failed upsert restores the PREVIOUS station row untouched
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReinstallsFailedUpsertRestoresThePreviousStationRow(DatabaseFixture db)
    {
        const string Slug = "compensation-reinstall-slug";
        const string GoodPath = "/authored/jingle-packs/compensation-reinstall-slug/bed.wav";

        [Fact]
        public async Task TheStationRowIsRestoredToItsPreviousDefinitionAndTheGoodLibraryRowSurvivesUnchanged()
        {
            await db.ResetJinglePackAsync();
            await db.ResetAsync();
            var repo = Repo(db);
            var libraryId = await CreateLibraryAsync(db, "reinstall");

            const string firstDefinition = """{"packName":"Compensation Test","version":1}""";
            var firstAssets = new[] { Asset(libraryId, "Good Bed", "bed", GoodPath) };
            var firstResult = await repo.UpsertAsync(Slug, firstDefinition, "compensation-reinstall-first", firstAssets, CancellationToken.None);
            var firstMediaId = Assert.IsType<JinglePackUpsertResult.Upserted>(firstResult).MediaIds.Single();

            const string secondDefinition = """{"packName":"Compensation Test","version":2}""";
            var secondAssets = new[] { Asset(libraryId, "Bogus Role Sting", "bogus", "/authored/jingle-packs/compensation-reinstall-slug/sting.wav") };

            await Assert.ThrowsAsync<PostgresException>(() =>
                repo.UpsertAsync(Slug, secondDefinition, "compensation-reinstall-second", secondAssets, CancellationToken.None));

            await using var stationConn = await db.StationDataSource.OpenConnectionAsync();
            var restored = await stationConn.QuerySingleAsync<(string Definition, string ImportedFrom)>(
                "select definition::text as definition, imported_from from station.jingle_pack where slug = @slug", new { slug = Slug });
            Assert.Equal("compensation-reinstall-first", restored.ImportedFrom);
            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(firstDefinition), JsonNode.Parse(restored.Definition)),
                "the station row must be compensated back to the FIRST install's own definition, not the second's");

            await using var libraryConn = await db.DataSource.OpenConnectionAsync();
            var survivingRow = await libraryConn.QuerySingleAsync<(long Id, string Title)>(
                "select id, title from library.media where pack_slug = @slug", new { slug = Slug });
            Assert.Equal(firstMediaId, survivingRow.Id);
            Assert.Equal("Good Bed", survivingRow.Title);
        }
    }
}
