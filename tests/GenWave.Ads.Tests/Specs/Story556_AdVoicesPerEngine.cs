// STORY-556 — Every speaker keeps a voice per engine, the ads half (SPEC F219.8/.9 · PLAN T753b)
//
// A spot's stored plan holds voice setting values (a bare id or an engine map), so the engine speaking at
// render time decides each speaker's voice; a speaker with no voice there silences the spot and the booth log
// names the setting. AdRenderService is the real subject; every I/O edge is a fake.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Tts;
using Microsoft.Extensions.Configuration;

public static class FeatureAdVoicesPerEngine
{
    const string Script = "ANNOUNCER: Come on down.\nVOICE1: Prices you won't believe.";
    const string NovaMap = "{\"kokoro\":\"af_nova\",\"chatterbox\":\"nova\"}";
    const string KokoroOnlyMap = "{\"kokoro\":\"af_bella\"}";

    static AdSpot Spot(string? voicePlan, long id = 1) =>
        new(
            Id: id, SponsorId: 1, SponsorName: "Acme", Title: "Big Sale Spot", Brief: null, Script: Script,
            Source: AdSource.Llm, PackSlug: null, SpotSeconds: 30, VoicePlan: voicePlan,
            BedMediaId: null, State: AdState.Rendering, FailReason: null, MediaId: null, Generation: 1,
            CreatedAt: DateTime.UtcNow, StateChangedAt: DateTime.UtcNow, RenderedAt: null,
            RetiredAt: null, Version: "1");

    static string Plan(string announcer, string voice1) =>
        AdVoicePlanJson.Serialize([new AdVoicePlanEntry("ANNOUNCER", announcer), new AdVoicePlanEntry("VOICE1", voice1)]);

    static AdLiveSettings Live(
        string engine, string announcerVoice = "", IReadOnlyList<string>? castVoices = null, string stationVoice = "") =>
        new(announcerVoice, castVoices ?? [], BedFadeMs: AdLiveSettingsReader.DefaultBedFadeMs,
            BedDuckDb: AdLiveSettingsReader.DefaultBedDuckDb, TargetLufs: AdLiveSettingsReader.DefaultTargetLufs,
            StationVoice: stationVoice, ActiveEngine: engine);

    static (AdRenderService Service, FakeCastSegmentAuthor Author, FakeAdSpotStore Store, RecordingStationEventSink Events)
        Build(string engine)
    {
        var author = new FakeCastSegmentAuthor();
        var store = new FakeAdSpotStore();
        var libraries = new FakeAdsLibraryStore();
        libraries.AddExisting("ads");
        var events = new RecordingStationEventSink();
        var stationIdentity = new FakeStationIdentityProvider(new StationIdentity("station-1", "Test Station", "af_heart"));
        var service = new AdRenderService(
            author, store, new FakeAdminMediaLookup(), libraries, stationIdentity,
            AdTestVoices.SpeakersFor(stationIdentity, engine, events),
            new FakeOptionsMonitor<AdsOptions>(new AdsOptions { LibraryName = "ads", DurationToleranceRatio = 0.4 }),
            new AdSpotLocatorRoots("/media", "/authored"), new NoOpLogger<AdRenderService>());
        return (service, author, store, events);
    }

    static string VoiceOf(FakeCastSegmentAuthor author, string tag) =>
        author.LastRequest?.Cast.Single(member => member.Tag == tag).Voice.VoiceId ?? "";

    public sealed class ScenarioAMappedVoiceFollowsTheActiveEngine
    {
        [Fact]
        public async Task OnChatterboxTheMappedAnnouncerVoiceIsTheChatterboxOne()
        {
            var (service, author, _, _) = Build("chatterbox");

            await service.RenderAsync(Spot(Plan(NovaMap, NovaMap)), Live("chatterbox"), CancellationToken.None);

            Assert.Equal("nova", VoiceOf(author, "ANNOUNCER"));
        }

