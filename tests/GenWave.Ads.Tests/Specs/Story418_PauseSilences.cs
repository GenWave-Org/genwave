// STORY-418 — Pausing a sponsor silences their spots on the very next pick (SPEC F173.1/F173.2 · PLAN T439, T440)

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;

namespace GenWave.Ads.Tests.Specs;

public static class FeaturePausingASponsorSilencesTheirSpotsOnTheVeryNextPick
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    const string AdsLibraryName = "ads";

    static (LibraryAdSpotSource Source, FakeAdSpotCatalog Catalog, FakeAiringExclusionsStore Store) Build(
        int antiRepeatWindow = 5)
    {
        var catalog = new FakeAdSpotCatalog();
        var libraries = new FakeAdsLibraryStore();
        libraries.AddExisting(AdsLibraryName);
        var store = new FakeAiringExclusionsStore();

        var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = AdsLibraryName });
        var antiRepeat = new FakeOptionsMonitor<AdSpotAntiRepeatOptions>(
            new AdSpotAntiRepeatOptions { AntiRepeatWindow = antiRepeatWindow });

        var source = new LibraryAdSpotSource(
            catalog, libraries, adsOptions, antiRepeat, store, new NoOpLogger<LibraryAdSpotSource>());
        return (source, catalog, store);
    }

    // The paused-sponsor SQL predicate itself (AC1) cannot be proven against a fake store — it lives
    // in GenWave.MediaLibrary.Tests.Specs.Story418_AiringExclusionsSql, over real Postgres.

    // ---------------------------------------------------------------------
    // AC2 — one pick after pause is silence
    // ---------------------------------------------------------------------

    public sealed class ScenarioOnePickAfterPauseIsSilence
    {
        [Fact]
        public async Task GetNextSpotNeverReturnsThePausedSponsorsMedia()
        {
            // Given the store reports the paused sponsor's media (100) excluded, and another
            // sponsor's ready media (101) is also in the pool...
            var (source, catalog, store) = Build();
            catalog.AddReady("100").AddReady("101");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).SetPaused(sponsorId: 42, paused: true);

            // When the very next pick runs...
            var spot = await source.GetNextSpotAsync(CancellationToken.None);

            // Then it is never the paused sponsor's media — the other sponsor's spot airs instead.
            Assert.NotNull(spot);
            Assert.Equal("101", spot.MediaId);
        }

        [Fact]
        public async Task ANullPickReplacesThePausedSponsorsMediaWhenNothingElseIsAvailable()
        {
            // Given the paused sponsor's media (100) is the ONLY candidate in the pool...
            var (source, catalog, store) = Build();
            catalog.AddReady("100");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).SetPaused(sponsorId: 42, paused: true);

            // When the next pick runs...
            var spot = await source.GetNextSpotAsync(CancellationToken.None);

            // Then it is null (STORY-418 AC2's own "or is null if 100 was the only candidate") — a
            // paused sponsor's own media is excluded on the relaxed pick too, so relaxing never
            // recovers it.
            Assert.Null(spot);
        }
    }

    // ---------------------------------------------------------------------
    // AC3 — refill skips paused sponsors' briefs (T440)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRefillSkipsPausedSponsorsBriefs
    {
        static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task NoSpotIsCreatedFromAPausedSponsorsBriefs()
        {
            // Given zero ready spots and one enabled brief whose ONLY sponsor is paused —
            // FakeAdBriefStore.SampleEnabledAsync's own paused-sponsor filter (wired to
            // FakeSponsorStore.IsPaused by AdSpotWorkerHarness.Build, mirroring
            // AdBriefRepository.SampleEnabledAsync's real EXISTS/"and not s.paused" predicate, SPEC
            // F173.2, PLAN T440) has nothing else to return — and the LLM handler is one that WOULD
            // serve a valid script if ever reached, so a red here can only mean the sample itself let
            // the paused sponsor's brief through...
            var logger = new CapturingLogger<AdSpotWorker>();
            var handler = AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply);
            var harness = AdSpotWorkerHarness.Build(Now, llmHandler: handler, workerLogger: logger);
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");
            harness.Sponsors.Pause(harness.Briefs.SponsorIdsByBrand["Cravin's Diner"]);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then no spot is created — the paused sponsor's own brief was never sampled into a
            // generation attempt — and RefillIfNeededAsync's own no-brief-sampled branch fired instead,
            // the discriminating line that proves WHY: the sample returned null, not some unrelated
            // early-out.
            Assert.Equal(0, harness.Store.CreateCallCount);
            Assert.Contains(
                logger.Messages, m => m.Contains("but no enabled brief to sample from", StringComparison.Ordinal));
        }

        [Fact]
        public async Task NoLlmCompletionIsLogged()
        {
            // Given the SAME setup, with the SAME would-succeed LLM handler wired in...
            var handler = AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply);
            var harness = AdSpotWorkerHarness.Build(Now, llmHandler: handler);
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");
            harness.Sponsors.Pause(harness.Briefs.SponsorIdsByBrand["Cravin's Diner"]);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the handler never received a single request — the sample returned null before
            // GenerateOneAsync could ever build one, so no spend on the paused sponsor's own brief
            // (AC3): a handler that WOULD have answered proves the LLM was skipped by the sample, not
            // by an unrelated fault swallowed on the way there.
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task TheGeneratingLineIsNotLoggedWhenNoBriefIsSampled()
        {
            // Given the SAME setup, with the SAME would-succeed LLM handler wired in and the tick's
            // own logger captured...
            var logger = new CapturingLogger<AdSpotWorker>();
            var handler = AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply);
            var harness = AdSpotWorkerHarness.Build(Now, llmHandler: handler, workerLogger: logger);
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");
            harness.Sponsors.Pause(harness.Briefs.SponsorIdsByBrand["Cravin's Diner"]);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then STORY-420 AC1's own "generating one" line never fires here — it belongs to the
            // OTHER branch of RefillIfNeededAsync (PLAN T440), the one only reached once a brief was
            // actually sampled, so its absence pins the placement: the line sits strictly after the
            // no-brief-sampled early-out this scenario's own sample (correctly) took instead.
            Assert.DoesNotContain(
                logger.Messages, m => m.Contains("Ad stock below target: generating one", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // AC4 — resume restores airing within one pick, no re-render
    // ---------------------------------------------------------------------

    public sealed class ScenarioResumeRestoresAiringWithinOnePickNoReRender
    {
        [Fact]
        public async Task TheMediaIsEligibleAgainOnTheNextPick()
        {
            // Given a paused sponsor's media (100) excluded on the first pick...
            var (source, catalog, store) = Build();
            catalog.AddReady("100");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).SetPaused(sponsorId: 42, paused: true);
            var whilePaused = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.Null(whilePaused);

            // When the sponsor is unpaused (the store simply stops reporting their media)...
            store.SetPaused(sponsorId: 42, paused: false);

            // Then the very next pick returns it — no re-render, the same media id as before.
            var afterResume = await source.GetNextSpotAsync(CancellationToken.None);
            Assert.NotNull(afterResume);
            Assert.Equal("100", afterResume.MediaId);
        }

        [Fact]
        public async Task TheSpotStateIsStillReady()
        {
            // Given the same pause/resume sequence as the fact above...
            var (source, catalog, store) = Build();
            catalog.AddReady("100");
            store.AddReadySpot(sponsorId: 42, mediaId: 100).SetPaused(sponsorId: 42, paused: true);
            await source.GetNextSpotAsync(CancellationToken.None);
            store.SetPaused(sponsorId: 42, paused: false);

            // When the next pick runs...
            await source.GetNextSpotAsync(CancellationToken.None);

            // Then the source never called anything on the store OTHER than ListAiringExclusionsAsync
            // — every other IAdSpotStore member on this fake throws NotSupportedException if reached,
            // so simply completing without an exception already proves no state transition was
            // attempted; the call log below confirms it was ListAiringExclusionsAsync doing all the
            // work: the first pick's strict call comes up empty (100 is the only candidate and it is
            // excluded), so its own relaxation call runs too (2 calls) — then, once unpaused, the
            // second pick's strict call alone succeeds (1 more), for 3 total.
            Assert.Equal(3, store.Calls.Count);
        }
    }
}
