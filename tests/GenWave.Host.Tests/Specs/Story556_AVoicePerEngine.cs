// STORY-556 — Every speaker keeps a voice per engine, settings and render half (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Orchestration;
using GenWave.Tts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class FeatureAVoicePerEngine
{
    static Task<HttpResponseMessage> PutAsync(HttpClient client, string key, string value) =>
        client.PutAsJsonAsync("/api/settings", new[] { new { key, value } });

    // AC3 — Station:Voice, AnnouncerVoice, CastVoices
    public sealed class ScenarioSettingsTakeBothShapes : EngineSettingScenario
    {
        HttpStatusCode bareId;
        string? resolvedBareId;
        HttpStatusCode engineMap;
        HttpStatusCode castList;
        HttpStatusCode announcerMap;
        HttpStatusCode legacyCastList;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            bareId = (await PutAsync(Client, "Station:Voice", "af_nova")).StatusCode;
            resolvedBareId = Service<IVoiceResolver>().Resolve(
                VoiceMap.TryParseSetting(Service<IConfiguration>()["Station:Voice"]) ?? VoiceMap.FromEngineVoices(new Dictionary<string, string>()));
            engineMap = (await PutAsync(Client, "Station:Voice", "{\"chatterbox\":\"reader-1\"}")).StatusCode;
            castList = (await PutAsync(Client, "Station:Ads:CastVoices",
                "[\"af_nova\",{\"kokoro\":\"am_michael\",\"chatterbox\":\"reader-2\"}]")).StatusCode;
            announcerMap = (await PutAsync(Client, "Station:Ads:AnnouncerVoice", "{\"kokoro\":\"af_nova\"}")).StatusCode;
            legacyCastList = (await PutAsync(Client, "Station:Ads:CastVoices", "af_nova,am_michael")).StatusCode;
        }

        [Fact]
        public void ABareIdValidates() => Assert.Equal(HttpStatusCode.OK, bareId);

        [Fact]
        public void AnEngineMapValidates() => Assert.Equal(HttpStatusCode.OK, engineMap);

        [Fact]
        public void ACastListPerEngineValidates() => Assert.Equal(HttpStatusCode.OK, castList);

        [Fact]
        public void AnAnnouncerEngineMapValidates() => Assert.Equal(HttpStatusCode.OK, announcerMap);

        [Fact]
        public void TheLegacyCommaListStillValidates() => Assert.Equal(HttpStatusCode.OK, legacyCastList);

        [Fact]
        public void ABareIdResolvesOnKokoro() => Assert.Equal("af_nova", resolvedBareId);
    }

    // AC4 — voices for both engines; kokoro → chatterbox → kokoro through the preview endpoint
    public sealed class ScenarioThereAndBack : EngineSettingScenario
    {
        const string BothEngines = "{\"kokoro\":\"af_nova\",\"chatterbox\":\"reader-1\"}";

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await PreviewAsync();
            await SwitchToAsync("chatterbox");
            await PreviewAsync();
            await SwitchToAsync("kokoro");
            await PreviewAsync();
        }

        async Task SwitchToAsync(string engine) =>
            Assert.Equal(HttpStatusCode.OK, (await PutEngineAsync(engine)).StatusCode);

        async Task PreviewAsync() =>
            Assert.Equal(HttpStatusCode.OK,
                (await Client.PostAsJsonAsync("/api/tts/preview", new { text = "Hello there.", voice = BothEngines })).StatusCode);

        string VoiceSentTo(int request) =>
            JsonDocument.Parse(EngineRequestBodies[request]).RootElement.GetProperty("voice").GetString() ?? string.Empty;

        [Fact]
        public void TheChatterboxRenderUsesTheChatterboxVoice() => Assert.Equal("reader-1", VoiceSentTo(1));

        [Fact]
        public void TheLastRenderUsesTheFirstVoice() => Assert.Equal("af_nova", VoiceSentTo(2));
    }

    // AC4 / F219.11 — on air, the planner stamps the voice resolved for the active engine, per break
    public sealed class ScenarioThePlannedVoiceFollowsTheEngine : EngineSettingScenario
    {
        const long PersonaId = 7;

        string? onChatterbox;
        string? backOnKokoro;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new OnAirDj());
            services.RemoveAll<IPersonaCardByIdSource>();
            services.AddSingleton<IPersonaCardByIdSource>(new OnAirDj());
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Assert.Equal(HttpStatusCode.OK, (await PutEngineAsync("chatterbox")).StatusCode);
            onChatterbox = await PlannedLeadInVoiceAsync();
            Assert.Equal(HttpStatusCode.OK, (await PutEngineAsync("kokoro")).StatusCode);
            backOnKokoro = await PlannedLeadInVoiceAsync();
        }

        async Task<string> PlannedLeadInVoiceAsync()
        {
            var next = new MediaItem("t1", "/music/t1.mp3", "Song", new Loudness(-14, -1, true));
            var context = new BreakContext(
                1, null, next, "DJ", new CadenceConfig { LeadInBeforeEachTrack = true, BackAnnounceAfterEachTrack = false },
                Service<IStationIdentityProvider>().Current, DateTimeOffset.UtcNow, null, new HashSet<SpeechDeferralKind>(), TimeSpan.Zero);
            var plan = await Service<BreakPlanner>().PlanAsync(context, CancellationToken.None);
            var slot = Assert.Single(plan.Slots, s => s.Kind == SegmentKind.LeadIn);
            var request = ((RenderSource)slot.Source).Request;
            Assert.Equal(request.Voice, request.Speaker?.Voice);
            return request.Voice;
        }

        [Fact]
        public void TheChatterboxBreakStampsTheChatterboxVoice() => Assert.Equal("reader-1", onChatterbox);

        [Fact]
        public void TheNextBreakOnKokoroStampsTheKokoroVoice() => Assert.Equal("af_nova", backOnKokoro);

        sealed class OnAirDj : IActivePersonaAccessor, IPersonaCardByIdSource
        {
            public Task<Persona?> ResolveAsync(CancellationToken ct) =>
                Task.FromResult<Persona?>(new Persona(PersonaId, "DJ", string.Empty, string.Empty, "af_nova", DateTime.UtcNow, DateTime.UtcNow));

            public Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct) =>
                Task.FromResult<PersonaCard?>(new PersonaCard(
                    1, "DJ", "Talks.", "Soul.", [], new VoiceSpec("kokoro", "af_nova", 1.0, "en"), 0.5, [], [])
                {
                    Voices = new Dictionary<string, string> { ["kokoro"] = "af_nova", ["chatterbox"] = "reader-1" },
                });
        }
    }

    // Sad path
    // AC7
    public sealed class ScenarioAnUnknownEngineKey : EngineSettingScenario
    {
        [Fact]
        public async Task TheSaveReturns400() =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(Client, "Station:Voice", "{\"nope\":\"x\"}")).StatusCode);
    }

    // Sad path
    public sealed class ScenarioABadVoiceShape : EngineSettingScenario
    {
        [Theory]
        [InlineData("Station:Voice", "{\"kokoro\":\"a\",\"Kokoro\":\"b\"}")]
        [InlineData("Station:Voice", "{\"kokoro\":\" \"}")]
        [InlineData("Station:Voice", "{}")]
        [InlineData("Station:Voice", "{\"kokoro\":5}")]
        [InlineData("Station:Ads:AnnouncerVoice", "{\"nope\":\"x\"}")]
        [InlineData("Station:Ads:CastVoices", "[{\"nope\":\"x\"}]")]
        [InlineData("Station:Ads:CastVoices", "[]")]
        [InlineData("Station:Ads:CastVoices", "[5]")]
        [InlineData("Station:Ads:CastVoices", "[{\"kokoro\":\"af_nova\"},{ \"kokoro\" : \"af_nova\" }]")]
        [InlineData("Station:Ads:CastVoices", "[{\"kokoro\":\"a\",\"chatterbox\":\"r\"},{\"chatterbox\":\"r\",\"kokoro\":\"b\"}]")]
        [InlineData("Station:Ads:CastVoices", "[\"af_nova\",{\"kokoro\":\"af_nova\"}]")]
        [InlineData("Station:Ads:CastVoices", "[{\"kokoro\":\"Bad Id!\"}]")]
        [InlineData("Station:Ads:AnnouncerVoice", "{\"kokoro\":\"Bad Id!\"}")]
        public async Task ItIsRefusedWith400(string key, string value) =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(Client, key, value)).StatusCode);
    }

    // Sad path
    // AC8 — a DJ whose map lacks the active engine
    public sealed class ScenarioNoVoiceForThisEngine : EngineSettingScenario
    {
        const string Dj = "Quiet Quentin";

        readonly CapturingEventSink events = new();
        // Its own cache: a render another test cached under the same key would answer without the engine.
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), "genwave-no-voice-" + Guid.NewGuid().ToString("N"));
        MediaItem? rendered;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.Configure<TtsOptions>(o => o.CacheRoot = cacheRoot);
            services.RemoveAll<IStationEventSink>();
            services.AddSingleton<IStationEventSink>(events);
            services.RemoveAll<IPersonaCardByIdSource>();
            services.AddSingleton<IPersonaCardByIdSource>(new KokoroOnlyDj());
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Assert.Equal(HttpStatusCode.OK, (await PutEngineAsync("chatterbox")).StatusCode);

            var speaker = await Service<ISpeakerSnapshotSource>().ForPersonaAsync(KokoroOnlyDj.Id, CancellationToken.None);
            var request = new SegmentRequest(
                SegmentKind.LeadIn, speaker.Voice, "Test Station", null, DateTimeOffset.UtcNow, "test", Dj)
            { Speaker = speaker };
            rendered = await Service<ITtsSegmentSource>().RenderAsync(request, CancellationToken.None);
        }

        [Fact]
        public void TheTalkIsDropped() => Assert.Null(rendered);

        [Fact]
        public void AVoiceMissingRowNamesTheDj() =>
            Assert.Equal(Dj, Assert.Single(events.Events.OfType<VoiceMissing>()).Speaker);

        [Fact]
        public void NoRenderRequestIsSent() => Assert.Empty(EngineRequests);

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }

        sealed class KokoroOnlyDj : IPersonaCardByIdSource
        {
            public const long Id = 7;

            public Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct) =>
                Task.FromResult<PersonaCard?>(new PersonaCard(
                    1, Dj, "Says little.", "Soul.", [], new VoiceSpec("kokoro", "af_nova", 1.0, "en"), 0.5, [], [])
                {
                    Voices = new Dictionary<string, string> { ["kokoro"] = "af_nova" },
                });
        }
    }
}
