// STORY-388 — The Ads library seeds itself once, idempotently (F159.1 · PLAN T396)
//
// BDD specification — xUnit, pure unit tests over fakes (the SafeLoopSeeder/Story080 precedent:
// AdsLibrarySeeder is exercised in-process against ILibraryRepository/IAdminLibraryWrite fakes, no
// live Postgres). Deliberate choice over a live-Postgres integration fact in
// GenWave.MediaLibrary.Tests: GenWave.Ads.Tests carries no DB fixture BY DESIGN (the project itself
// never references Npgsql/Dapper, L2 confinement), and Story080_SafeSeedOnBoot.cs's own precedent —
// the seeder this class's shape mirrors — proves its in-process behavior the SAME way, leaving the
// real-stack proof (a real Postgres row, a real boot) operator-gated rather than built here. This
// seeder has no render/overlay step for a live proof to add value over the fakes anyway (its own
// class remarks explain why "the library's own presence IS the marker" makes that safe).

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAdsLibraryBootSeed
{
    static AdsLibrarySeeder Build(FakeAdsLibraryStore libraries, string libraryName = "ads") =>
        new(libraries, libraries, new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = libraryName }),
            NullLogger<AdsLibrarySeeder>.Instance);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioFirstBoot
    {
        [Fact]
        public async Task NoExistingAdsLibraryIsCreated()
        {
            var libraries = new FakeAdsLibraryStore();
            var seeder = Build(libraries);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(AdsLibrarySeedOutcome.Seeded, outcome);
            Assert.Equal(1, libraries.CreateCallCount);
            var all = await libraries.GetAllWithMediaCountAsync(CancellationToken.None);
            Assert.Contains(all, l => l.Name == "ads");
        }

        [Fact]
        public async Task TheCreatedLibraryUsesTheConfiguredName()
        {
            var libraries = new FakeAdsLibraryStore();
            var seeder = Build(libraries, libraryName: "house-ads");

            await seeder.SeedAsync(CancellationToken.None);

            var all = await libraries.GetAllWithMediaCountAsync(CancellationToken.None);
            Assert.Contains(all, l => l.Name == "house-ads");
        }
    }

    public sealed class ScenarioIdempotency
    {
        [Fact]
        public async Task AnExistingAdsLibraryIsReusedNotRecreated()
        {
            var libraries = new FakeAdsLibraryStore();
            libraries.AddExisting("ads", mediaCount: 3);
            var seeder = Build(libraries);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(AdsLibrarySeedOutcome.AlreadySeeded, outcome);
            Assert.Equal(0, libraries.CreateCallCount);
        }

        [Fact]
        public async Task CallingSeedTwiceInARowNeverProducesASecondLibrary()
        {
            var libraries = new FakeAdsLibraryStore();
            var seeder = Build(libraries);

            var first = await seeder.SeedAsync(CancellationToken.None);
            var second = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(AdsLibrarySeedOutcome.Seeded, first);
            Assert.Equal(AdsLibrarySeedOutcome.AlreadySeeded, second);
            Assert.Equal(1, libraries.CreateCallCount);
            var all = await libraries.GetAllWithMediaCountAsync(CancellationToken.None);
            Assert.Single(all, l => l.Name == "ads");
        }

        [Fact]
        public async Task ARaceThatCreatesTheLibraryConcurrentlyIsReusedNotDoubled()
        {
            // Simulates an operator POST /api/libraries (or a concurrent boot on another replica)
            //   racing this create — CreateAsync returns NameConflict; the seeder re-looks-up and
            //   reuses rather than failing (mirrors SafeLoopSeeder.EnsureSafeLibraryAsync's identical
            //   race handling).
            var libraries = new RacingLibraryStore();
            var seeder = new AdsLibrarySeeder(
                libraries, libraries, new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = "ads" }),
                NullLogger<AdsLibrarySeeder>.Instance);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(AdsLibrarySeedOutcome.AlreadySeeded, outcome);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioFailureDegrades
    {
        [Fact]
        public async Task ARepositoryFaultDegradesToFailedNeverThrows()
        {
            var throwing = new ThrowingLibraryStore();
            var seeder = new AdsLibrarySeeder(
                throwing, throwing, new FakeOptionsMonitor<AdsOptions>(new AdsOptions()),
                NullLogger<AdsLibrarySeeder>.Instance);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(AdsLibrarySeedOutcome.Failed, outcome);
        }
    }

    // ---------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------

    /// <summary>Always reports a name conflict on the FIRST create, then reports the library present
    /// on any subsequent lookup — the T396 race scenario above.</summary>
    sealed class RacingLibraryStore : ILibraryRepository, IAdminLibraryWrite
    {
        bool createAttempted;

        public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LibraryInfo>>([]);

        public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LibraryAdminInfo>>(
                createAttempted ? [new LibraryAdminInfo(1, "ads", 0)] : []);

        public Task<LibraryAdminInfo?> GetByNameAsync(string name, CancellationToken ct) =>
            Task.FromResult<LibraryAdminInfo?>(createAttempted ? new LibraryAdminInfo(1, "ads", 0) : null);

        public Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct)
        {
            createAttempted = true;
            return Task.FromResult<LibraryWriteResult>(new LibraryWriteResult.NameConflict());
        }

        public Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct) =>
            throw new NotSupportedException("Not used by the ads library seed.");

        public Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct) =>
            throw new NotSupportedException("Not used by the ads library seed.");
    }
}
