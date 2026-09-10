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

    /// <summary>A well-formed 30s spot the REAL AdScriptValidator accepts end to end (Story390's own
    /// proven reply — ANNOUNCER-led, a second voice, a 555 number, comfortably under the 42s ceiling)
    /// — the one script every scenario that means to generate successfully hands to
    /// <see cref="ServeSameReplyEveryTime"/>.</summary>
    public const string WellFormedReply =
        "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
        "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
        "ANNOUNCER: Call 555-0142 - that's 555-0142 - Cravin's Diner.";

    /// <summary>The one <c>Station:Ads:*</c> key a stock-count scenario most commonly varies, as the
    /// SAME raw-<see cref="IConfiguration"/> shape <see cref="Build"/>'s own stationSettings takes —
    /// everything else falls back to <see cref="AdStockSettingsReader"/>'s own SPEC F163.1
    /// defaults.</summary>
    public static Dictionary<string, string?> Settings(int targetCount) =>
        new() { ["Station:Ads:TargetCount"] = targetCount.ToString() };

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
    /// <param name="workerLogger">Defaults to <see cref="NoOpLogger{T}"/> — a scenario asserting on an
    /// <see cref="AdSpotWorker"/>-own log line passes its own
    /// <see cref="GenWave.Ads.Tests.Fakes.CapturingLogger{T}"/> and keeps the reference to read back
    /// after the tick.</param>
    /// <param name="stamperLogger">Defaults to <see cref="NoOpLogger{T}"/> — a scenario asserting on
    /// the cast/bed pick's own degraded-pool INFO line (PLAN T415/T416, STORY-402/STORY-403 AC7's own
    /// INFO-per-tick facts) passes its own <see cref="GenWave.Ads.Tests.Fakes.CapturingLogger{T}"/>
    /// here instead — that logging moved to <see cref="AdSpotStamper"/> when PLAN T442 hoisted the
    /// stamping pair out of this worker, so it no longer reaches <paramref name="workerLogger"/>.
    /// </param>
    public static Harness Build(
        DateTimeOffset now, IReadOnlyDictionary<string, string?>? stationSettings = null,
        int renderBudgetSeconds = 300, double durationToleranceRatio = 0.4, FakeHttpMessageHandler? llmHandler = null,
        ILogger<AdSpotWorker>? workerLogger = null, ILogger<AdSpotStamper>? stamperLogger = null)
    {
        var timeProvider = new FakeTimeProvider(now);
        var store = new FakeAdSpotLifecycleStore();
        var briefs = new FakeAdBriefStore();
        var sponsors = new FakeSponsorStore(id => briefs.SponsorIdsByBrand
            .Where(pair => pair.Value == id)
            .Select(pair => pair.Key)
            .FirstOrDefault());
        // PLAN T440 (SPEC F173.2, F173.4): briefs is built above, before sponsors — whose nameLookup
        // closure reads briefs.SponsorIdsByBrand, so briefs's own declaration order can't flip — so it
        // is wired to the paused-sponsor check post-construction instead of via a constructor
        // parameter. store carries no such dependency (its own ctor takes no arguments); its wiring
        // follows the same post-construction call for symmetry with briefs. A scenario calling only
        // harness.Sponsors.Pause(id) now reaches BOTH the sample and the stock-count/airing-exclusion
        // reads through this one call.
        briefs.ExcludePausedSponsors(sponsors.IsPaused);
        store.ExcludePausedSponsors(sponsors.IsPaused);
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

        var stamper = new AdSpotStamper(
            store, bedPool, libraries, stationIdentity, adsOptions, stamperLogger ?? new NoOpLogger<AdSpotStamper>());

        var handler = llmHandler ?? new FakeHttpMessageHandler((_, _) =>
            throw new InvalidOperationException(
                "No LLM handler wired for this scenario — pass one via AdSpotWorkerHarness.Build's own llmHandler parameter."));
        var recorder = new LlmCallRecorder(new LlmCallRing(llmOptions), new LlmCallCauseCounters(timeProvider));
        var scriptWriter = new AdScriptWriter(
            new SingleHandlerHttpClientFactory(handler), llmOptions, recorder, new FakeDegradationModeReader(),
            new NoOpLogger<AdScriptWriter>(), timeProvider);

        var worker = new AdSpotWorker(
            store, briefs, sponsors, scriptWriter, renderService, durationEstimator, audiencePosture, catalogWriter,
            adminLookup, stamper, gate, adsOptions, llmOptions, configuration,
            timeProvider, workerLogger ?? new NoOpLogger<AdSpotWorker>());

        var guardian = new AdSpotLifecycleGuardianService(
            store, adsOptions, locatorRoots, timeProvider, new NoOpLogger<AdSpotLifecycleGuardianService>());

        return new Harness(
            worker, guardian, store, briefs, sponsors, gate, author, timeProvider, adsOptions, catalogWriter,
            adminLookup, bedPool, handler, adsLibraryId);
    }
}
