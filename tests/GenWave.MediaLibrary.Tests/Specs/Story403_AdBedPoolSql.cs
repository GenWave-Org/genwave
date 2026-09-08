// STORY-403 — the ad bed pool query, structurally + against real Postgres (SPEC F168.1 · PLAN T416)
//
// Pure text assertion first (the Story402_AdSpotStampVoicePlanSql precedent, this same directory):
// AdBedPoolRepository.PoolSql is the ONE place the predicate lives (ListReadyBedIdsAsync references
// it directly, never a second inline copy), so a mutation weakening any single term has nowhere else
// to hide — each term pinned SEPARATELY, so a mutation to one term fails exactly one fact, not the
// whole set. A real-Postgres fact follows, installing a genuine bed/bed/sting pack through the SAME
// production path T414's own install facts use (JinglePackRepository.UpsertAsync) and proving the
// pool holds only the two bed-role ids, in id order.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAdBedPoolSql
{
    public sealed class ScenarioEveryPredicateTermIsPartOfTheSingleSelectStatement
    {
        // Given AdBedPoolRepository's own pool query text, When each term is inspected directly (one
        // fact per term, so a mutation dropping any single one fails exactly one fact).
        [Fact]
        public void ItOnlyEverSelectsJingleRoleBedRows() =>
            Assert.Contains("jingle_role = 'bed'", AdBedPoolRepository.PoolSql, StringComparison.Ordinal);

        [Fact]
        public void ItOnlyEverSelectsReadyRows() =>
            Assert.Contains("state = 'ready'", AdBedPoolRepository.PoolSql, StringComparison.Ordinal);

        [Fact]
        public void ItExcludesUnavailableRows() =>
            Assert.Contains("unavailable_since is null", AdBedPoolRepository.PoolSql, StringComparison.Ordinal);

        [Fact]
        public void ItIsScopedToTheGivenLibrary() =>
            Assert.Contains("library_id = @libraryId", AdBedPoolRepository.PoolSql, StringComparison.Ordinal);

        [Fact]
        public void ItOrdersDeterministicallyById() =>
            Assert.Contains("order by id", AdBedPoolRepository.PoolSql, StringComparison.Ordinal);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAMixedInstallReturnsOnlyTheBedRoleRowsInOrder(DatabaseFixture db)
    {
        const string Slug = "bed-pool-sql-mixed-pack";

        static JinglePackRepository PackRepo(DatabaseFixture db) =>
            new(
                new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
                new Lazy<NpgsqlDataSource>(() => db.DataSource),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<JinglePackRepository>.Instance);

        static JinglePackAssetInput Asset(long libraryId, string title, string role) => new(
            new AuthoredMediaInsert(
                Path: $"/authored/jingle-packs/{Slug}/{title}.wav",
                Format: "wav",
                LibraryId: libraryId,
                SizeBytes: 1024,
                Mtime: DateTime.UtcNow,
                Tags: new AudioTags(Artist: "Bed Pool SQL Test Pack", Title: title),
                Loudness: new GenWave.Core.Domain.Loudness(IntegratedLufs: -16, TruePeakDbtp: -1, Measurable: true),
                Cue: new CuePoints(CueInSec: 0, CueOutSec: 4),
                Energy: new EnergyPoints(IntroEnergy: 0.5, OutroEnergy: 0.5),
                DurationMs: 4000,
                SampleRate: null,
                Channels: null,
                BitrateKbps: null,
                Kind: ImagingKind.Jingle),
            role);

        [Fact]
        public async Task OnlyTheBedRoleRowsComeBackOrderedById()
        {
            // Given an installed pack carrying two AVAILABLE bed rows, one bed row that will be
            // marked unavailable_since, one bed row that will be moved off state='ready', and one
            // sting row (wrong role entirely)...
            await db.ResetJinglePackAsync();
            await db.ResetAsync();
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var libraryId = await conn.ExecuteScalarAsync<long>(
                "insert into library.library (name) values (@name) returning id",
                new { name = $"bed-pool-sql-{Guid.NewGuid():N}" });

            var packRepo = PackRepo(db);
            var assets = new[]
            {
                Asset(libraryId, "First Bed", "bed"),
                Asset(libraryId, "Second Bed", "bed"),
                Asset(libraryId, "Unavailable Bed", "bed"),
                Asset(libraryId, "Not Ready Bed", "bed"),
                Asset(libraryId, "A Sting", "sting"),
            };
            var upserted = Assert.IsType<JinglePackUpsertResult.Upserted>(
                await packRepo.UpsertAsync(Slug, """{"packName":"Bed Pool SQL Test"}""", Slug, assets, CancellationToken.None));

            // MediaIds preserves the caller's own asset order (JinglePackUpsertResult.Upserted's own
            // contract) — select by ROLE, never by a positional Take(n) assumption (PLAN T416 review
            // O4): a reordered manifest must never silently break this fact.
            var idsByAsset = assets.Zip(upserted.MediaIds, (asset, id) => (asset.Role, id)).ToList();
            var bedIds = idsByAsset.Where(x => x.Role == "bed").Select(x => x.id).ToArray();
            var (_, unavailableBedId) = idsByAsset[2];
            var (_, notReadyBedId) = idsByAsset[3];
            var eligibleBedIds = bedIds.Except([unavailableBedId, notReadyBedId]).OrderBy(id => id).ToArray();

            // ...and, directly, the two disqualifying states the pool query itself must exclude (PLAN
            // T416 review O5 — a real-Postgres fact, not merely the text pin above, must go red if
            // AdBedPoolRepository.PoolSql ever drops either term).
            await conn.ExecuteAsync(
                "update library.media set unavailable_since = now() where id = @id", new { id = unavailableBedId });
            await conn.ExecuteAsync(
                "update library.media set state = 'discovered' where id = @id", new { id = notReadyBedId });

            // When the pool is queried for that library...
            var pool = Harness.AdBedPoolRepo(db);
            var poolIds = await pool.ListReadyBedIdsAsync(libraryId, CancellationToken.None);

            // Then only the two ELIGIBLE bed-role ids come back, ordered — the sting row, the
            // unavailable bed, and the not-ready bed never appear.
            Assert.Equal(eligibleBedIds, poolIds);
        }
    }
}
