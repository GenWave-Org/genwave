// gh-#854 — AdSpotRepository's own new SQL surface: FindStaleReadyAsync (oldest-first, excludes ids /
// current-version / non-ready rows) and SwapRenderedMediaAsync (guarded on state='ready' AND
// media_id=oldMediaId). Mirrors Story389_AdSpotLifecycleStore.cs's own fixture idiom one file over —
// REAL Postgres via DatabaseFixture, Harness.SeedReadySpotAsync/SeedSponsorAsync/AdSpotRepo, a direct
// AdSpotRepository under test — real SQL, a real guarded UPDATE.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class Gh854FeatureAdSpotRenderVersionStore
{
    // CurrentVersion mirrors GenWave.Ads.AdRenderVersion.Current — this test project carries no
    // reference to GenWave.Ads (MediaLibrary never depends on Ads), so the value is pinned here by
    // convention, the same posture Gh854_RenderVersionMigrationMirror.cs takes toward db/48 vs db/06.
    const int CurrentVersion = 1;

    static async Task SetStateChangedAtAsync(DatabaseFixture db, long id, DateTime stateChangedAt)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update station.ad_spot set state_changed_at = @StateChangedAt where id = @Id",
            new { Id = id, StateChangedAt = stateChangedAt });
    }

    /// <summary>A real, id-bearing <c>library.media</c> row for <see cref="AdSpotRepository.SwapRenderedMediaAsync"/>'s
    /// own <see cref="GenWave.Core.Abstractions.IAdminMediaLookup"/> guard to find (gh-#854) — the
    /// SAME <see cref="Harness.Repo"/>/<see cref="Harness.AuthoredInsert"/> pair
    /// Story076_AuthoredInsertSeam.cs seeds through, since <c>station.ad_spot.media_id</c> carries no
    /// cross-schema FK (PRD §9) and this store's own guard is the only thing that would ever notice a
    /// row this shallow. A fresh <see cref="Guid"/>-suffixed <c>path</c> per call — <c>library.media.path</c>
    /// carries its own unique constraint, and every scenario below seeds at least two rows (old and
    /// new) in the same test, so <see cref="Harness.AuthoredInsert"/>'s own fixed default path would
    /// collide on the second call.</summary>
    static Task<long> SeedAuthoredMediaAsync(DatabaseFixture db, bool eligible) =>
        Harness.Repo(db).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: $"/authored/gh854-{Guid.NewGuid():N}.wav", eligible: eligible),
            CancellationToken.None);

    // ---------------------------------------------------------------------
    // FindStaleReadyAsync — oldest-first, filtered by version/state/exclusion
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheOldestStaleCandidateWinsOverANewerOne(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        long olderId;
        AdSpot? found;

        public async Task InitializeAsync()
        {
            // Given two ready spots, both behind the current version, the first ready well before
            // the second...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            olderId = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: 500, renderVersion: 0);
            await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: 501, renderVersion: 0);
            await SetStateChangedAtAsync(db, olderId, DateTime.UtcNow.AddMinutes(-10));

            // When the worker looks for the next stale candidate...
            found = await repo.FindStaleReadyAsync(CurrentVersion, excludeIds: [], CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ACandidateIsFound()
            => Assert.NotNull(found);

        [Fact]
        public void TheOlderOneComesFirst()
            // Then it finds the older one, not the newer.
            => Assert.Equal((long?)olderId, found?.Id);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioExcludedIdsAreNeverCandidates(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        AdSpot? found;

        public async Task InitializeAsync()
        {
            // Given exactly one stale ready spot...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var staleId = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: 500, renderVersion: 0);

            // When it is passed in the caller's own exclusion set (the process-lifetime skip set, a
            // failed re-render already given up on this tick)...
            found = await repo.FindStaleReadyAsync(CurrentVersion, excludeIds: [staleId], CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingComesBack()
            // The excluded row is never a candidate.
            => Assert.Null(found);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARowAlreadyAtTheCurrentVersionIsNeverACandidate(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        AdSpot? found;

        public async Task InitializeAsync()
        {
            // Given a ready spot already at the current render version...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: 500, renderVersion: CurrentVersion);

            // When the worker looks for a stale candidate...
            found = await repo.FindStaleReadyAsync(CurrentVersion, excludeIds: [], CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingComesBack()
            // An up-to-date row is never a re-render candidate.
            => Assert.Null(found);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASpotCarryingAPendingMarkerIsNeverACandidate(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        AdSpot? found;

        public async Task InitializeAsync()
        {
            // Given a stale ready spot that has ALREADY been swapped once, with its old-media flip
            // failing (so pending_retire_media_id survives past the call) — landed on a render version
            // still deliberately behind current, isolating this guard from FindStaleReadyAsync's own
            // separate version filter, which alone would already exclude a freshly re-rendered row...
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);
            var oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(oldMediaId);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
            await repo.SwapRenderedMediaAsync(id, oldMediaId, newMediaId, renderVersion: 0, CancellationToken.None);

            // When the worker looks for the next stale candidate...
            found = await repo.FindStaleReadyAsync(CurrentVersion, excludeIds: [], CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingComesBack()
            // A spot still waiting on its own pending marker is never a fresh candidate for a second
            // swap, no matter how stale its own render version still reads.
            => Assert.Null(found);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASpotThatIsNotReadyIsNeverACandidate(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        AdSpot? found;

        public async Task InitializeAsync()
        {
            // Given only a draft spot — never rendered, so never airing...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await repo.CreateAsync(
                new NewAdSpot(sponsorId, "Draft spot", Brief: "A cozy hardware shop", Script: null, AdSource.Llm,
                    PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null,
                    InitialState: AdState.Draft, FailReason: null),
                CancellationToken.None);

            // When the worker looks for a stale candidate...
            found = await repo.FindStaleReadyAsync(CurrentVersion, excludeIds: [], CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingComesBack()
            // Only Ready rows are ever re-render candidates.
            => Assert.Null(found);
    }

    // ---------------------------------------------------------------------
    // SwapRenderedMediaAsync — guarded on state='ready' AND media_id=oldMediaId
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapSucceedsWhenTheGuardMatches(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        long oldMediaId;
        long newMediaId;
        bool swapped;
        AdSpot? spot;

        public async Task InitializeAsync()
        {
            // Given a ready spot airing a real, eligible old media row, and a real (still-ineligible —
            // the gh-#854 render-tail invariant: a re-render's own confirmAsync is the SwapRenderedMediaAsync
            // call under test here, so nothing has flipped it eligible yet) new one waiting to take
            // over...
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);

            // When the re-render's own confirmAsync closure swaps it in, oldMediaId still matching...
            swapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapSucceeds()
            => Assert.True(swapped);

        [Fact]
        public void TheRowNowCarriesTheNewMediaId()
            => Assert.Equal(newMediaId, spot?.MediaId);

        [Fact]
        public void TheRowNowCarriesTheNewRenderVersion()
            => Assert.Equal(CurrentVersion, spot?.RenderVersion);

        [Fact]
        public async Task TheOldMediaIsNowIneligible()
            => Assert.False(await Harness.EligibleOfAsync(db, oldMediaId));

        [Fact]
        public async Task TheNewMediaIsNowEligible()
            => Assert.True(await Harness.EligibleOfAsync(db, newMediaId));
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapDeclinesWhenTheOldMediaIdNoLongerMatches(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        long oldMediaId;
        bool swapped;
        AdSpot? spot;

        public async Task InitializeAsync()
        {
            // Given a ready spot airing a real, eligible old media row...
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);

            // When a swap is attempted against the WRONG old media id (a second, stale confirm racing
            // in after some other write already moved the row on)...
            swapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId: 999_999, newMediaId: 600, renderVersion: CurrentVersion, CancellationToken.None);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapDeclines()
            => Assert.False(swapped);

        [Fact]
        public void TheMediaIdIsUntouched()
            => Assert.Equal(oldMediaId, spot?.MediaId);

        [Fact]
        public async Task TheOldMediaStaysEligible()
            => Assert.True(await Harness.EligibleOfAsync(db, oldMediaId));
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapDeclinesWhenTheOldMediaIsIneligible(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        long oldMediaId;
        long newMediaId;
        bool swapped;
        AdSpot? spot;

        public async Task InitializeAsync()
        {
            // Given a ready spot whose old media has ALREADY gone ineligible out from under it (an
            // operator's own bulk-eligibility edit, or a second swap that already claimed it) — the
            // upfront IAdminMediaLookup guard (gh-#854) must catch this BEFORE the guarded UPDATE ever
            // runs, not merely rely on the SQL-time state/media_id check...
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);

            // When the re-render's own confirmAsync closure lands anyway...
            swapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapDeclines()
            => Assert.False(swapped);

        [Fact]
        public void TheMediaIdIsUntouched()
            => Assert.Equal(oldMediaId, spot?.MediaId);

        [Fact]
        public async Task TheNewMediaStaysIneligible()
            => Assert.False(await Harness.EligibleOfAsync(db, newMediaId));
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapDeclinesWhenTheOldMediaIsNeverPlay(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        long oldMediaId;
        long newMediaId;
        bool swapped;

        public async Task InitializeAsync()
        {
            // Given a ready spot whose old media is eligible but flagged never_play (an operator's own
            // safety pull, PLAN T401's own AdminMediaDto.NeverPlay) — SwapRenderedMediaAsync's own
            // upfront guard reads this fact through the SAME IAdminMediaLookup an operator's own
            // never-play toggle already writes through (library.media_rating; MediaRatingRepository —
            // this test project's own established RatingRepo shape, e.g. Story111_NeverPlaySelectionSuppression.cs).
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            var ratingRepo = new MediaRatingRepository(db.DataSource, new FakeSafeScopeProvider());
            await ratingRepo.SetNeverPlayAsync(oldMediaId.ToString(), neverPlay: true, CancellationToken.None);

            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);

            swapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapDeclines()
            => Assert.False(swapped);

        [Fact]
        public async Task TheNewMediaStaysIneligible()
            => Assert.False(await Harness.EligibleOfAsync(db, newMediaId));
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapDeclinesWhenTheSpotIsNoLongerReady(DatabaseFixture db) : IAsyncLifetime
    {
        readonly AdSpotRepository repo = Harness.AdSpotRepo(db);
        long oldMediaId;
        long newMediaId;
        bool swapped;
        AdSpot? spot;

        public async Task InitializeAsync()
        {
            // Given a ready spot, airing a real eligible old media row, that an operator retires WHILE
            // its background re-render is still running — the old media row stays genuinely eligible/
            // playable throughout, so a swap declining here proves the SQL-time state guard itself is
            // doing the refusing, not merely the upfront IAdminMediaLookup check this suite's own
            // ineligible/never_play scenarios already cover...
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
            var ready = await repo.GetByIdAsync(id, CancellationToken.None)
                ?? throw new InvalidOperationException("arrange: seeded spot went missing");
            var retired = await repo.RetireAsync(id, ready.Version, CancellationToken.None);
            if (retired.Result != AdSpotWriteResult.Updated)
                throw new InvalidOperationException("arrange: retiring the spot did not apply");

            // When the re-render's own confirmAsync closure lands afterward, oldMediaId still
            // matching...
            swapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapDeclines()
            // The state half of the guard alone is enough to refuse.
            => Assert.False(swapped);

        [Fact]
        public void TheRetiredRowsMediaIdIsUntouched()
            => Assert.Equal(oldMediaId, spot?.MediaId);

        [Fact]
        public async Task NeitherMediaRowsEligibilityIsTouched()
        {
            Assert.True(await Harness.EligibleOfAsync(db, oldMediaId));
            Assert.False(await Harness.EligibleOfAsync(db, newMediaId));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapDeclinesWhenARetireMarkerIsAlreadyPending(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long firstNewMediaId;
        bool secondSwapped;
        AdSpot? spot;

        public async Task InitializeAsync()
        {
            // Given a spot already swapped once, its old-media flip failing so
            // pending_retire_media_id survives (gh-#854: a second swap must never overwrite that
            // still-outstanding marker)...
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);
            var oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            firstNewMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            var secondNewMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(oldMediaId);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
            var firstSwapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId, firstNewMediaId, renderVersion: CurrentVersion, CancellationToken.None);
            if (!firstSwapped)
                throw new InvalidOperationException("arrange: the first swap did not land");

            // When a second swap targets the spot's own CURRENT media id (the first swap's own new
            // media, still Ready) while that first marker is still outstanding...
            secondSwapped = await repo.SwapRenderedMediaAsync(
                id, firstNewMediaId, secondNewMediaId, renderVersion: CurrentVersion, CancellationToken.None);
            spot = await repo.GetByIdAsync(id, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSecondSwapDeclines()
            => Assert.False(secondSwapped);

        [Fact]
        public void TheSpotStaysOnTheFirstSwapsNewMedia()
            => Assert.Equal(firstNewMediaId, spot?.MediaId);
    }
}
