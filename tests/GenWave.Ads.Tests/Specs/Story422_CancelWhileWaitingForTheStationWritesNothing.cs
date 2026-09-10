// STORY-422 — a cancel that lands while a job is PARKED waiting for the station (InFlight, PLAN T441
// ruling) writes its clear exactly once, and that write is CancelAsync's own — never a second one from
// the parked job's own token-cancel catch once its wait loop finally observes the cancellation. The
// probabilistic sibling in Story422_AdSpotJobs only pins this sometimes (the wait loop's own poll
// interval means a run can slip past InFlight before the cancel lands); this fact forces the race by
// holding InFlight true until IsWaitingForStation is observed true first.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using GenWave.Tts;
using Microsoft.Extensions.Configuration;

public static class FeatureCancelWhileWaitingForTheStationWritesNothing
{
    public sealed class ScenarioAJobParkedOnTheStationIsCancelled
    {
        [Fact]
        public async Task TheCancelledJobsOnlyClearIsCancelAsyncsOwn()
        {
            const long aSpotId = 1;
            const long cSpotId = 2;
            const long sponsorId = 1;

            var store = new FakeAdSpotJobStore();
            store.Seed(aSpotId, sponsorId, "A deal so good it's almost illegal.");
            store.Seed(cSpotId, sponsorId, "Come on down.");
            var sponsors = new FakeSponsorStore(_ => "Cravin's Diner");
            var handler = AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply);
            var llmOptions = new FakeOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 300,
            });
            var recorder = new LlmCallRecorder(new LlmCallRing(llmOptions), new LlmCallCauseCounters(TimeProvider.System));
            var scriptWriter = new AdScriptWriter(
                new SingleHandlerHttpClientFactory(handler), llmOptions, recorder, new FakeDegradationModeReader(),
                new NoOpLogger<AdScriptWriter>(), TimeProvider.System);
            var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions());
            var gate = new FakeOnAirRenderSignal { InFlight = true };

            // renderService/stamper/configuration only reach RunPreviewAsync (PLAN T442) — this
            // scenario only ever enqueues "write" jobs, so these stay wired but unreached.
            var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", "Test Station", "station_voice"));
            var libraries = new FakeAdsLibraryStore();
            var locatorRoots = new AdSpotLocatorRoots("/media", "/authored");
            var renderService = new AdRenderService(
                new FakeCastSegmentAuthor(), store, new FakeAdminMediaLookup(), libraries, stationIdentity, adsOptions,
                locatorRoots, new NoOpLogger<AdRenderService>());
            var stamper = new AdSpotStamper(
                store, new FakeAdBedPool(), libraries, stationIdentity, adsOptions, new NoOpLogger<AdSpotStamper>());
            var configuration = new ConfigurationBuilder().Build();

            var service = new AdSpotJobService(
                store, sponsors, scriptWriter, renderService, stamper, new FakePatterDurationEstimator(),
                new FakeAudiencePostureProvider(), gate, adsOptions, llmOptions, configuration, TimeProvider.System,
                new NoOpLogger<AdSpotJobService>());

            await service.StartAsync(CancellationToken.None);
            try
            {
                var enqueued = await service.TryEnqueueAsync(aSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, enqueued);

                // Wait until the job's own wait loop has actually parked on the station — the flag
                // asserted first, never an unbounded await, so a wording/timing drift fails this fact
                // outright instead of hanging it.
                var waiting = await JobWait.TryWaitUntilAsync(
                    () => service.IsWaitingForStation(aSpotId), TimeSpan.FromSeconds(5));
                Assert.True(waiting, "the job never reached its own wait-for-station loop");

                await service.CancelAsync(aSpotId, CancellationToken.None);

                // Only now does the station clear — after the cancel has already landed, so the parked
                // job's own wait loop observes cancellation on its next poll rather than InFlight
                // turning false.
                gate.InFlight = false;

                var sentinelEnqueued = await service.TryEnqueueAsync(cSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, sentinelEnqueued);

                var sentinelRan = await JobWait.TryWaitUntilAsync(
                    () => store.ClearJobCallCount(cSpotId) == 1, TimeSpan.FromSeconds(5));
                Assert.True(sentinelRan, "the sentinel job C never finished, so the loop never got past A");

                // The claim itself: A's own stamp was cleared exactly once — by CancelAsync, never by
                // a second clear from the parked job's own token-cancel catch once it woke up.
                Assert.Equal(1, store.ClearJobCallCount(aSpotId));

                var aSpot = await store.GetByIdAsync(aSpotId, CancellationToken.None);
                Assert.Null(aSpot?.JobKind);
                Assert.Null(aSpot?.JobError);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                service.Dispose();
            }
        }
    }
}
