// STORY-429 — the preview sweep's own store half, against real Postgres (SPEC F176.2 · PLAN T442)
//
// The Story389_AdSpotLifecycleStore precedent, this same directory, one lifecycle stamp trio over:
// ListPreviewsToSweepAsync's own predicate (a ready/retired spot's preview is always due, a still-
// editable spot's preview is due only once it ages past retention) and ClearPreviewAsync/
// StampPreviewAsync's own total round trip, proven live rather than only through the FakeAdSpotStore
// double Story429_PreviewCleanup.cs (GenWave.Ads.Tests) already exercises the guardian's own sweep
// loop against (the T362 loop law — every new SQL read here gets its own live fact).

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePreviewSweepSql
{
    static NewAdSpot Draft(long sponsorId) =>
        new(sponsorId, "Draft spot", Brief: "A cozy hardware shop", Script: "ANNOUNCER: Hi.", AdSource.Llm,
            PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null, InitialState: AdState.Draft,
            FailReason: null);

    /// <summary>Backdates a row's own <c>preview_at</c> directly — the ONLY way a fact can put a
    /// stamped preview genuinely past <see cref="AdSpotRepository.ListPreviewsToSweepAsync"/>'s own
    /// retention cutoff without an actual wall-clock wait (mirrors
    /// <c>Story389_AdSpotLifecycleStore.SetStateChangedAtAsync</c>'s own posture, one column over).
    /// </summary>
    static async Task SetPreviewAtAsync(DatabaseFixture db, long id, DateTime previewAt)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update station.ad_spot set preview_at = @PreviewAt where id = @Id",
            new { Id = id, PreviewAt = previewAt });
    }

    /// <summary>An independent raw-SQL read (bypasses <see cref="AdSpotRepository"/> itself) so a fact
    /// verifies what the repository under test actually persisted — the
    /// <c>Story389_AdSpotLifecycleStore.ReadRowAsync</c> precedent, one column trio over.</summary>
    static async Task<(string? Path, string? Key, DateTime? At)> ReadPreviewRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(string?, string?, DateTime?)>(
            "select preview_path, preview_key, preview_at from station.ad_spot where id = @id",
            new { id });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListPreviewsToSweepAsyncAppliesTheSpecsOwnPredicate(DatabaseFixture db)
    {
        [Fact]
        public async Task AReadySpotWithAFreshPreviewIsListed()
        {
            // Given a spot already Ready, whose preview was stamped moments ago...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var readyId = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: 900);
            await repo.StampPreviewAsync(readyId, "/authored/preview/900-abc.wav", new string('a', 64), CancellationToken.None);

            // When the sweep candidates are read for a 30-day retention...
            var candidates = await repo.ListPreviewsToSweepAsync(TimeSpan.FromDays(30), DateTimeOffset.UtcNow, CancellationToken.None);

            // Then it is due regardless of age — Ready leaves the editable lifecycle outright.
            Assert.Contains(candidates, s => s.Id == readyId);
        }

        [Fact]
        public async Task ADraftSpotWithAFreshPreviewIsNotListed()
        {
            // Given a still-editable draft spot whose preview was stamped moments ago...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.StampPreviewAsync(spot.Id, "/authored/preview/1-abc.wav", new string('a', 64), CancellationToken.None);

            // When the sweep candidates are read for a 30-day retention...
            var candidates = await repo.ListPreviewsToSweepAsync(TimeSpan.FromDays(30), DateTimeOffset.UtcNow, CancellationToken.None);

            // Then it is not due — still editable, still fresh.
            Assert.DoesNotContain(candidates, s => s.Id == spot.Id);
        }

        [Fact]
        public async Task ADraftSpotWithAPreviewOlderThanRetentionIsListed()
        {
            // Given a still-editable draft spot whose preview was stamped, then backdated well past a
            // 1-day retention...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.StampPreviewAsync(spot.Id, "/authored/preview/1-abc.wav", new string('a', 64), CancellationToken.None);
            await SetPreviewAtAsync(db, spot.Id, DateTime.UtcNow.AddDays(-2));

            // When the sweep candidates are read for a 1-day retention...
            var candidates = await repo.ListPreviewsToSweepAsync(TimeSpan.FromDays(1), DateTimeOffset.UtcNow, CancellationToken.None);

            // Then it is due — aged out, even though still editable.
            Assert.Contains(candidates, s => s.Id == spot.Id);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioClearPreviewAsyncNullsAllThreeStamps(DatabaseFixture db)
    {
        [Fact]
        public async Task TheStampedTrioIsClearedInOneCall()
        {
            // Given a spot with a stamped preview...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            await repo.StampPreviewAsync(spot.Id, "/authored/preview/1-abc.wav", new string('a', 64), CancellationToken.None);

            // When the preview is cleared...
            var cleared = await repo.ClearPreviewAsync(spot.Id, CancellationToken.None);

            // Then the call reports success, and all three columns read back null.
            Assert.True(cleared);
            var (path, key, at) = await ReadPreviewRowAsync(db, spot.Id);
            Assert.Null(path);
            Assert.Null(key);
            Assert.Null(at);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStampPreviewAsyncRoundTrips(DatabaseFixture db)
    {
        [Fact]
        public async Task TheStampedPathAndKeyAreReadBackVerbatimWithATimestamp()
        {
            // Given a plain draft spot with no preview yet...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            var key = new string('f', 64);
            var before = DateTime.UtcNow;

            // When a preview is stamped...
            var stamped = await repo.StampPreviewAsync(spot.Id, "/authored/preview/1-fff.wav", key, CancellationToken.None);

            // Then the call reports success, and the row reads back the exact path/key with a fresh
            // preview_at.
            Assert.True(stamped);
            var (path, readKey, at) = await ReadPreviewRowAsync(db, spot.Id);
            Assert.Equal("/authored/preview/1-fff.wav", path);
            Assert.Equal(key, readKey);
            Assert.NotNull(at);
            Assert.True(at >= before);
        }
    }
}
