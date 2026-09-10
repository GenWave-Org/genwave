// STORY-422 — the skip path in AdSpotJobService.RunOneAsync is keyed by INSTANCE, not by spot id
// alone (PLAN T441 ruling): a job that finishes and is immediately requeued for the SAME spot id must
// still run, never be silently skipped because the finishing job's OWN finally block evicted the
// requeued job's own token out from under it. The race window this pins — a requeue landing between
// ClearJobAsync's own row write and RunOneAsync's own finally removing its token — is too narrow for
// real wall-clock/HTTP timing to hit deterministically, so this is an Ads.Tests unit fact instead: a
// real AdSpotJobService against a fake store whose ClearJobAsync re-enters TryEnqueueAsync for the
// same id from inside the original job's own cleanup call, before that job's own finally has a chance
// to run.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Tts;

public static class FeatureTheSkipPathIsKeyedByInstanceNotBySpotIdAlone
{
    public sealed class ScenarioASpotIsRequeuedTheInstantItsOriginalJobFinishes
    {
        [Fact]
        public async Task TheRequeuedJobStillRunsToItsOwnCompletion()
        {
            const long spotId = 1;
            const long sponsorId = 1;

            var store = new FakeAdSpotJobStore();
            store.Seed(spotId, sponsorId, "A deal so good it's almost illegal.");
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
            var gate = new FakeOnAirRenderSignal { InFlight = false };

            var service = new AdSpotJobService(
                store, sponsors, scriptWriter, new FakePatterDurationEstimator(), new FakeAudiencePostureProvider(),
                gate, adsOptions, llmOptions, TimeProvider.System, new NoOpLogger<AdSpotJobService>());

            // The race itself: the ORIGINAL job's own ClearJobAsync call re-enters TryEnqueueAsync for
            // the same spot id — a second job's token now lands in the service's own dictionary while
            // the original job is still executing, not yet at its own finally. Guarded to fire once:
            // the requeued job's OWN ClearJobAsync call must not recurse forever.
            var requeued = false;
            store.OnClearJob = async id =>
            {
                if (id != spotId || requeued)
                    return;
                requeued = true;
                await service.TryEnqueueAsync(spotId, "write", CancellationToken.None);
            };

            await service.StartAsync(CancellationToken.None);
            try
            {
                var enqueued = await service.TryEnqueueAsync(spotId, "write", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, enqueued);

                // A non-throwing bounded wait (Support/JobWait.cs): the polled condition IS the claim
                // this fact makes, so a regression must fail the Assert below, never crash the wait
                // into a TimeoutException first.
                var requeuedJobFinished = await JobWait.TryWaitUntilAsync(
                    () => store.ClearJobCallCount(spotId) >= 2, TimeSpan.FromSeconds(5));
                Assert.True(requeuedJobFinished, "the requeued job never reached its own second ClearJobAsync call");

                // Two ClearJobAsync calls for the SAME spot id proves the requeued job ran its own
                // RunWriteAsync through to completion rather than being silently skipped by
                // RunOneAsync's own guard — under the by-key-only token removal this fact is built to
                // catch, the requeued job's token is evicted before it is ever dequeued, and this
                // count never leaves 1.
                Assert.Equal(2, store.ClearJobCallCount(spotId));
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                service.Dispose();
            }
        }
    }
}
