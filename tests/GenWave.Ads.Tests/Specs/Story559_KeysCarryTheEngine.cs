// STORY-559 — Old audio is replaced quietly, the ad halves (SPEC F219.21 · PLAN T762)
//
// The preview key and the render marker an ad is stamped with both carry the engine; the speech
// cache-key facts live in GenWave.Tts.Tests/Specs/Story559_KeysCarryTheEngine.cs.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

public static class FeatureKeysCarryTheEngine
{
    static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    const string NovaMap = "{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}";

    static AdLiveSettings Live(string engine, string stationVoice = NovaMap) =>
        new(AnnouncerVoice: "", CastVoices: [], BedFadeMs: AdLiveSettingsReader.DefaultBedFadeMs,
            BedDuckDb: AdLiveSettingsReader.DefaultBedDuckDb, TargetLufs: AdLiveSettingsReader.DefaultTargetLufs,
            StationVoice: stationVoice, ActiveEngine: engine);

    // AC1 — same text and voice, engine changed
    public sealed class ScenarioTheEngineChanges
    {
        readonly AdSpot spot = new(
            Id: 1, SponsorId: 1, SponsorName: "Acme", Title: "Big Sale Spot", Brief: null,
            Script: "ANNOUNCER: Come on down.", Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30,
            VoicePlan: null, BedMediaId: null, State: AdState.Ready, FailReason: null, MediaId: 500, Generation: 1,
            CreatedAt: DateTime.UtcNow, StateChangedAt: DateTime.UtcNow, RenderedAt: null, RetiredAt: null, Version: "1");

        readonly Sponsor sponsor = new(
            Id: 1, Name: "Acme", PackSlug: null, Tagline: null, About: null, Phone: null, Address: null,
            Website: null, Tone: null, Paused: false, PausedAt: null,
            CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow, Version: "1");

        [Fact]
        public void TheAdPreviewKeyChanges() =>
            Assert.NotEqual(
                AdPreviewKey.Compute(spot, sponsor, Live("kokoro")),
                AdPreviewKey.Compute(spot, sponsor, Live("chatterbox")));

        [Fact]
        public void TheAdRenderMarkerChanges() =>
            Assert.NotEqual(AdRenderMarker.For(Live("kokoro")), AdRenderMarker.For(Live("chatterbox")));
    }

    // same text and engine, voice changed
    public sealed class ScenarioTheVoiceChanges
    {
        [Fact]
        public void TheAdRenderMarkerChangesWithTheVoice() =>
            Assert.NotEqual(
                AdRenderMarker.For(Live("chatterbox", stationVoice: "{\"chatterbox\":\"nova\"}")),
                AdRenderMarker.For(Live("chatterbox", stationVoice: "{\"chatterbox\":\"ember\"}")));

        [Fact]
        public void TheSameInputsKeepTheMarker() =>
            Assert.Equal(AdRenderMarker.For(Live("chatterbox")), AdRenderMarker.For(Live("chatterbox")));
    }

    // AC2 — an ad rendered before the switch is stale after it, through the worker's real stale pass.
    // The seeded kokoro marker is numerically ABOVE the chatterbox marker, so the old "stored < current" rule
    // would leave the spot alone; only the new "differs from the current marker" rule can pick it up.
    public sealed class ScenarioAnAdMadeOnKokoroIsStaleOnChatterbox : IAsyncLifetime
    {
        static readonly Dictionary<string, string?> Settings = new() { ["Station:Voice"] = NovaMap };

        readonly AdSpotWorkerHarness.Harness onKokoro =
            AdSpotWorkerHarness.Build(Now, Settings);

        readonly AdSpotWorkerHarness.Harness onChatterbox =
            AdSpotWorkerHarness.Build(Now, Settings, engine: "chatterbox");

        public async Task InitializeAsync()
        {
            onChatterbox.Store.AddSpot(
                1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1),
                renderVersion: onKokoro.CurrentMarker);
            onChatterbox.AdminLookup.Add(
                500, AdSpotWorkerHarness.MakeMediaRow(500, eligible: true), onChatterbox.AdsLibraryId);
            await onChatterbox.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheKokoroMarkerIsAboveTheChatterboxMarker() =>
            Assert.True(onKokoro.CurrentMarker > onChatterbox.CurrentMarker);

        [Fact]
        public void TheSpotIsReRenderedAndStampedWithTheNewMarker() =>
            Assert.Equal(onChatterbox.CurrentMarker, onChatterbox.Store.Spots.Single(s => s.Id == 1).RenderVersion);
    }

    // a spot already at the current marker is left alone
    public sealed class ScenarioAnAdAlreadyAtTheCurrentMarkerIsLeftAlone : IAsyncLifetime
    {
        static readonly Dictionary<string, string?> Settings = new() { ["Station:Voice"] = NovaMap };

        readonly AdSpotWorkerHarness.Harness onChatterbox =
            AdSpotWorkerHarness.Build(Now, Settings, engine: "chatterbox");

        public async Task InitializeAsync()
        {
            onChatterbox.Store.AddSpot(
                1, AdState.Ready, mediaId: 500, stateChangedAt: Now.UtcDateTime.AddHours(-1),
                renderVersion: onChatterbox.CurrentMarker);
            onChatterbox.AdminLookup.Add(
                500, AdSpotWorkerHarness.MakeMediaRow(500, eligible: true), onChatterbox.AdsLibraryId);
            await onChatterbox.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSpotKeepsItsMedia() =>
            Assert.Equal(500, onChatterbox.Store.Spots.Single(s => s.Id == 1).MediaId);
    }
}
