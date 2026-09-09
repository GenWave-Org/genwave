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
using Microsoft.Extensions.Logging;
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
        FakeSponsorStore Sponsors,
        FakeOnAirRenderSignal Gate,
        FakeCastSegmentAuthor Author,
        FakeTimeProvider TimeProvider,
        FakeOptionsMonitor<AdsOptions> AdsOptions,
        FakeAuthoredCatalogWriter CatalogWriter,
        FakeAdminMediaLookup AdminLookup,
        FakeAdBedPool BedPool,
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
    /// <param name="workerLogger">Defaults to <see cref="NoOpLogger{T}"/> — a scenario asserting on a
    /// specific log line (PLAN T415, STORY-402 AC7's own INFO-per-tick fact) passes its own
    /// <see cref="GenWave.Ads.Tests.Fakes.CapturingLogger{T}"/> and keeps the reference to read back
    /// after the tick.</param>
    public static Harness Build(
        DateTimeOffset now, IReadOnlyDictionary<string, string?>? stationSettings = null,
        int renderBudgetSeconds = 300, double durationToleranceRatio = 0.4, FakeHttpMessageHandler? llmHandler = null,
        ILogger<AdSpotWorker>? workerLogger = null)
    {
        var timeProvider = new FakeTimeProvider(now);
        var store = new FakeAdSpotLifecycleStore();
        var briefs = new FakeAdBriefStore();
        var sponsors = new FakeSponsorStore(id => briefs.SponsorIdsByBrand
            .Where(pair => pair.Value == id)
            .Select(pair => pair.Key)
            .FirstOrDefault());
        var gate = new FakeOnAirRenderSignal();
        var author = new FakeCastSegmentAuthor();
        var adminLookup = new FakeAdminMediaLookup();
        var libraries = new FakeAdsLibraryStore();
        var adsLibraryId = libraries.AddExisting("ads");
        var bedPool = new FakeAdBedPool();
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

        // PLAN T416 review F3+O3: AdSpotWorker's own cast/bed pick needs the live configuration —
        // built once, here, before it. AdRenderService no longer carries its own IConfiguration; the
        // worker hands it the SAME AdLiveSettingsReader.Read result each tick instead (see
        // AdSpotWorker.cs's own remarks).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(stationSettings ?? new Dictionary<string, string?>())
            .Build();

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

        var worker = new AdSpotWorker(
            store, briefs, sponsors, scriptWriter, renderService, durationEstimator, audiencePosture, catalogWriter,
            adminLookup, bedPool, libraries, gate, stationIdentity, adsOptions, llmOptions, configuration,
            timeProvider, workerLogger ?? new NoOpLogger<AdSpotWorker>());

        var guardian = new AdSpotLifecycleGuardianService(
            store, adsOptions, timeProvider, new NoOpLogger<AdSpotLifecycleGuardianService>());

        return new Harness(
            worker, guardian, store, briefs, sponsors, gate, author, timeProvider, adsOptions, catalogWriter,
            adminLookup, bedPool, handler, adsLibraryId);
    }
}
