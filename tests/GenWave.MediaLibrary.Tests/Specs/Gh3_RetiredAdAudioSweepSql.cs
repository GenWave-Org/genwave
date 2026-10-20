// gh-#3 — the retired ad audio sweep's own SQL, against real Postgres: MediaRepository's
// IAuthoredMediaSweep (ineligible rows in one library past a cutoff; delete only while ineligible)
// and AdSpotRepository's IAdAudioReferences (every media id a spot still names, minus the audio of a
// spot retired before the cutoff). The sweep loop itself is proved in GenWave.Ads.Tests over fakes.

using Dapper;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRetiredAdAudioSweepSql
{
    static readonly DateTimeOffset Cutoff = DateTimeOffset.UtcNow.AddDays(-30);

    static async Task<long> SeedLibraryAsync(DatabaseFixture db, string name)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into library.library (name) values (@name) returning id", new { name });
    }

    static async Task<long> SeedMediaAsync(DatabaseFixture db, long libraryId, bool eligible, DateTime mtime)
    {
        var id = await Harness.Repo(db).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: $"/authored/ads/gh3-{Guid.NewGuid():N}.wav", libraryId: libraryId, eligible: eligible),
            CancellationToken.None);
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("update library.media set mtime = @mtime where id = @id", new { id, mtime });
        return id;
    }

    static async Task RetireAsync(DatabaseFixture db, long spotId, DateTime retiredAt)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update station.ad_spot set state = 'retired'::station.ad_state, retired_at = @retiredAt where id = @spotId",
            new { spotId, retiredAt });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListingIneligibleAudio(DatabaseFixture db) : IAsyncLifetime
    {
        long oldIneligible, freshIneligible, oldEligible, otherLibrary;
        IReadOnlyList<AuthoredMediaFile> listed = [];

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            // Given ads-library rows of every shape, and an old ineligible row in another library
            var ads = await SeedLibraryAsync(db, $"gh3-ads-{Guid.NewGuid():N}");
            var other = await SeedLibraryAsync(db, $"gh3-safe-{Guid.NewGuid():N}");
            var old = DateTime.UtcNow.AddDays(-60);
            oldIneligible = await SeedMediaAsync(db, ads, eligible: false, old);
            freshIneligible = await SeedMediaAsync(db, ads, eligible: false, DateTime.UtcNow);
            oldEligible = await SeedMediaAsync(db, ads, eligible: true, old);
            otherLibrary = await SeedMediaAsync(db, other, eligible: false, old);

            // When the ads library is listed with a 30-day cutoff
            listed = await Harness.Repo(db).ListIneligibleAsync(ads, Cutoff, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OnlyTheOldIneligibleAdsRowIsListed() =>
            Assert.Equal([oldIneligible], listed.Select(m => m.Id));

        [Fact]
        public void ItsPathComesBack() => Assert.StartsWith("/authored/ads/gh3-", Assert.Single(listed).Path);

        [Fact]
        public void TheOthersAreLeftOut()
        {
            var ids = listed.Select(m => m.Id).ToHashSet();
            Assert.DoesNotContain(freshIneligible, ids);
            Assert.DoesNotContain(oldEligible, ids);
            Assert.DoesNotContain(otherLibrary, ids);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDeletingARow(DatabaseFixture db)
    {
        [Fact]
        public async Task AnIneligibleRowIsDeleted()
        {
            await db.ResetAsync();
            var id = await SeedMediaAsync(db, 1, eligible: false, DateTime.UtcNow.AddDays(-60));

            Assert.True(await Harness.Repo(db).DeleteIneligibleAsync(id, CancellationToken.None));
            Assert.Equal(0, await Harness.CountMediaRowsAsync(db));
        }

        [Fact]
        public async Task ARowMadeEligibleAgainSurvives()
        {
            // Sad path: an operator re-enabled the row between the list read and the delete
            await db.ResetAsync();
            var id = await SeedMediaAsync(db, 1, eligible: true, DateTime.UtcNow.AddDays(-60));

            Assert.False(await Harness.Repo(db).DeleteIneligibleAsync(id, CancellationToken.None));
            Assert.Equal(1, await Harness.CountMediaRowsAsync(db));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadingWhatSpotsStillUse(DatabaseFixture db) : IAsyncLifetime
    {
        const long ReadyMedia = 9001, RecentlyRetiredMedia = 9002, LongRetiredMedia = 9003;
        IReadOnlySet<long> inUse = new HashSet<long>();
        IReadOnlySet<string> previews = new HashSet<string>();

        public async Task InitializeAsync()
        {
            await db.ResetAdsAndShowsAsync();
            var repo = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");

            // Given a ready spot, one retired last week, and one retired two months ago
            var ready = await Harness.SeedReadySpotAsync(repo, sponsorId, ReadyMedia);
            var recent = await Harness.SeedReadySpotAsync(repo, sponsorId, RecentlyRetiredMedia);
            var longGone = await Harness.SeedReadySpotAsync(repo, sponsorId, LongRetiredMedia);
            await RetireAsync(db, recent, DateTime.UtcNow.AddDays(-7));
            await RetireAsync(db, longGone, DateTime.UtcNow.AddDays(-60));
            await repo.StampPreviewAsync(ready, "/authored/preview/1-abc.wav", new string('a', 64), CancellationToken.None);

            // When the references are read with a 30-day cutoff
            inUse = await repo.ListMediaIdsInUseAsync(Cutoff, CancellationToken.None);
            previews = await repo.ListPreviewPathsAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheReadySpotsAudioIsInUse() => Assert.Contains(ReadyMedia, inUse);

        [Fact]
        public void ARecentlyRetiredSpotsAudioIsStillKept() => Assert.Contains(RecentlyRetiredMedia, inUse);

        [Fact]
        public void ALongRetiredSpotsAudioIsFree() => Assert.DoesNotContain(LongRetiredMedia, inUse);

        [Fact]
        public void TheStampedPreviewIsListed() => Assert.Equal(["/authored/preview/1-abc.wav"], previews);
    }
}
