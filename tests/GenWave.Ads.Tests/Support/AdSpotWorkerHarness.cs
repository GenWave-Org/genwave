// Shared home for a REAL AdSpotWorker + AdSpotLifecycleGuardianService, wired to fakes at every I/O
// edge (the CrosstalkWorkerHarness precedent, GenWave.Host.Tests/Support — see that file's own
// remarks). Both Story389_AdStockKeeping.cs and Story391_AdSpotWorker.cs call
// AdSpotWorkerHarness.Build instead of keeping their own copy of this construction.

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;
using GenWave.Tts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Ads.Tests.Support;

internal static class AdSpotWorkerHarness
{
    public const string StationName = "GWAV Test Station";
    public const string StationVoice = "station_voice";

    public sealed record Harness(
        AdSpotWorker Worker,
        AdSpotLifecycleGuardianService Guardian,
        FakeAdSpotLifecycleStore Store,
        FakeAdBriefStore Briefs,
        FakeOnAirRenderSignal Gate,
        FakeCastSegmentAuthor Author,
        FakeTimeProvider TimeProvider,
        FakeOptionsMonitor<AdsOptions> AdsOptions,
        FakeAuthoredCatalogWriter CatalogWriter,
        FakeAdminMediaLookup AdminLookup,
        FakeHttpMessageHandler LlmHandler,
        long AdsLibraryId);

    /// <summary>A minimal, well-formed <c>library.media</c> row for the repair sweep's own recency/
    /// eligibility facts (PLAN T402 review F2) — every field this project's own reads actually
    /// touch (<see cref="GenWave.Core.Domain.AdminMediaDto.Eligible"/> alone, for the repair sweep)
    /// filled with an inert placeholder for everything else.</summary>
    public static GenWave.Core.Domain.AdminMediaDto MakeMediaRow(long mediaId, bool eligible) => new(
        MediaId: mediaId.ToString(), Locator: $"/authored/ads/{mediaId}.wav", Format: "wav", State: "ready",
        DurationMs: 7000, Title: "spot", Artist: StationName, Album: null, Genre: null, Year: null,
        IntegratedLufs: -16.0, TruePeakDbtp: -1.0, Measurable: true, CueInSec: null, CueOutSec: null,
        Eligible: eligible, Version: "1");

    /// <summary>Serves the SAME completion reply for every request the writer sends (a re-ask, should
    /// one fire, gets the identical reply back) — the Story390_AdScriptWriterMeetsTheRealValidator
    /// precedent, this project's own Specs folder.</summary>
    public static FakeHttpMessageHandler ServeSameReplyEveryTime(string content) => new((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
                Encoding.UTF8, "application/json"),
        }));

    /// <param name="now">The worker/guardian's own shared clock — advanced with
    /// <see cref="FakeTimeProvider.Advance(TimeSpan)"/> to drive a watchdog/sweep forward without a
    /// real wall-clock wait.</param>
    /// <param name="stationSettings">Raw <c>Station:Ads:*</c> config values — the SAME "raw
    /// IConfiguration reads" shape <see cref="AdStockSettingsReader"/> itself reads production values
    /// through (see that class's own remarks); a scenario overrides only the keys it cares about,
    /// everything else falls back to <see cref="AdStockSettingsReader"/>'s own SPEC F163.1
    /// defaults.</param>
    /// <param name="renderBudgetSeconds">Small on purpose — every spec here runs in-process against
    /// fakes, never real ffmpeg/kokoro, so nothing legitimately takes seconds; the production 180s
    /// default would only ever matter for a real backend.</param>
    /// <param name="llmHandler">The generation seam's own controllable HTTP backend — defaults to a
    /// handler that throws if ever invoked (a scenario that never means to generate should never
    /// reach it silently); a scenario that DOES mean to generate passes
    /// <see cref="ServeSameReplyEveryTime"/> or its own custom handler.</param>
    public static Harness Build(
        DateTimeOffset now, IReadOnlyDictionary<string, string?>? stationSettings = null,
        int renderBudgetSeconds = 300, double durationToleranceRatio = 0.4, FakeHttpMessageHandler? llmHandler = null)
    {
        var timeProvider = new FakeTimeProvider(now);
        var store = new FakeAdSpotLifecycleStore();
        var briefs = new FakeAdBriefStore();
        var gate = new FakeOnAirRenderSignal();
        var author = new FakeCastSegmentAuthor();
        var adminLookup = new FakeAdminMediaLookup();
        var libraries = new FakeAdsLibraryStore();
        var adsLibraryId = libraries.AddExisting("ads");
        var catalogWriter = new FakeAuthoredCatalogWriter();
        var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", StationName, StationVoice));
        var audiencePosture = new FakeAudiencePostureProvider();
        var durationEstimator = new FakePatterDurationEstimator();

        var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions
        {
            LibraryName = "ads", DurationToleranceRatio = durationToleranceRatio, BedDuckDb = -12.0,
            RenderBudgetSeconds = renderBudgetSeconds, WorkerIntervalMinutes = 10,
        });
        var llmOptions = new FakeOptionsMonitor<LlmOptions>(new LlmOptions
        {
            Endpoint = "http://fake-llm.local", Model = "test-model", TimeoutSeconds = 5, MaxCopyChars = 300,
        });
        var locatorRoots = new AdSpotLocatorRoots("/media", "/authored");

        var renderService = new AdRenderService(
            author, store, adminLookup, libraries, stationIdentity, adsOptions, locatorRoots,
            new NoOpLogger<AdRenderService>());

        var handler = llmHandler ?? new FakeHttpMessageHandler((_, _) =>
            throw new InvalidOperationException(
                "No LLM handler wired for this scenario — pass one via AdSpotWorkerHarness.Build's own llmHandler parameter."));
        var recorder = new LlmCallRecorder(new LlmCallRing(llmOptions), new LlmCallCauseCounters(timeProvider));
        var scriptWriter = new AdScriptWriter(
            new SingleHandlerHttpClientFactory(handler), llmOptions, recorder, new FakeDegradationModeReader(),
            new NoOpLogger<AdScriptWriter>(), timeProvider);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(stationSettings ?? new Dictionary<string, string?>())
            .Build();

        var worker = new AdSpotWorker(
            store, briefs, scriptWriter, renderService, durationEstimator, audiencePosture, catalogWriter,
            adminLookup, gate, adsOptions, llmOptions, configuration, timeProvider, new NoOpLogger<AdSpotWorker>());

        var guardian = new AdSpotLifecycleGuardianService(
            store, adsOptions, timeProvider, new NoOpLogger<AdSpotLifecycleGuardianService>());

        return new Harness(
            worker, guardian, store, briefs, gate, author, timeProvider, adsOptions, catalogWriter, adminLookup,
            handler, adsLibraryId);
    }
}
