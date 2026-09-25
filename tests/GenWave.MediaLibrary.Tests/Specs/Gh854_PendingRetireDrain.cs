// gh-#854 — db/48's own pending_retire_media_id column and AdSpotRepository's own guarded
// read/clear surface over it (ListPendingRetiresAsync/ClearPendingRetireAsync): the durable half of
// SwapRenderedMediaAsync's own post-commit best-effort old-media flip, proved here against REAL
// Postgres. The worker-tick drain that consumes this surface (never turning off a media id some OTHER
// spot now names) is proved one project over, in GenWave.Ads.Tests, against a full in-memory
// AdSpotWorker tick — this file proves the SQL surface itself, including its own currently-referenced
// guard, is correct underneath that drain.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class Gh854FeaturePendingRetireDrain
{
    const int CurrentVersion = 1;

    /// <summary>A real, id-bearing <c>library.media</c> row — the same
    /// Gh854_AdSpotRenderVersionStore.cs <c>SeedAuthoredMediaAsync</c> shape, a fresh path per call
    /// since every scenario below seeds several rows in the same test.</summary>
    static Task<long> SeedAuthoredMediaAsync(DatabaseFixture db, bool eligible) =>
        Harness.Repo(db).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: $"/authored/gh854-pending-{Guid.NewGuid():N}.wav", eligible: eligible),
            CancellationToken.None);

    /// <summary>The raw <c>pending_retire_media_id</c> column for a spot, straight off Postgres — the
    /// Gh854_AdSpotRenderVersionStore.cs <c>SetStateChangedAtAsync</c> precedent, read instead of
    /// written.</summary>
    static async Task<long?> PendingRetireMediaIdOfAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long?>(
            "select pending_retire_media_id from station.ad_spot where id = @Id", new { Id = id });
    }

    // ---------------------------------------------------------------------
    // SwapRenderedMediaAsync stamps pending_retire_media_id whenever the post-commit flip does not
    // clear it itself in the same call
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapStampsThePendingMarkerWhenTheFlipFails(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long oldMediaId;
        long id;
        bool swapped;

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            // Given a ready spot airing a real, eligible old media row, and the post-commit flip that
            // would normally retire that row wired to fail (a transient library outage, PLAN's own
            // read of the "old flip throws after the swap" fact)...
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(oldMediaId);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);

            // When the re-render's own confirmAsync closure lands anyway...
            swapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapStillCommits()
            // The station-side transaction is unaffected by a library-side flip failing after it.
            => Assert.True(swapped);

        [Fact]
        public async Task ThePendingMarkerNamesTheOldMedia()
            => Assert.Equal(oldMediaId, await PendingRetireMediaIdOfAsync(db, id));

        [Fact]
        public async Task TheOldMediaStaysEligibleUntilARetryLandsIt()
            => Assert.True(await Harness.EligibleOfAsync(db, oldMediaId));
    }

    // ---------------------------------------------------------------------
    // ClearPendingRetireAsync — guarded on BOTH id and the exact stamped media id
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheGuardedClearDeclinesOnAMediaIdMismatch(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long oldMediaId;
        long id;
        bool clearedWrong;
        bool clearedRight;

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            // Given a swap whose old-media flip fails, so its pending marker survives past the call...
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(oldMediaId);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
            await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);

            // When a stale retry names the WRONG old media id (some other spot's own pending retire,
            // misrouted) it must never clear this row's marker...
            clearedWrong = await repo.ClearPendingRetireAsync(id, mediaId: 999_999, CancellationToken.None);

            // ...only the exact stamped pair does.
            clearedRight = await repo.ClearPendingRetireAsync(id, oldMediaId, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheMismatchedClearDeclines()
            => Assert.False(clearedWrong);

        [Fact]
        public void TheMatchingClearSucceeds()
            => Assert.True(clearedRight);

        [Fact]
        public async Task ThePendingMarkerIsGoneAfterTheMatchingClear()
            => Assert.Null(await PendingRetireMediaIdOfAsync(db, id));
    }

    // ---------------------------------------------------------------------
    // ListPendingRetiresAsync — every stamped row comes back; clearing one leaves the others
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListReturnsEveryStampedRowAndClearingOneLeavesTheOthers(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long firstOldMediaId;
        long secondOldMediaId;
        long firstId;
        long secondId;
        IReadOnlyList<PendingAdSpotRetire> listedBeforeClear = [];
        IReadOnlyList<PendingAdSpotRetire> listedAfterClear = [];

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            // Given two swaps, both with a failing old-media flip, so BOTH pending markers survive...
            firstOldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            secondOldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var firstNewMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            var secondNewMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(firstOldMediaId);
            catalogWriter.ThrowMediaIds.Add(secondOldMediaId);

            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            firstId = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: firstOldMediaId, renderVersion: 0);
            secondId = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: secondOldMediaId, renderVersion: 0);
            await repo.SwapRenderedMediaAsync(
                firstId, firstOldMediaId, firstNewMediaId, CurrentVersion, CancellationToken.None);
            await repo.SwapRenderedMediaAsync(
                secondId, secondOldMediaId, secondNewMediaId, CurrentVersion, CancellationToken.None);

            // When the drain lists pending retires, then clears just the first...
            listedBeforeClear = await repo.ListPendingRetiresAsync(CancellationToken.None);
            await repo.ClearPendingRetireAsync(firstId, firstOldMediaId, CancellationToken.None);
            listedAfterClear = await repo.ListPendingRetiresAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void BothStampedRowsAreListedBeforeEitherIsCleared()
        {
            Assert.Contains(new PendingAdSpotRetire(firstId, firstOldMediaId), listedBeforeClear);
            Assert.Contains(new PendingAdSpotRetire(secondId, secondOldMediaId), listedBeforeClear);
        }

        [Fact]
        public void OnlyTheClearedRowDropsOutAfterwards()
        {
            Assert.DoesNotContain(new PendingAdSpotRetire(firstId, firstOldMediaId), listedAfterClear);
            Assert.Contains(new PendingAdSpotRetire(secondId, secondOldMediaId), listedAfterClear);
        }
    }

    // ---------------------------------------------------------------------
    // The currently-referenced guard — never list (or hand back for flipping) a media id some OTHER
    // spot now names as its own current media_id
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAMediaIdCurrentlyReferencedByAnotherSpotClearsWithoutBeingListed(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long oldMediaId;
        long id;
        IReadOnlyList<PendingAdSpotRetire> listed = [];

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            // Given a swap whose old-media flip fails, so its pending marker survives...
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(oldMediaId);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
            await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);

            // ...and an operator re-points a SECOND, unrelated spot at that very old media id before
            // any retry ever lands...
            await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);

            // When the drain runs its own self-heal pass, then lists pending retires — two explicit
            // calls now (gh-#854), the caller's own responsibility each tick rather than a side effect
            // buried inside the list read itself...
            await repo.ClearReferencedPendingRetiresAsync(CancellationToken.None);
            listed = await repo.ListPendingRetiresAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheReReferencedRowIsNeverListed()
            => Assert.DoesNotContain(new PendingAdSpotRetire(id, oldMediaId), listed);

        [Fact]
        public async Task ThePendingMarkerItselfIsClearedInPlace()
            => Assert.Null(await PendingRetireMediaIdOfAsync(db, id));
    }
}

