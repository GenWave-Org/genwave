// gh-#865 — AdSpotRepository's own half of "Re-rendered automatically on vX": the stale pass's guarded
// swap stamps auto_rerendered_at + auto_rerendered_on_version in the same UPDATE, and an
// operator-driven MarkReady clears both. REAL Postgres via DatabaseFixture — the
// Gh854_AdSpotRenderVersionStore.cs fixture idiom one file over.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class Gh865FeatureAutoRerenderMarkerStore
{
    const int CurrentVersion = 1;

    static Task<long> SeedAuthoredMediaAsync(DatabaseFixture db, bool eligible) =>
        Harness.Repo(db).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: $"/authored/gh865-{Guid.NewGuid():N}.wav", eligible: eligible),
            CancellationToken.None);

    static async Task<(AdSpotRepository Repo, long Id, long NewMediaId)> SwapOneAsync(DatabaseFixture db)
    {
        await db.ResetAsync();
        await db.ResetAdsAndShowsAsync();
        var repo = Harness.AdSpotRepo(db);
        var oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
        var newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
        var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
        var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
        if (!await repo.SwapRenderedMediaAsync(id, oldMediaId, newMediaId, CurrentVersion, "v5.13.1", CancellationToken.None))
            throw new InvalidOperationException("arrange: the swap did not land");
        return (repo, id, newMediaId);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapStampsTheMarker(DatabaseFixture db) : IAsyncLifetime
    {
        AdSpot? spot;
        DateTime before;

        public async Task InitializeAsync()
        {
            before = DateTime.UtcNow.AddSeconds(-5);
            var (repo, id, _) = await SwapOneAsync(db);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheReleaseIsRecorded()
            => Assert.Equal("v5.13.1", spot?.AutoRerenderedOnVersion);

        [Fact]
        public void TheTimeIsRecorded()
            => Assert.True(spot?.AutoRerenderedAt > before);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnOperatorRenderClearsTheMarker(DatabaseFixture db) : IAsyncLifetime
    {
        AdSpot? spot;

        public async Task InitializeAsync()
        {
            // Given a spot the stale pass swapped earlier, which the operator then sends back through
            // a render of their own (rendering → ready by MarkReady)...
            var (repo, id, newMediaId) = await SwapOneAsync(db);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "update station.ad_spot set state = 'rendering'::station.ad_state where id = @id", new { id });

            await repo.MarkReadyAsync(id, newMediaId, CurrentVersion, CancellationToken.None);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotIsReady()
            => Assert.Equal(AdState.Ready, spot?.State);

        [Fact]
        public void TheReleaseIsCleared()
            => Assert.Null(spot?.AutoRerenderedOnVersion);

        [Fact]
        public void TheTimeIsCleared()
            => Assert.Null(spot?.AutoRerenderedAt);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAFreshSpotCarriesNoMarker(DatabaseFixture db) : IAsyncLifetime
    {
        AdSpot? spot;

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.AdSpotRepo(db);
            var mediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: mediaId, renderVersion: CurrentVersion);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoReleaseIsRecorded()
            => Assert.Null(spot?.AutoRerenderedOnVersion);
    }
}