        [Fact]
        public async Task OnChatterboxTheMappedCastVoiceIsTheChatterboxOne()
        {
            var (service, author, _, _) = Build("chatterbox");

            await service.RenderAsync(Spot(Plan(NovaMap, NovaMap)), Live("chatterbox"), CancellationToken.None);

            Assert.Equal("nova", VoiceOf(author, "VOICE1"));
        }

        [Fact]
        public async Task SwitchingBackToKokoroRestoresTheKokoroVoice()
        {
            var (service, author, _, _) = Build("kokoro");

            await service.RenderAsync(Spot(Plan(NovaMap, NovaMap)), Live("kokoro"), CancellationToken.None);

            Assert.Equal("af_nova", VoiceOf(author, "VOICE1"));
        }

        [Fact]
        public async Task ABareIdStillMeansKokoro()
        {
            var (service, author, _, _) = Build("kokoro");

            await service.RenderAsync(Spot(Plan("af_bella", "am_michael")), Live("kokoro"), CancellationToken.None);

            Assert.Equal("am_michael", VoiceOf(author, "VOICE1"));
        }
    }

    public sealed class ScenarioASpeakerWithNoVoiceOnTheActiveEngineDropsTheSpot
    {
        [Fact]
        public async Task TheBoothRowNamesTheCastVoicesSetting()
        {
            var (service, _, _, events) = Build("chatterbox");

            await service.RenderAsync(
                Spot(Plan(NovaMap, KokoroOnlyMap)), Live("chatterbox", castVoices: [KokoroOnlyMap]), CancellationToken.None);

            Assert.Equal("Station:Ads:CastVoices", events.Published.OfType<VoiceMissing>().Single().Speaker);
        }

        [Fact]
        public async Task AMissingAnnouncerNamesTheAnnouncerVoiceSetting()
        {
            var (service, _, _, events) = Build("chatterbox");

            await service.RenderAsync(
                Spot(Plan(KokoroOnlyMap, NovaMap)), Live("chatterbox", announcerVoice: KokoroOnlyMap), CancellationToken.None);

            Assert.Equal("Station:Ads:AnnouncerVoice", events.Published.OfType<VoiceMissing>().Single().Speaker);
        }

