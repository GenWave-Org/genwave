// gh-#854 — AdRenderService's own stale re-render entry point, RenderStaleAsync, plus the render
// version now stamped on every confirm. Mirrors Story391_AdRenderService.cs's own local Build/MakeSpot/
// LiveSettings helpers and FakeAdSpotStore double — the narrow, throw-on-unused render-only fake, never
// the stateful worker-integration one (this suite renders directly against AdRenderService, no worker).

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Core.Domain;
using GenWave.Tts;

public static class Gh854FeatureAdRenderServiceStaleReRender
{
    const string StationName = "GWAV Test Station";
    const string StationVoice = "station_voice";
    const string TwoLineScript = "ANNOUNCER: Come on down to the big sale.\nVOICE1: Prices you won't believe.";

    static AdSpot MakeSpot(long id = 1, long? mediaId = null) =>
        new(
            Id: id, SponsorId: 1, SponsorName: "Acme", Title: "Big Sale Spot", Brief: null, Script: TwoLineScript,
            Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null,
            State: AdState.Ready, FailReason: null, MediaId: mediaId, Generation: 1, CreatedAt: DateTime.UtcNow,
            StateChangedAt: DateTime.UtcNow, RenderedAt: DateTime.UtcNow, RetiredAt: null, Version: "1",
            RenderVersion: 0);

    static AdLiveSettings LiveSettings() =>
        new(
            AnnouncerVoice: "", CastVoices: [], BedFadeMs: AdLiveSettingsReader.DefaultBedFadeMs,
            BedDuckDb: AdLiveSettingsReader.DefaultBedDuckDb, TargetLufs: AdLiveSettingsReader.DefaultTargetLufs);

    /// <summary>Wires a REAL <see cref="AdRenderService"/> against fakes at every I/O seam — the
    /// Story391_AdRenderService.cs precedent, copied for this file's own facts.</summary>
    static (AdRenderService Service, FakeCastSegmentAuthor Author, FakeAdSpotStore Store, FakeAdminMediaLookup AdminLookup) Build()
    {
        var author = new FakeCastSegmentAuthor();
        var store = new FakeAdSpotStore();
        var adminLookup = new FakeAdminMediaLookup();
        var libraries = new FakeAdsLibraryStore();
        libraries.AddExisting("ads");
        var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", StationName, StationVoice));
        var adsOptions = new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = "ads", DurationToleranceRatio = 0.4 });
        var locatorRoots = new AdSpotLocatorRoots("/media", "/authored");

        var service = new AdRenderService(
            author, store, adminLookup, libraries, stationIdentity, adsOptions, locatorRoots,
            new NoOpLogger<AdRenderService>());

        return (service, author, store, adminLookup);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a fresh (non-stale) render stamps the current version
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFreshRenderStampsTheCurrentVersion : IAsyncLifetime
    {
        readonly (AdRenderService Service, FakeCastSegmentAuthor Author, FakeAdSpotStore Store, FakeAdminMediaLookup AdminLookup) built = Build();

        public Task InitializeAsync() =>
            built.Service.RenderAsync(MakeSpot(id: 10), LiveSettings(), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void MarkReadyCarriesTheCurrentRenderVersion()
            => Assert.Equal(AdRenderVersion.Current, built.Store.LastMarkReadyRenderVersion);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — RenderStaleAsync confirms via the guarded swap, never MarkReady
    // ---------------------------------------------------------------------

    public sealed class ScenarioAStaleReRenderConfirmsViaTheGuardedSwap : IAsyncLifetime
    {
        readonly (AdRenderService Service, FakeCastSegmentAuthor Author, FakeAdSpotStore Store, FakeAdminMediaLookup AdminLookup) built = Build();

        public Task InitializeAsync()
        {
            built.Author.MediaIdToConfirm = 999;
            return built.Service.RenderStaleAsync(MakeSpot(id: 20, mediaId: 500), oldMediaId: 500, LiveSettings(), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void SwapsTheSpotsOldMediaForTheNewOneAtTheCurrentVersion()
            => Assert.Equal((20L, 500L, 999L, AdRenderVersion.Current), (
                built.Store.LastSwapSpotId, built.Store.LastSwapOldMediaId, built.Store.LastSwapNewMediaId, built.Store.LastSwapRenderVersion));

        [Fact]
        public void NeverCallsMarkReady()
            => Assert.Equal(0, built.Store.MarkReadyCalls);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — gh-#854: a stale re-render failure never touches the spot's own row — the spot never
    // left Ready, so MarkFailedAsync's own `state = 'rendering'` guard could only ever decline, and
    // RenderStaleAsync now skips that call entirely rather than relying on the guard.
    // ---------------------------------------------------------------------

    public sealed class ScenarioAStaleReRenderFailureNeverTouchesTheStore : IAsyncLifetime
    {
        readonly (AdRenderService Service, FakeCastSegmentAuthor Author, FakeAdSpotStore Store, FakeAdminMediaLookup AdminLookup) built = Build();
        AdStaleRenderOutcome outcome = AdStaleRenderOutcome.Failed.Instance;

        public async Task InitializeAsync()
        {
            built.Author.InvokeDelegates = false;
            built.Author.Result = CastSegmentAuthorResult.Failure(CastSegmentFailureReason.ConfirmationFailed, "confirmation declined");

            outcome = await built.Service.RenderStaleAsync(MakeSpot(id: 22, mediaId: 502), oldMediaId: 502, LiveSettings(), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ReportsFailed()
            => Assert.IsType<AdStaleRenderOutcome.Failed>(outcome);

        [Fact]
        public void NeverCallsMarkFailed()
            => Assert.Equal(0, built.Store.MarkFailedCalls);
    }
}
