// STORY-419 — Anti-repeat is by sponsor, with a one-sponsor relaxation (SPEC F173.3 · PLAN T439)

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureAntiRepeatIsBySponsorWithAOneSponsorRelaxation
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    const string AdsLibraryName = "ads";

    static (LibraryAdSpotSource Source, FakeAdSpotCatalog Catalog, FakeAiringExclusionsStore Store,
        CapturingLogger<LibraryAdSpotSource> Logger) Build(int antiRepeatWindow)
    {
        var catalog = new FakeAdSpotCatalog();
        var libraries = new FakeAdsLibraryStore();
        libraries.AddExisting(AdsLibraryName);
        var store = new FakeAiringExclusionsStore();
        var logger = new CapturingLogger<LibraryAdSpotSource>();

        var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = AdsLibraryName });
        var antiRepeat = new FakeOptionsMonitor<AdSpotAntiRepeatOptions>(
            new AdSpotAntiRepeatOptions { AntiRepeatWindow = antiRepeatWindow });

        var source = new LibraryAdSpotSource(catalog, libraries, adsOptions, antiRepeat, store, logger);
        return (source, catalog, store, logger);
    }

    const string RelaxationMessage = "Ad anti-repeat relaxed: one sponsor in rotation";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheSameSponsorNeverAirsTwiceInARow
    {
        [Fact]
        public async Task TheSecondPickReturnsTheOtherSponsor()
        {
            // Given AntiRepeatWindow=1 and sponsors 42 and 99, each with one ready spot...
            var (source, catalog, store, _) = Build(antiRepeatWindow: 1);
            catalog.AddReady("100").AddReady("200");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).AddReadySpot(sponsorId: 99, mediaId: 200);

            // When two consecutive picks run (the first returns sponsor 42's spot, deterministic pool
            // order)...
            var first = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.NotNull(first);
            Assert.Equal("100", first.MediaId);

            // Then the second pick returns sponsor 99's spot — 42 is excluded by the one-deep window.
            var second = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.NotNull(second);
            Assert.Equal("200", second.MediaId);
        }
    }

    public sealed class ScenarioTheWindowIsHonouredUpToN
    {
        [Fact]
        public async Task TheThirdPickReturnsTheThirdSponsor()
        {
            // Given AntiRepeatWindow=2 and sponsors 42/99/7, each with one ready spot...
            var (source, catalog, store, _) = Build(antiRepeatWindow: 2);
            catalog.AddReady("100").AddReady("200").AddReady("300");
            store.AddReadySpot(sponsorId: 42, mediaId: 100)
                .AddReadySpot(sponsorId: 99, mediaId: 200)
                .AddReadySpot(sponsorId: 7, mediaId: 300);

            // When the last two picks were 42 then 99...
            var pick1 = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.Equal("100", pick1?.MediaId);
            var pick2 = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.Equal("200", pick2?.MediaId);

            // Then the next pick's spot belongs to sponsor 7 — neither 42 nor 99, both still inside
            // the two-deep window.
            var pick3 = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.NotNull(pick3);
            Assert.Equal("300", pick3.MediaId);
        }
    }

    public sealed class ScenarioOneSponsorRelaxationLogsAndAirs
    {
        /// <summary>Runs AntiRepeatWindow=3 with one unpaused sponsor (two ready spots) through three
        /// prior picks — every one of them the same sponsor, since none other exists — then returns
        /// the FOURTH pick's own result alongside the logger, with the entry count captured
        /// immediately before that fourth pick so both facts below can isolate ITS OWN relaxation line
        /// from the ones the three prior (necessarily also relaxed) picks already logged.</summary>
        static async Task<(MediaItem? Spot, CapturingLogger<LibraryAdSpotSource> Logger,
            int EntriesBeforeTestedPick)> RunAsync()
        {
            var (source, catalog, store, logger) = Build(antiRepeatWindow: 3);
            catalog.AddReady("100").AddReady("101");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).AddReadySpot(sponsorId: 42, mediaId: 101);

            for (var i = 0; i < 3; i++)
                await source.GetNextSpotAsync(CancellationToken.None);

            var entriesBefore = logger.Entries.Count;
            var spot = await source.GetNextSpotAsync(CancellationToken.None);
            return (spot, logger, entriesBefore);
        }

        [Fact]
        public async Task ThePickReturnsTheOnlySponsorsSpot()
        {
            // When the fourth pick runs (the last three were all sponsor 42, the only sponsor there
            // is)...
            var (spot, _, _) = await RunAsync();

            // Then it returns one of sponsor 42's own spots — never null, only sponsor 42 exists.
            Assert.NotNull(spot);
            Assert.Contains(spot.MediaId, new[] { "100", "101" });
        }

        [Fact]
        public async Task TheRelaxationLineIsLoggedExactlyOnceAtInformation()
        {
            // When the fourth pick runs...
            var (_, logger, entriesBefore) = await RunAsync();

            // Then exactly one NEW Information entry carries the exact relaxation message — isolated
            // from the (also relaxed) three prior picks by the before/after entry-count delta.
            var newEntries = logger.Entries.Skip(entriesBefore).ToList();
            var relaxationEntries = newEntries.Where(
                e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information
                    && e.Message == RelaxationMessage);
            Assert.Single(relaxationEntries);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAPausedSponsorIsNeverRelaxedIntoAiring
    {
        [Fact]
        public async Task ThePickReturnsNull()
        {
            // Given AntiRepeatWindow=3 and the only sponsor with any ready spots is paused (99 is
            // exhausted — it has no ready spots at all)...
            var (source, catalog, store, _) = Build(antiRepeatWindow: 3);
            catalog.AddReady("100");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).SetPaused(sponsorId: 42, paused: true);

            // When the next pick runs...
            var spot = await source.GetNextSpotAsync(CancellationToken.None);

            // Then it is null — the paused sponsor's own media is excluded on BOTH the strict AND the
            // relaxed call, so there is nothing left to relax into.
            Assert.Null(spot);
        }

        [Fact]
        public async Task NoRelaxationLineAppears()
        {
            // Given the same paused-only setup as the fact above...
            var (source, catalog, store, logger) = Build(antiRepeatWindow: 3);
            catalog.AddReady("100");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).SetPaused(sponsorId: 42, paused: true);

            // When the next pick runs...
            await source.GetNextSpotAsync(CancellationToken.None);

            // Then no relaxation line was ever logged — a relaxed pick that itself comes back null
            // logs nothing.
            Assert.DoesNotContain(logger.Entries, e => e.Message == RelaxationMessage);
        }
    }

    // ---------------------------------------------------------------------
    // The seam's own call shape (not a STORY-419 AC, but the mechanism both scenarios above rest on)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheStrictPickHandsTheStoreTheLiveWindowAndTheRing
    {
        [Fact]
        public async Task EachStrictCallCarriesTheLiveWindowAndTheRingSoFar()
        {
            // Given AntiRepeatWindow=2 and two sponsors, one ready spot each...
            var (source, catalog, store, _) = Build(antiRepeatWindow: 2);
            catalog.AddReady("100").AddReady("200");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).AddReadySpot(sponsorId: 99, mediaId: 200);

            // When the first pick runs (empty ring)...
            await source.GetNextSpotAsync(CancellationToken.None);

            // Then the store recorded that call with an empty recent list and the live window.
            Assert.Empty(store.Calls[0].Recent);
            Assert.Equal(2, store.Calls[0].Window);

            // When the second pick runs...
            await source.GetNextSpotAsync(CancellationToken.None);

            // Then the store recorded THAT call with 100 (the first vend) as the sole recent entry,
            // still window=2.
            Assert.Equal([100], store.Calls[1].Recent);
            Assert.Equal(2, store.Calls[1].Window);
        }
    }

    public sealed class ScenarioTheRelaxedPicksExcludeListIsThePausedOnlySet
    {
        [Fact]
        public async Task TheCatalogSeesOnlyThePausedSponsorsMediaOnTheRelaxedPick()
        {
            // Given AntiRepeatWindow=3, one active sponsor (42) with two ready spots, and a SEPARATE
            // paused sponsor (7) with one ready spot...
            var (source, catalog, store, _) = Build(antiRepeatWindow: 3);
            catalog.AddReady("100").AddReady("101").AddReady("300");
            store.AddReadySpot(sponsorId: 42, mediaId: 100)
                .AddReadySpot(sponsorId: 42, mediaId: 101)
                .AddReadySpot(sponsorId: 7, mediaId: 300)
                .SetPaused(sponsorId: 7, paused: true);

            // When the first pick runs (excludes only the paused sponsor's 300, picks 42's 100) and
            // the second pick runs (42 is now "recent," so BOTH of 42's own spots are excluded by the
            // strict call, forcing relaxation)...
            var first = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.Equal("100", first?.MediaId);
            await source.GetNextSpotAsync(CancellationToken.None);

            // Then the relaxed pick's own exclude list, as handed to the catalog, is EXACTLY the
            // paused sponsor's media (300) — the ring plays no part in a relaxed pick, so 42's own
            // spots (100, 101) are absent from it even though 100 was just vended.
            Assert.Equal(["300"], catalog.LastExcludeIds);

            // And the relaxed call itself (the LAST call recorded) carried an EMPTY recent list and
            // window=0 — paused-only is structural (PLAN T439 ruling: an empty list is passed rather
            // than the ring), not merely a side effect of the store honouring window=0 against
            // whatever list it was handed.
            var relaxedCall = store.Calls[^1];
            Assert.Empty(relaxedCall.Recent);
            Assert.Equal(0, relaxedCall.Window);
        }
    }

    public sealed class ScenarioARelaxedVendEntersTheRingLikeAnyOther
    {
        [Fact]
        public async Task ARelaxedVendEntersTheRingLikeAnyOther()
        {
            // Given AntiRepeatWindow=3 and ONE sponsor (42) with two ready spots — the first pick is
            // strict (vends 100, the pool's first entry) and the second pick relaxes: both of 42's own
            // spots are now ring-excluded by the strict call, so RelaxedPickAsync's paused-only set
            // (nobody is paused here) leaves the catalog's deterministic first-unexcluded rule to vend
            // 100 again.
            var (source, catalog, store, _) = Build(antiRepeatWindow: 3);
            catalog.AddReady("100").AddReady("101");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).AddReadySpot(sponsorId: 42, mediaId: 101);

            var first = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.Equal("100", first?.MediaId);
            var second = await source.GetNextSpotAsync(CancellationToken.None); // the relaxed vend
            Assert.Equal("100", second?.MediaId);

            // When a third pick runs, its own strict call snapshots whatever the ring holds by then...
            await source.GetNextSpotAsync(CancellationToken.None);

            // Then that call — the LAST one the store recorded with a non-zero window (a relaxed call
            // always passes window=0, so this filter isolates strict calls only) — carries TWO ring
            // entries, not one. A relaxed vend's own media id is ALWAYS already ring-excluded going in
            // (that is why relaxation ran at all), so its mere PRESENCE in the ring could never
            // discriminate whether the second pick's own `Remember` actually ran — 100 would already be
            // there from the first pick regardless. Only the ring's LENGTH tells the two apart: a
            // skipped `Remember` on the relaxed path leaves the ring at its pre-relaxation size (one
            // entry), while a real `Remember` grows it to two. Length is unaffected by which end
            // `SnapshotRing` reverses from, so this fact pins `Remember` alone, not `SnapshotRing`'s own
            // ordering (PLAN T439 ruling).
            var lastStrictCall = store.Calls.Last(call => call.Window > 0);
            Assert.Equal(2, lastStrictCall.Recent.Count);
        }
    }
}
