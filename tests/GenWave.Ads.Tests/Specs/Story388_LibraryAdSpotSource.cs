// STORY-388 — The library floor picks uniformly and never repeats inside the window (F158.5 · PLAN
// T396)
//
// BDD specification — xUnit, pure unit tests over fakes (no live stack). AC4 (anti-repeat) was
// originally seeded as a pending fact in GenWave.Orchestration.Tests/Specs/Story388_AdCadenceAndPipeline.cs
// under ScenarioAntiRepeat — MOVED here at T396: the ring is LibraryAdSpotSource's own state, not the
// pipeline's, and Orchestration.Tests cannot reference GenWave.Ads. The catalog-side predicate proof
// (ready+measurable+eligible+not never_play+imaging_kind='ad', the explicit-posture exclusion) is a
// SQL-planner fact and lives in GenWave.MediaLibrary.Tests/Specs/Story387_ImagingNeverAirsAsMusic.cs
// instead (Category=Integration, live Postgres) — this file only proves LibraryAdSpotSource's OWN
// logic (library-name resolution, the ring, the SegmentKind stamp) against a fake IMediaCatalog, per
// F158.5's own "the feeder precedent" ring shape.

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureLibraryAdSpotSource
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    const string AdsLibraryName = "ads";

    static (LibraryAdSpotSource Source, FakeAdSpotCatalog Catalog, FakeAdsLibraryStore Libraries,
        FakeOptionsMonitor<AdSpotAntiRepeatOptions> AntiRepeat) Build(int antiRepeatWindow = 5)
    {
        var catalog = new FakeAdSpotCatalog();
        var libraries = new FakeAdsLibraryStore();
        libraries.AddExisting(AdsLibraryName);

        var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = AdsLibraryName });
        var antiRepeat = new FakeOptionsMonitor<AdSpotAntiRepeatOptions>(
            new AdSpotAntiRepeatOptions { AntiRepeatWindow = antiRepeatWindow });

        var source = new LibraryAdSpotSource(catalog, libraries, adsOptions, antiRepeat);
        return (source, catalog, libraries, antiRepeat);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAntiRepeat
    {
        [Fact]
        public async Task NoSpotRepeatsInsideTheWindow()
        {
            // AntiRepeatWindow=5, 6 ready spots, 6 consecutive vends: all distinct (F158.5's own
            //   "the feeder precedent" ring — mirrors PlayoutFeeder.Remember's live-trimmed shape).
            var (source, catalog, _, _) = Build(antiRepeatWindow: 5);
            for (var i = 1; i <= 6; i++)
                catalog.AddReady($"spot-{i}");

            var vended = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                var spot = await source.GetNextSpotAsync(CancellationToken.None);
                Assert.NotNull(spot);
                vended.Add(spot.MediaId);
            }

            Assert.Equal(6, vended.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public async Task AShrunkWindowCapsTheRingOnTheNextRead()
        {
            // The READ-side cap (LibraryAdSpotSource.SnapshotRing), not the write-side trim
            //   (Remember): with only one spot in the pool and that id already in the ring, NOTHING
            //   ever vends again to trigger a write-time trim — a shrunk window must free room on the
            //   very next READ instead, or this source stays wedged forever regardless of later
            //   config edits.
            var (source, catalog, _, antiRepeat) = Build(antiRepeatWindow: 5);
            catalog.AddReady("only-spot");

            var first = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.NotNull(first);
            Assert.Equal("only-spot", first.MediaId);

            // With only one spot in the pool, the very next vend is excluded by the ring and returns
            //   null — proving the exclusion took effect at all — until the window shrinks to 0.
            var second = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.Null(second);

            antiRepeat.CurrentValue = new AdSpotAntiRepeatOptions { AntiRepeatWindow = 0 };

            var third = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.NotNull(third);
            Assert.Equal("only-spot", third.MediaId);
        }
    }

    public sealed class ScenarioTheVendedItem
    {
        [Fact]
        public async Task CarriesTheAdSegmentKind()
        {
            var (source, catalog, _, _) = Build();
            catalog.AddReady("spot-1");

            var spot = await source.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(spot);
            Assert.Equal(SegmentKind.Ad, spot.SegmentKind);
        }

        [Fact]
        public async Task ResolvesTheLibraryByTheConfiguredName()
        {
            var catalog = new FakeAdSpotCatalog().AddReady("spot-1");
            var libraries = new FakeAdsLibraryStore();
            var libraryId = libraries.AddExisting("house-ads");
            var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = "house-ads" });
            var antiRepeat = new FakeOptionsMonitor<AdSpotAntiRepeatOptions>(new AdSpotAntiRepeatOptions());
            var source = new LibraryAdSpotSource(catalog, libraries, adsOptions, antiRepeat);

            var spot = await source.GetNextSpotAsync(CancellationToken.None);

            Assert.NotNull(spot);
            Assert.Equal(1, catalog.CallCount);
            Assert.NotNull(catalog.LastScope);
            Assert.Equal([libraryId], catalog.LastScope.LibraryIds);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoAdsLibrary
    {
        [Fact]
        public async Task NoLibraryNamedAsConfiguredMeansNoSpotEverAskedFor()
        {
            // F158.1: null is always a legal answer — an unseeded/renamed-away ads library is no
            //   dead-air excuse, and the catalog is never even queried (default-deny at the source).
            var catalog = new FakeAdSpotCatalog().AddReady("spot-1");
            var libraries = new FakeAdsLibraryStore(); // No library named "ads" exists.
            var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions());
            var antiRepeat = new FakeOptionsMonitor<AdSpotAntiRepeatOptions>(new AdSpotAntiRepeatOptions());
            var source = new LibraryAdSpotSource(catalog, libraries, adsOptions, antiRepeat);

            var spot = await source.GetNextSpotAsync(CancellationToken.None);

            Assert.Null(spot);
            Assert.Equal(0, catalog.CallCount);
        }

        [Fact]
        public async Task AnEmptyPoolAnswersNull()
        {
            var (source, _, _, _) = Build(); // No AddReady call — the pool is empty.

            var spot = await source.GetNextSpotAsync(CancellationToken.None);

            Assert.Null(spot);
        }
    }
}
