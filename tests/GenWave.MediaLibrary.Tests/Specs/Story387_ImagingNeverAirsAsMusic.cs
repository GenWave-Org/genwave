// STORY-387 — Imaging can never air as music (SPEC F158.4/F158.5 · PLAN T395)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection — the fence
// is selection SQL, provable only against the real planner (mirrors Story301_ImagingPoolQuery.cs's and
// Story250_ExplicitPoolExclusion.cs's own posture for the identical reason). Every fact here runs
// against live Postgres (the T362 loop law).

using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Garden;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureImagingNeverAirsAsMusic
{
    // ---------------------------------------------------------------------
    // Helpers (spec-local, the Story242/Gh149 convention)
    // ---------------------------------------------------------------------

    static readonly LibraryScope DefaultScope = new([1L]);

    /// <summary>A ready, measurable, eligible, NULL-imaging_kind music row — the scan+enrich shape
    /// (mirrors Story250_ExplicitPoolExclusion.cs's own InsertReadyAsync helper).</summary>
    static async Task<long> InsertReadyMusicAsync(MediaRepository repo, string path, string artist = "a", string title = "t")
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(title: title, artist: artist), CancellationToken.None);
        return id;
    }

    /// <summary>A ready, measurable, eligible, authored row stamped <paramref name="kind"/> (mirrors
    /// Story301_ImagingPoolQuery.cs's own InsertReadyAsync helper). <paramref name="tags"/> defaults
    /// to Harness.AuthoredInsert's own default (Artist "Station Name", Title "Please Stand By");
    /// PLAN T395 review finding-3's request-leg fact overrides it so the row matches a wish.</summary>
    static async Task<long> InsertReadyImagingAsync(
        MediaRepository repo, string path, ImagingKind kind, long libraryId = 1L, AudioTags? tags = null) =>
        await ((IAuthoredCatalogWriter)repo).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: path, libraryId: libraryId, tags: tags, kind: kind), CancellationToken.None);

    /// <summary>RequestCatalogProbeRepository, wired the SAME way production wires it — mirrors
    /// Story250_ExplicitPoolExclusion.cs's own Probe helper (PLAN T395 review finding-3: the
    /// request leg of AC1, over the SAME live-Postgres fixture every other fact in this file uses).</summary>
    static RequestCatalogProbeRepository RequestProbe(DatabaseFixture db) =>
        new(db.DataSource, new FakeSafeScopeProvider(), new FakeAudiencePostureProvider(),
            NullLogger<RequestCatalogProbeRepository>.Instance);

    /// <summary>Flags a row eligible/ineligible directly (STORY-040's own SetEligibilityAsync predates
    /// this fixture; a raw update is simpler than routing through the admin write seam here).</summary>
    static async Task SetEligibleAsync(DatabaseFixture db, long mediaId, bool eligible)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("update library.media set eligible = @eligible where id = @mediaId", new { mediaId, eligible });
    }

    /// <summary>Flags a row never_play (mirrors Story376_TheSameSongTwice.cs's own SetNeverPlayAsync helper).</summary>
    static async Task SetNeverPlayAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rating (media_id, never_play) values (@mediaId, true)", new { mediaId });
    }

    /// <summary>Stamps a row's explicit flag directly (PLAN T396, T395 review carry-forward 1) —
    /// mirrors Story250_ExplicitPoolExclusion.cs's own WriteEnrichmentAsync-based classification, but
    /// as a raw update: an authored imaging row (InsertAuthoredAsync) has no enrichment pass of its
    /// own to route the flag through, so a direct write is the simplest honest way to stamp one
    /// explicit for this fixture, the same way SetEligibleAsync/SetNeverPlayAsync above already do
    /// for their own columns.</summary>
    static async Task SetExplicitAsync(DatabaseFixture db, long mediaId, bool explicitFlag)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set explicit = @explicitFlag where id = @mediaId", new { mediaId, explicitFlag });
    }

    /// <summary>MediaRotationRepository, wired the SAME way production wires it (own
    /// StationSettingsRepository instance over the fixture's own station connection string) — mirrors
    /// Story371_ThumbsAggregateIsBounded.cs's own RotationRepo helper.</summary>
    static MediaRotationRepository RotationRepo(DatabaseFixture db) =>
        new(db.DataSource, new StationSettingsRepository(db.StationConnectionString), new FakeSafeScopeProvider());

    static async Task<IReadOnlyList<(long MediaId, long PlayCount)>> SnapshotRotationAsync(DatabaseFixture db)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<(long MediaId, long PlayCount)>(
            "select media_id, play_count from library.media_rotation order by media_id");
        return rows.ToList();
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — both directions of the fence
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheFenceHoldsFromTheImagingSide(DatabaseFixture db)
    {
        [Fact]
        public async Task RotationSelectionNeverReturnsAnAdRow()
        {
            // Ready, eligible imaging_kind='ad' row inside the music scope:
            //   GetRotationCandidateAsync never returns it (loop the pick to exhaustion).
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var musicId = await InsertReadyMusicAsync(repo, "/fence/rotation-music.flac");
            var adId = await InsertReadyImagingAsync(repo, "/fence/rotation-ad.wav", ImagingKind.Ad);

            var catalog = (IMediaCatalog)repo;
            for (var i = 0; i < 15; i++)
            {
                var candidate = await catalog.GetRotationCandidateAsync(DefaultScope, [], artistSeparation: 0, CancellationToken.None);
                Assert.NotNull(candidate);
                Assert.Equal(musicId.ToString(), candidate.Media.MediaId);
                Assert.NotEqual(adId.ToString(), candidate.Media.MediaId);
            }
        }

        [Fact]
        public async Task EnvelopeSelectionNeverReturnsAnAdRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var musicId = await InsertReadyMusicAsync(repo, "/fence/envelope-music.flac");
            var adId = await InsertReadyImagingAsync(repo, "/fence/envelope-ad.wav", ImagingKind.Ad);

            var catalog = (IMediaCatalog)repo;
            var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                DefaultScope, [], artistSeparation: 0, SegmentEnvelope.StationDefault, limit: 20, CancellationToken.None);

            Assert.Contains(pool, c => c.Media.MediaId == musicId.ToString());
            Assert.DoesNotContain(pool, c => c.Media.MediaId == adId.ToString());
        }

        [Fact]
        public async Task MediaRandomNeverReturnsAnAdRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var musicId = await InsertReadyMusicAsync(repo, "/fence/random-music.flac");
            var adId = await InsertReadyImagingAsync(repo, "/fence/random-ad.wav", ImagingKind.Ad);

            var catalog = (IMediaCatalog)repo;
            for (var i = 0; i < 15; i++)
            {
                var result = await catalog.GetRandomPlayableAsync(DefaultScope, [], CancellationToken.None);
                Assert.NotNull(result);
                Assert.Equal(musicId.ToString(), result.MediaId);
                Assert.NotEqual(adId.ToString(), result.MediaId);
            }
        }

        [Fact]
        public async Task TheFenceCoversEveryImagingKindNotJustAd()
        {
            // A station_id row in the music scope is equally invisible — the fence is
            //   `imaging_kind is null`, not `!= 'ad'` (retro-fixes the standing leak).
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var musicId = await InsertReadyMusicAsync(repo, "/fence/stationid-music.flac");
            var stationIdId = await InsertReadyImagingAsync(repo, "/fence/stationid-ident.wav", ImagingKind.StationId);

            var catalog = (IMediaCatalog)repo;
            for (var i = 0; i < 15; i++)
            {
                var result = await catalog.GetRandomPlayableAsync(DefaultScope, [], CancellationToken.None);
                Assert.NotNull(result);
                Assert.Equal(musicId.ToString(), result.MediaId);
                Assert.NotEqual(stationIdId.ToString(), result.MediaId);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheRequestMatcherNeverResolvesAnAdRow(DatabaseFixture db)
    {
        // AC1's request leg (PLAN T395 review finding-3): a ready ad row whose artist matches a
        //   wish is still never resolved — RequestCatalogProbeRepository composes
        //   MediaRepository.PlayablePredicate (T395 review finding-1's own fix to that class), so it
        //   inherits the "imaging_kind is null" fence for free.

        [Fact]
        public async Task FindBestAsyncReturnsNullForAMatchingAdRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            await InsertReadyImagingAsync(
                repo, "/fence/request-ad.wav", ImagingKind.Ad,
                tags: new AudioTags(Artist: "Wish Artist", Title: "Wish Title"));

            var found = await RequestProbe(db).FindBestAsync("Wish Artist", null, null, CancellationToken.None);

            Assert.Null(found);
        }

        [Fact]
        public async Task GetSelectableByIdAsyncReturnsNullForAnAdRow()
        {
            // Covers the second fork: the fulfillment re-check must refuse the same ad id directly,
            //   not just the artist/title search leg above.
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var adId = await InsertReadyImagingAsync(repo, "/fence/request-selectable-ad.wav", ImagingKind.Ad);

            var found = await RequestProbe(db).GetSelectableByIdAsync(adId, envelope: null, CancellationToken.None);

            Assert.Null(found);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheFenceIsInvisibleFromTheMusicSide(DatabaseFixture db)
    {
        [Fact]
        public async Task ANullKindMusicRowSurfacesExactlyAsBefore()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var musicId = await InsertReadyMusicAsync(repo, "/fence/music-side.flac");

            var catalog = (IMediaCatalog)repo;

            var candidate = await catalog.GetRotationCandidateAsync(DefaultScope, [], artistSeparation: 0, CancellationToken.None);
            Assert.NotNull(candidate);
            Assert.Equal(musicId.ToString(), candidate.Media.MediaId);

            var randomResult = await catalog.GetRandomPlayableAsync(DefaultScope, [], CancellationToken.None);
            Assert.NotNull(randomResult);
            Assert.Equal(musicId.ToString(), randomResult.MediaId);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheAdsPoolRead(DatabaseFixture db)
    {
        [Fact]
        public async Task TheAdsPoolReturnsOnlyReadyEligibleAdRows()
        {
            // ready+measurable+eligible+not never_play+imaging_kind='ad' in the ads library;
            //   an ineligible or never_play ad row never vends.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var readyAdId = await InsertReadyImagingAsync(repo, "/fence/ads-pool-ready.wav", ImagingKind.Ad);

            var ineligibleAdId = await InsertReadyImagingAsync(repo, "/fence/ads-pool-ineligible.wav", ImagingKind.Ad);
            await SetEligibleAsync(db, ineligibleAdId, false);

            var neverPlayAdId = await InsertReadyImagingAsync(repo, "/fence/ads-pool-never-play.wav", ImagingKind.Ad);
            await SetNeverPlayAsync(db, neverPlayAdId);

            await InsertReadyImagingAsync(repo, "/fence/ads-pool-liner.wav", ImagingKind.Liner);
            await InsertReadyMusicAsync(repo, "/fence/ads-pool-music.flac");

            var catalog = (IMediaCatalog)repo;
            for (var i = 0; i < 10; i++)
            {
                var result = await catalog.GetRandomReadyAdSpotAsync(DefaultScope, [], CancellationToken.None);
                Assert.NotNull(result);
                Assert.Equal(readyAdId.ToString(), result.MediaId);
            }
        }

        [Fact]
        public async Task AnExplicitMarkedAdRowNeverVendsOnAnEveryoneStation()
        {
            // PLAN T395 review carry-forward, RULED: the ads-pool read's ExplicitPredicate() term
            //   ANDs in exactly like every other pool-predicate query (Story250's own
            //   ScenarioEveryoneExcludesAtThePool mirror) — an ad read has no dead-air excuse to
            //   trade it for; null is IAdSpotSource.GetNextSpotAsync's own always-legal answer, so
            //   excluding an explicit-flagged spot costs nothing but a floor miss (PLAN T396).
            await db.ResetAsync();
            var repo = Harness.Repo(db, audiencePosture: new FakeAudiencePostureProvider(AudiencePosture.Everyone));

            var explicitAdId = await InsertReadyImagingAsync(repo, "/fence/ads-pool-explicit.wav", ImagingKind.Ad);
            await SetExplicitAsync(db, explicitAdId, true);

            var admittedAdId = await InsertReadyImagingAsync(repo, "/fence/ads-pool-admitted.wav", ImagingKind.Ad);

            var catalog = (IMediaCatalog)repo;
            for (var i = 0; i < 10; i++)
            {
                var result = await catalog.GetRandomReadyAdSpotAsync(DefaultScope, [], CancellationToken.None);
                Assert.NotNull(result);
                Assert.Equal(admittedAdId.ToString(), result.MediaId);
                Assert.NotEqual(explicitAdId.ToString(), result.MediaId);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — what the fence must NOT touch
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSafeFloorIsUntouched(DatabaseFixture db)
    {
        [Fact]
        public async Task SafeTrackSelectionAnswersExactlyAsBefore()
        {
            // The never-silence path deliberately skips the fence (F158.4) — a SafeScope row
            //   with a non-null imaging_kind still vends from GetRandomReadyAsync.
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var adId = await InsertReadyImagingAsync(repo, "/fence/safe-floor-ad.wav", ImagingKind.Ad);

            var catalog = (IMediaCatalog)repo;
            // Mirrors InternalEndpoints.HandleSafeTrackAsync's own call shape: GetRandomReadyAsync, no
            // recent-exclusion list, over whatever LibraryScope the operator's SafeScope resolves to.
            var result = await catalog.GetRandomReadyAsync(DefaultScope, [], CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(adId.ToString(), result.MediaId);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAdsNeverEnterTheRotationLedger(DatabaseFixture db)
    {
        [Fact]
        public async Task AnAdAiringLeavesMediaRotationByteIdentical()
        {
            // TrackAired for an imaging_kind='ad' row: library.media_rotation unchanged
            //   (the F149.2 exclusion re-pinned over the new kind).
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var adId = await InsertReadyImagingAsync(repo, "/fence/ledger-ad.wav", ImagingKind.Ad);

            var before = await SnapshotRotationAsync(db);

            await RotationRepo(db).RecordAiringAsync(adId, DateTimeOffset.UtcNow, CancellationToken.None);

            var after = await SnapshotRotationAsync(db);
            Assert.Equal(before, after);
            Assert.DoesNotContain(after, row => row.MediaId == adId);
        }
    }
}