/// <summary>gh-#854 — <see cref="Gh854FeaturePendingRetireDrain"/>'s own facts, one marker over:
/// <c>pending_confirm_media_id</c> and <see cref="AdSpotRepository"/>'s own guarded read/clear surface
/// (<c>ListPendingConfirmsAsync</c>/<c>ClearPendingConfirmAsync</c>) over it, plus the shared
/// <c>IsReadyOnMediaAsync</c> guard the confirm flip (unlike the retire flip) checks before ever
/// attempting it.</summary>
public static class Gh854FeaturePendingConfirmDrain
{
    const int CurrentVersion = 1;

    static Task<long> SeedAuthoredMediaAsync(DatabaseFixture db, bool eligible) =>
        Harness.Repo(db).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: $"/authored/gh854-confirm-{Guid.NewGuid():N}.wav", eligible: eligible),
            CancellationToken.None);

    static async Task<long?> PendingConfirmMediaIdOfAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long?>(
            "select pending_confirm_media_id from station.ad_spot where id = @Id", new { Id = id });
    }

    /// <summary><see cref="Gh854FeaturePendingRetireDrain"/>'s own helper, read instead of written —
    /// <see cref="ScenarioTheSwapStampsBothMarkersWhenBothFlipsFail"/> asserts on both markers at once,
    /// so this class keeps its own copy rather than reaching into that other static class's private
    /// member.</summary>
    static async Task<long?> PendingRetireMediaIdOfAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long?>(
            "select pending_retire_media_id from station.ad_spot where id = @Id", new { Id = id });
    }

    // ---------------------------------------------------------------------
    // SwapRenderedMediaAsync stamps BOTH markers in the same guarded UPDATE that lands the swap —
    // proved here by making BOTH post-commit flips fail, so neither marker's own fast-path clear ever
    // fires.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSwapStampsBothMarkersWhenBothFlipsFail(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long oldMediaId;
        long newMediaId;
        long id;
        bool swapped;

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            // Given a ready spot, and BOTH post-commit flips wired to fail (a transient library
            // outage that hits every write, not just one)...
            oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(oldMediaId);
            catalogWriter.ThrowMediaIds.Add(newMediaId);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);

            swapped = await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwapStillCommits()
            => Assert.True(swapped);

        [Fact]
        public async Task ThePendingRetireMarkerNamesTheOldMedia()
            => Assert.Equal(oldMediaId, await PendingRetireMediaIdOfAsync(db, id));

        [Fact]
        public async Task ThePendingConfirmMarkerNamesTheNewMedia()
            => Assert.Equal(newMediaId, await PendingConfirmMediaIdOfAsync(db, id));
    }

    // ---------------------------------------------------------------------
    // ClearPendingConfirmAsync — guarded on BOTH id and the exact stamped media id
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheGuardedClearDeclinesOnAMediaIdMismatch(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long newMediaId;
        long id;
        bool clearedWrong;
        bool clearedRight;

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            // Given a swap whose new-media flip fails, so its pending marker survives past the call...
            var oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(newMediaId);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
            await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);

            // When a stale retry names the WRONG new media id, it must never clear this row's marker...
            clearedWrong = await repo.ClearPendingConfirmAsync(id, mediaId: 999_999, CancellationToken.None);

            // ...only the exact stamped pair does.
            clearedRight = await repo.ClearPendingConfirmAsync(id, newMediaId, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheMismatchedClearDeclines()
            => Assert.False(clearedWrong);

        [Fact]
        public void TheMatchingClearSucceeds()
            => Assert.True(clearedRight);

        [Fact]
        public async Task ThePendingMarkerIsGoneAfterTheMatchingClear()
            => Assert.Null(await PendingConfirmMediaIdOfAsync(db, id));
    }

    // ---------------------------------------------------------------------
    // ListPendingConfirmsAsync — every stamped row comes back; clearing one leaves the others
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListReturnsEveryStampedRowAndClearingOneLeavesTheOthers(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long firstNewMediaId;
        long secondNewMediaId;
        long firstId;
        long secondId;
        IReadOnlyList<PendingAdSpotConfirm> listedBeforeClear = [];
        IReadOnlyList<PendingAdSpotConfirm> listedAfterClear = [];

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            // Given two swaps, both with a failing new-media flip, so BOTH pending markers survive...
            var firstOldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            var secondOldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            firstNewMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            secondNewMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(firstNewMediaId);
            catalogWriter.ThrowMediaIds.Add(secondNewMediaId);

            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            firstId = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: firstOldMediaId, renderVersion: 0);
            secondId = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: secondOldMediaId, renderVersion: 0);
            await repo.SwapRenderedMediaAsync(
                firstId, firstOldMediaId, firstNewMediaId, CurrentVersion, CancellationToken.None);
            await repo.SwapRenderedMediaAsync(
                secondId, secondOldMediaId, secondNewMediaId, CurrentVersion, CancellationToken.None);

            // When the drain lists pending confirms, then clears just the first...
            listedBeforeClear = await repo.ListPendingConfirmsAsync(CancellationToken.None);
            await repo.ClearPendingConfirmAsync(firstId, firstNewMediaId, CancellationToken.None);
            listedAfterClear = await repo.ListPendingConfirmsAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void BothStampedRowsAreListedBeforeEitherIsCleared()
        {
            Assert.Contains(new PendingAdSpotConfirm(firstId, firstNewMediaId), listedBeforeClear);
            Assert.Contains(new PendingAdSpotConfirm(secondId, secondNewMediaId), listedBeforeClear);
        }

        [Fact]
        public void OnlyTheClearedRowDropsOutAfterwards()
        {
            Assert.DoesNotContain(new PendingAdSpotConfirm(firstId, firstNewMediaId), listedAfterClear);
            Assert.Contains(new PendingAdSpotConfirm(secondId, secondNewMediaId), listedAfterClear);
        }
    }

    // ---------------------------------------------------------------------
    // IsReadyOnMediaAsync — the shared guard the confirm flip checks before ever attempting it
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIsReadyOnMediaGuard(DatabaseFixture db) : IAsyncLifetime
    {
        readonly FakeAuthoredCatalogWriter catalogWriter = new(Harness.Repo(db));
        AdSpotRepository repo = null!;
        long newMediaId;
        long id;

        public async Task InitializeAsync()
        {
            await db.ResetAsync();
            await db.ResetAdsAndShowsAsync();
            repo = Harness.AdSpotRepoWithCatalogWriter(db, catalogWriter);

            var oldMediaId = await SeedAuthoredMediaAsync(db, eligible: true);
            newMediaId = await SeedAuthoredMediaAsync(db, eligible: false);
            catalogWriter.ThrowMediaIds.Add(newMediaId); // keep the swap's own inline confirm from ever landing
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            id = await Harness.SeedReadySpotAsync(repo, sponsorId, mediaId: oldMediaId, renderVersion: 0);
            await repo.SwapRenderedMediaAsync(
                id, oldMediaId, newMediaId, renderVersion: CurrentVersion, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TrueWhenTheSpotIsStillReadyOnTheStampedMedia()
            => Assert.True(await repo.IsReadyOnMediaAsync(id, newMediaId, CancellationToken.None));

        [Fact]
        public async Task FalseWhenTheMediaIdDoesNotMatch()
            => Assert.False(await repo.IsReadyOnMediaAsync(id, mediaId: 999_999, CancellationToken.None));

        [Fact]
        public async Task FalseOnceTheSpotIsRetired()
        {
            var current = await repo.GetByIdAsync(id, CancellationToken.None)
                ?? throw new InvalidOperationException("arrange: seeded spot went missing");
            var retired = await repo.RetireAsync(id, current.Version, CancellationToken.None);
            if (retired.Result != AdSpotWriteResult.Updated)
                throw new InvalidOperationException("arrange: retiring the spot did not apply");

            Assert.False(await repo.IsReadyOnMediaAsync(id, newMediaId, CancellationToken.None));
        }
    }
}
