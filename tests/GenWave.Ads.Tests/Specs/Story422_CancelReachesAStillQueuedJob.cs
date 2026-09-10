// STORY-422 AC6 — CancelAsync reaches a job that is still QUEUED, not only one already running
// (AdSpotJobService.RunOneAsync's own skip guard, PLAN T441 ruling): once the consumer loop dequeues a
// spot whose own token CancelAsync already removed, it must be skipped outright rather than run with a
// freshly minted token — its stamp was already cleared by the cancel itself, and it must never reach
// the writer at all.

namespace GenWave.Ads.Tests.Specs;

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Tts;

public static class FeatureCancelReachesAStillQueuedJob
{
    /// <summary>The same arrival-order request-body capture
    /// <c>Story428_TheWorkerHandsTheWriterTheSponsorsFacts</c> (this project's own Specs folder) uses —
    /// every outbound completion request's own body, in arrival order, so a fact can check WHICH
    /// sponsor's own name a request actually carried rather than merely counting how many requests
    /// fired.</summary>
    static (FakeHttpMessageHandler Handler, List<string> RequestBodies) CapturingHandler(string reply)
    {
        var bodies = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = reply } } } }),
                    Encoding.UTF8, "application/json"),
            };
        });
        return (handler, bodies);
    }

    public sealed class ScenarioAJobCancelledWhileQueuedNeverRuns
    {
        [Fact]
        public async Task TheQueuedJobIsSkippedAndItsStampWasClearedOnceByCancel()
        {
            const long aSpotId = 1;
            const long bSpotId = 2;
            const long cSpotId = 3;
            const long aSponsorId = 1;
            const long bSponsorId = 2;
            const long cSponsorId = 3;
            const string bSponsorName = "Bramble Bakery";

            var store = new FakeAdSpotJobStore();
            store.Seed(aSpotId, aSponsorId, "Everything must go by Friday.");
            store.Seed(bSpotId, bSponsorId, "A deal so good it's almost illegal.");
            store.Seed(cSpotId, cSponsorId, "Fresh every morning, gone by noon.");

            // Three DISTINCT sponsor names, one per spot — the request-body capture below reads
            // WHICH name a completion request actually carried, so B's own name must be one no other
            // spot's own request could ever legitimately produce.
            var sponsors = new FakeSponsorStore(id => id switch
            {
                aSponsorId => "Aardvark Autos",
                bSponsorId => bSponsorName,
                cSponsorId => "Corner Cafe",
                _ => null,
            });

            var (handler, bodies) = CapturingHandler(AdSpotWorkerHarness.WellFormedReply);
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

            var service = new AdSpotJobService(
                store, sponsors, scriptWriter, new FakePatterDurationEstimator(), new FakeAudiencePostureProvider(),
                gate, adsOptions, llmOptions, TimeProvider.System, new NoOpLogger<AdSpotJobService>());

            await service.StartAsync(CancellationToken.None);
            try
            {
                // A is dequeued first and parks on the station (InFlight stays true), so the single
                // consumer never reaches B or C until InFlight clears below — B is therefore genuinely
                // still QUEUED, never running, at the moment it is cancelled.
                var aEnqueued = await service.TryEnqueueAsync(aSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, aEnqueued);

                var bEnqueued = await service.TryEnqueueAsync(bSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, bEnqueued);

                await service.CancelAsync(bSpotId, CancellationToken.None);

                // The arrangement's own sanity pin: CancelAsync's own ClearJobAsync call has already
                // landed for B before this fact ever asks whether a run also cleared it a second time.
                Assert.Equal(1, store.ClearJobCallCount(bSpotId));

                // C is the sentinel: it is enqueued AFTER B is cancelled, and its own completion is what
                // proves the consumer loop actually dequeued past B rather than stalling on it forever.
                var cEnqueued = await service.TryEnqueueAsync(cSpotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, cEnqueued);

                gate.InFlight = false;

                var sentinelFinished = await JobWait.TryWaitUntilAsync(
                    () => store.ClearJobCallCount(cSpotId) == 1, TimeSpan.FromSeconds(5));
                Assert.True(sentinelFinished, "the sentinel job C never finished, so the loop never got past B");

                // The claim itself: B's own stamp was cleared exactly once — by the cancel, never by a
                // run of its own reaching ClearJobAsync a second time.
                Assert.Equal(1, store.ClearJobCallCount(bSpotId));

                // No outbound completion request ever carried B's own sponsor name — B never reached
                // the writer at all, only skipped.
                Assert.DoesNotContain(bodies, body => body.Contains(bSponsorName, StringComparison.Ordinal));
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                service.Dispose();
            }
        }
    }
}
