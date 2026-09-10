// STORY-424 — AdSpotJobService.RunPreviewAsync's own .wav container guard (SPEC F174.4 · PLAN T442):
// a preview render whose assembled file is NOT a .wav (the station's own
// Tts:Format configured to something else) must fail the JOB — never rename the wrong bytes onto a
// ".wav" path — and, critically, never reach IAdSpotStore.StampPreviewAsync at all: the row's own
// preview_path/preview_at/preview_key stay exactly as they were before the job ran. Mutation (e)'s
// sibling at the AdRenderService layer already proves the Failed outcome itself
// (Story391_AdRenderAssembly-adjacent facts are the assembler's own domain); this fact proves the
// JOB layer's own consequence of that outcome: ClearJobAsync carries the "wav" reason, StampPreviewAsync
// is never called.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using GenWave.Tts;
using Microsoft.Extensions.Configuration;

public static class FeaturePreviewRenderJobRequiresWav
{
    public sealed class ScenarioTheAssembledFileIsNotAWav
    {
        [Fact]
        public async Task TheJobFailsNamingWavAndNeverStampsThePreview()
        {
            const long spotId = 1;
            const long sponsorId = 1;

            var store = new FakeAdSpotJobStore();
            store.Seed(spotId, sponsorId, brief: "A deal so good it's almost illegal.");
            // VoicePlan/BedMediaId both non-null so AdSpotStamper's own two stamps are no-ops here —
            // this fact means to isolate the .wav guard, not exercise the cast/bed pick a second time
            // (Story402/403's own specs already own that).
            var seeded = await store.GetByIdAsync(spotId, CancellationToken.None);
            Assert.NotNull(seeded);
            await store.UpdateAsync(
                spotId,
                new AdSpotEdit(
                    null, null, null,
                    Script: "ANNOUNCER: Come on down to the big sale.\nVOICE1: Prices you won't believe.",
                    VoicePlan: "[]", SpotSeconds: null, BedMediaId: null),
                seeded!.Version, CancellationToken.None);

            var sponsors = new FakeSponsorStore(_ => "Acme");
            var author = new FakeCastSegmentAuthor { AssembleOnlyExtension = "mp3" };
            var libraries = new FakeAdsLibraryStore(); // no "ads" library seeded: bed-stamp is a no-op
            var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", "Test Station", "station_voice"));
            var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions());
            // A REAL, writable temp directory — AdRenderService.RenderPreviewCoreAsync genuinely calls
            // Directory.CreateDirectory(previewRoot) (never mocked), so the authored root must be a
            // path this process can actually create under; deleted in the finally below.
            var authoredRoot = Directory.CreateTempSubdirectory("t442-preview-").FullName;
            var locatorRoots = new AdSpotLocatorRoots("/media", authoredRoot);
            var renderService = new AdRenderService(
                author, store, new FakeAdminMediaLookup(), libraries, stationIdentity, adsOptions, locatorRoots,
                new NoOpLogger<AdRenderService>());
            var stamper = new AdSpotStamper(
                store, new FakeAdBedPool(), libraries, stationIdentity, adsOptions, new NoOpLogger<AdSpotStamper>());
            var llmOptions = new FakeOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 300,
            });
            var recorder = new LlmCallRecorder(new LlmCallRing(llmOptions), new LlmCallCauseCounters(TimeProvider.System));
            var scriptWriter = new AdScriptWriter(
                new SingleHandlerHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                    throw new InvalidOperationException("A preview job never reaches the LLM."))),
                llmOptions, recorder, new FakeDegradationModeReader(), new NoOpLogger<AdScriptWriter>(), TimeProvider.System);
            var configuration = new ConfigurationBuilder().Build();

            var service = new AdSpotJobService(
                store, sponsors, scriptWriter, renderService, stamper, new FakePatterDurationEstimator(),
                new FakeAudiencePostureProvider(), new FakeOnAirRenderSignal(), adsOptions, llmOptions, configuration,
                TimeProvider.System, new NoOpLogger<AdSpotJobService>());

            await service.StartAsync(CancellationToken.None);
            try
            {
                var enqueued = await service.TryEnqueueAsync(spotId, "preview", CancellationToken.None);
                Assert.Equal(AdSpotJobEnqueueResult.Accepted, enqueued);

                var settled = await JobWait.TryWaitUntilAsync(
                    () => store.ClearJobCallCount(spotId) == 1, TimeSpan.FromSeconds(5));
                Assert.True(settled, "the preview job never reached its own ClearJobAsync call");

                var spot = await store.GetByIdAsync(spotId, CancellationToken.None);
                Assert.NotNull(spot);
                Assert.Null(spot!.JobKind);
                Assert.NotNull(spot.JobError);
                Assert.Contains("wav", spot.JobError, StringComparison.Ordinal);

                // The preview stamps themselves — StampPreviewAsync throws NotSupportedException on
                // this fake (FakeAdSpotJobStore's own "narrow double, throw on unused" precedent) were
                // it ever called; the job settling at all without an unhandled exception already proves
                // it was not, and the row's own preview fields stay untouched.
                Assert.Null(spot.PreviewPath);
                Assert.Null(spot.PreviewAt);
                Assert.Null(spot.PreviewKey);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                service.Dispose();
                Directory.Delete(authoredRoot, recursive: true);
            }
        }
    }
}