        [Fact]
        public async Task TheSpotFailsNamingTheSetting()
        {
            var (service, _, store, _) = Build("chatterbox");

            await service.RenderAsync(
                Spot(Plan(NovaMap, KokoroOnlyMap)), Live("chatterbox", castVoices: [KokoroOnlyMap]), CancellationToken.None);

            Assert.Contains("voice-missing", store.LastMarkFailedReason, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NothingReachesTheEngine()
        {
            var (service, author, _, _) = Build("chatterbox");

            await service.RenderAsync(
                Spot(Plan(NovaMap, KokoroOnlyMap)), Live("chatterbox", castVoices: [KokoroOnlyMap]), CancellationToken.None);

            Assert.Null(author.LastRequest);
        }
    }

    public sealed class ScenarioAStoredVoiceNoSettingHoldsNamesTheSpot
    {
        static string NamedFor(AdSpot spot, AdLiveSettings live)
        {
            var (service, _, _, events) = Build("chatterbox");
            service.RenderAsync(spot, live, CancellationToken.None).GetAwaiter().GetResult();
            return events.Published.OfType<VoiceMissing>().Single().Speaker;
        }

        [Fact]
        public void ABareIdStampedBeforeEnginesNamesTheSpotNotTheCastSetting() =>
            Assert.Equal(
                "ad spot 12 voice cast",
                NamedFor(Spot(Plan(NovaMap, "am_michael"), id: 12), Live("chatterbox", castVoices: [NovaMap])));

        [Fact]
        public void AVoicePinnedInTheEditorNamesTheSpotNotTheAnnouncerSetting() =>
            Assert.Equal(
                "ad spot 12 voice cast",
                NamedFor(Spot(Plan("af_bella", NovaMap), id: 12), Live("chatterbox", announcerVoice: NovaMap)));

        [Fact]
        public void AMatchingCastValueNamesTheCastSetting() =>
            Assert.Equal(
                "Station:Ads:CastVoices",
                NamedFor(Spot(Plan(NovaMap, KokoroOnlyMap), id: 12), Live("chatterbox", castVoices: [KokoroOnlyMap])));
    }

    public sealed class ScenarioAStationVoiceNameIsTheFallbackWhenNoOtherSettingHoldsTheVoice
    {
        const string BareStation = "af_heart";

        static string NamedFor(AdSpot spot, AdLiveSettings live)
        {
            var (service, _, _, events) = Build("chatterbox");
            service.RenderAsync(spot, live, CancellationToken.None).GetAwaiter().GetResult();
            return events.Published.OfType<VoiceMissing>().Single().Speaker;
        }

        [Fact]
        public void AnEmptyPoolPlanNamesTheStationVoiceSetting() =>
            Assert.Equal(
                "Station:Voice",
                NamedFor(Spot(Plan(BareStation, BareStation)), Live("chatterbox", stationVoice: BareStation)));

        [Fact]
        public void ABlankAnnouncerPlanHoldingTheStationValueNamesTheStationVoiceSetting() =>
            Assert.Equal(
                "Station:Voice",
                NamedFor(Spot(Plan(BareStation, NovaMap)), Live("chatterbox", stationVoice: BareStation)));
    }

    public sealed class ScenarioTheGuardChecksOnlyWhatACastCouldStamp
    {
        const string Reply =
            "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
            "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
            "ANNOUNCER: Call 555-0142 - that's 555-0142 - Cravin's Diner.";

        const string MapA = "{\"kokoro\":\"af_bella\",\"chatterbox\":\"a\"}";
        const string MapB = "{\"kokoro\":\"am_michael\",\"chatterbox\":\"b\"}";
        const string MapC = "{\"kokoro\":\"bf_alice\",\"chatterbox\":\"c\"}";

        static async Task<AdSpotWorkerHarness.Harness> TickedAsync(Dictionary<string, string?> settings)
        {
            var harness = AdSpotWorkerHarness.Build(
                new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero), settings,
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(Reply), engine: "chatterbox");
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner", tone: "warm");
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            return harness;
        }

        static Task<AdSpotWorkerHarness.Harness> BareStationWithMappedAnnouncerAndCast() =>
            TickedAsync(new Dictionary<string, string?>
            {
                ["Station:Voice"] = "af_heart",
                ["Station:Ads:AnnouncerVoice"] = MapA,
                ["Station:Ads:CastVoices"] = $"[{MapB}, {MapC}]",
            });

        [Fact]
        public async Task ABareStationVoiceNoSpotCanCarryStillWritesASpot() =>
            Assert.Equal(1, (await BareStationWithMappedAnnouncerAndCast()).Store.CreateCallCount);

        [Fact]
        public async Task ABareStationVoiceNoSpotCanCarryIsNotReportedByTheAdsWorker() =>
            Assert.DoesNotContain(
                (await BareStationWithMappedAnnouncerAndCast()).Events.Published.OfType<VoiceMissing>(),
                row => row.Speaker == "Station:Voice");

        [Fact]
        public async Task ABlankAnnouncerWithAnUnresolvableStationVoiceWritesNoSpot() =>
            Assert.Equal(0, (await BlankAnnouncerBareStation()).Store.CreateCallCount);

        [Fact]
        public async Task ABlankAnnouncerWithAnUnresolvableStationVoiceNamesTheStationVoice() =>
            Assert.Equal(
                "Station:Voice",
                (await BlankAnnouncerBareStation()).Events.Published.OfType<VoiceMissing>().Single().Speaker);

        static Task<AdSpotWorkerHarness.Harness> BlankAnnouncerBareStation() =>
            TickedAsync(new Dictionary<string, string?>
            {
                ["Station:Voice"] = "af_heart",
                ["Station:Ads:CastVoices"] = $"[{MapB}, {MapC}]",
            });
    }

    public sealed class ScenarioTheWorkerWritesNoSpotNobodyCanVoice
    {
        const string Reply =
            "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
            "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
            "ANNOUNCER: Call 555-0142 - that's 555-0142 - Cravin's Diner.";

