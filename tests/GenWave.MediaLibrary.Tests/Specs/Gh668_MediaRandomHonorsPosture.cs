// gh-#668 — /media/random honors the audience posture (SPEC F95.4)
//
// GetRandomPlayableAsync (the read behind GET /api/media/random) had never ANDed in the posture
// filter. Postgres-backed (Category=Integration): the filter is selection SQL.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMediaRandomHonorsPosture
{
    static readonly LibraryScope DefaultScope = new([1L]);

    static async Task<long> InsertReadyMusicAsync(MediaRepository repo, string path)
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: path, artist: path), CancellationToken.None);
        return id;
    }

    static async Task SetExplicitAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("update library.media set explicit = true where id = @mediaId", new { mediaId });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnExplicitRowUnderEachPosture(DatabaseFixture db)
    {
        [Fact]
        public async Task AnEveryoneStationNeverGetsTheExplicitRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Everyone));
            var cleanId = await InsertReadyMusicAsync(repo, "/gh668/clean.flac");
            var explicitId = await InsertReadyMusicAsync(repo, "/gh668/explicit.flac");
            await SetExplicitAsync(db, explicitId);

            var catalog = (IMediaCatalog)repo;
            for (var i = 0; i < 15; i++)
            {
                var result = await catalog.GetRandomPlayableAsync(DefaultScope, [], CancellationToken.None);
                Assert.NotNull(result);
                Assert.Equal(cleanId.ToString(), result.MediaId);
            }
        }

        [Fact]
        public async Task AnEveryoneStationWithOnlyExplicitRowsGetsNothing()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Everyone));
            var explicitId = await InsertReadyMusicAsync(repo, "/gh668/only-explicit.flac");
            await SetExplicitAsync(db, explicitId);

            var result = await ((IMediaCatalog)repo).GetRandomPlayableAsync(DefaultScope, [], CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task AMatureStationStillGetsTheExplicitRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Mature));
            var explicitId = await InsertReadyMusicAsync(repo, "/gh668/mature-explicit.flac");
            await SetExplicitAsync(db, explicitId);

            var result = await ((IMediaCatalog)repo).GetRandomPlayableAsync(DefaultScope, [], CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(explicitId.ToString(), result.MediaId);
        }
    }
}