        static AdSpotWorkerHarness.Harness OnChatterbox() =>
            AdSpotWorkerHarness.Build(
                new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
                new Dictionary<string, string?>
                {
                    ["Station:Ads:CastVoices"] = "af_bella, am_michael",
                    ["Station:Voice"] = NovaMap,
                },
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(Reply), engine: "chatterbox");

        static async Task<AdSpotWorkerHarness.Harness> TickedTwiceAsync()
        {
            var harness = OnChatterbox();
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner", tone: "warm");
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            return harness;
        }

        [Fact]
        public async Task AnUnresolvableCastWritesNoSpot() =>
            Assert.Equal(0, (await TickedTwiceAsync()).Store.CreateCallCount);

        [Fact]
        public async Task TheMissingVoiceIsReportedOnceNotOnEveryTick() =>
            Assert.Single((await TickedTwiceAsync()).Events.Published.OfType<VoiceMissing>());

        [Fact]
        public async Task TheReportNamesTheCastSetting() =>
            Assert.Equal(
                "Station:Ads:CastVoices",
                (await TickedTwiceAsync()).Events.Published.OfType<VoiceMissing>().Single().Speaker);
    }

    public sealed class ScenarioTheCastSettingIsReadWhole
    {
        static AdLiveSettings Read(string castVoices) =>
            AdLiveSettingsReader.Read(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Ads:CastVoices"] = castVoices,
                }).Build(),
                AdTestVoices.Kokoro);

        [Fact]
        public void AJsonArrayIsNotCutOnTheCommasInsideItsMaps() =>
            Assert.Equal(2, Read($"[\"af_bella\", {NovaMap}]").CastVoices.Count);

        [Fact]
        public void AMapSpeakerKeepsItsChatterboxVoice() =>
            Assert.Contains("\"chatterbox\":\"nova\"", Read($"[\"af_bella\", {NovaMap}]").CastVoices[1], StringComparison.Ordinal);

        [Fact]
        public void TheLegacyCommaListStillSplits() =>
            Assert.Equal(["af_bella", "am_michael"], Read("af_bella, am_michael").CastVoices);

        [Fact]
        public void AMalformedArrayReadsAsAnEmptyPool() =>
            Assert.Empty(Read("[\"af_bella\", ").CastVoices);

        [Fact]
        public void TheActiveEngineIsPartOfTheSnapshot() =>
            Assert.Equal("chatterbox", AdLiveSettingsReader.Read(
                new ConfigurationBuilder().Build(), new FixedActiveVoiceEngine("chatterbox")).ActiveEngine);
    }

    public sealed class ScenarioThePreviewKeyFollowsTheEngine
    {
        static string KeyOn(string engine)
        {
            var spot = Spot(Plan(NovaMap, NovaMap));
            var sponsor = new Sponsor(
                Id: 1, Name: "Acme", PackSlug: null, Tagline: null, About: null, Phone: null, Address: null,
                Website: null, Tone: null, Paused: false, PausedAt: null,
                CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow, Version: "1");
            return AdPreviewKey.Compute(spot, sponsor, Live(engine, castVoices: [NovaMap]));
        }

        [Fact]
        public void AnEngineSwitchChangesTheKey() => Assert.NotEqual(KeyOn("kokoro"), KeyOn("chatterbox"));

        [Fact]
        public void TheSameEngineKeepsTheKey() => Assert.Equal(KeyOn("chatterbox"), KeyOn("chatterbox"));
    }

    public sealed class ScenarioTheCastPickerKnowsAMapSpeaker
    {
        [Fact]
        public void ACastMapSharingAVoiceWithTheAnnouncerIsNotCastAgainstIt()
        {
            var spot = Spot(voicePlan: null);
            var live = Live("kokoro", announcerVoice: "af_nova", castVoices: [NovaMap, "am_michael", "bf_alice"]);

            var pick = AdCastPicker.Pick(spot, live, "station_voice");

            Assert.DoesNotContain(NovaMap, new[]
            {
                pick.Entries.Single(e => e.Tag == AdCastPicker.Voice1Tag).VoiceId,
                pick.Entries.Single(e => e.Tag == AdCastPicker.Voice2Tag).VoiceId,
            });
        }
    }
}
