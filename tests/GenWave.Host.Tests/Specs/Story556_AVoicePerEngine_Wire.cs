// STORY-556 — Every speaker keeps a voice per engine, the wire half (SPEC F219.7/.9/.11 · PLAN T753)
//
// A DJ imported on one engine and aired on another, through the real import route, the real engine
// setting and the real BreakPlanner (real DI; only the persona store, the card source and the engine
// endpoint are stand-ins), plus the preview endpoint's refusals.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

file sealed class OnlyAfHeartIsInstalled : ITtsVoiceLister
{
    public Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(["af_heart"]);
}

public static class FeatureAVoicePerEngineWire
{
    // Stores what the import route hands it and serves that DJ back as the on-air persona and card.
    sealed class ImportedDj : IPersonaImportStore, IActivePersonaAccessor, IPersonaCardByIdSource
    {
        public const long Id = 7;

        PersonaImportRequest? imported;

        public string StoredVoice => imported?.LegacyVoice ?? throw new InvalidOperationException("nothing imported yet");

        public Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct)
        {
            imported = request;
            return Task.FromResult<PersonaImportOutcome>(new PersonaImportOutcome.Imported(Id, WasCreated: true));
        }

        public Task<Persona?> ResolveAsync(CancellationToken ct) =>
            Task.FromResult(imported is null
                ? null
                : new Persona(Id, imported.Card.Name, string.Empty, string.Empty, imported.LegacyVoice, DateTime.UtcNow, DateTime.UtcNow));

        public Task<PersonaCard?> GetCardAsync(long personaId, CancellationToken ct) =>
            Task.FromResult(imported?.Card);
    }

    static string CardJson(string kokoroVoice, string? voicesJson) =>
        "{\"schemaVersion\":1,\"name\":\"DJ Nova\",\"tagline\":\"\",\"soul\":\"\",\"quirks\":[]," +
        $"\"voice\":{{\"engine\":\"kokoro\",\"voiceId\":\"{kokoroVoice}\",\"pace\":1.0,\"language\":\"en\"}}," +
        "\"energyDisposition\":0.4,\"lore\":[],\"corrections\":[]" +
        (voicesJson is null ? "}" : $",\"voices\":{voicesJson}}}");

    public abstract class ImportedDjScenario : EngineSettingScenario
    {
        readonly ImportedDj dj = new();

        protected string StoredVoice => dj.StoredVoice;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IPersonaImportStore>();
            services.AddSingleton<IPersonaImportStore>(dj);
            services.RemoveAll<ITtsVoiceLister>();
            services.AddSingleton<ITtsVoiceLister, OnlyAfHeartIsInstalled>();
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(dj);
            services.RemoveAll<IPersonaCardByIdSource>();
            services.AddSingleton<IPersonaCardByIdSource>(dj);
        }

        protected async Task SwitchToAsync(string engine) =>
            Assert.Equal(HttpStatusCode.OK, (await PutEngineAsync(engine)).StatusCode);

        protected async Task<IReadOnlyList<string>> ImportWarningsAsync(string cardJson)
        {
            var response = await Client.PostAsync(
                "/api/personas/dj-nova/import", new StringContent(cardJson, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return [.. body.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString() ?? string.Empty)];
        }

        protected async Task<SegmentRequest> PlannedLeadInAsync()
        {
            var next = new MediaItem("t1", "/music/t1.mp3", "Song", new Loudness(-14, -1, true));
            var context = new BreakContext(
                1, null, next, "DJ", new CadenceConfig { LeadInBeforeEachTrack = true, BackAnnounceAfterEachTrack = false },
                Service<IStationIdentityProvider>().Current, DateTimeOffset.UtcNow, null, new HashSet<SpeechDeferralKind>(), TimeSpan.Zero);
            var plan = await Service<BreakPlanner>().PlanAsync(context, CancellationToken.None);
            return ((RenderSource)Assert.Single(plan.Slots, s => s.Kind == SegmentKind.LeadIn).Source).Request;
        }

        protected string StationVoice => Service<IStationIdentityProvider>().Current.Voice;
    }

    // F219.11 — imported while chatterbox is active, aired on both engines
    public sealed class ScenarioImportedOnChatterbox : ImportedDjScenario
    {
        string onChatterbox = string.Empty;
        string backOnKokoro = string.Empty;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await SwitchToAsync("chatterbox");
            await ImportWarningsAsync(CardJson("af_nova", "{\"kokoro\":\"af_nova\",\"chatterbox\":\"reader-1\"}"));
            onChatterbox = (await PlannedLeadInAsync()).Voice;
            await SwitchToAsync("kokoro");
            backOnKokoro = (await PlannedLeadInAsync()).Voice;
        }

        [Fact]
        public void TheStoredVoiceIsTheCardsOwnVoice() => Assert.Equal("af_nova", StoredVoice);

        [Fact]
        public void ItAirsWithTheChatterboxEntryOnChatterbox() => Assert.Equal("reader-1", onChatterbox);

        [Fact]
        public void ItAirsWithTheKokoroEntryAfterTheSwitch() => Assert.Equal("af_nova", backOnKokoro);
    }

    // F219.11 — a kokoro-only card imported on chatterbox
    public sealed class ScenarioAKokoroOnlyCardImportedOnChatterbox : ImportedDjScenario
    {
        IReadOnlyList<string> warnings = [];
        SegmentRequest? onChatterbox;
        string backOnKokoro = string.Empty;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await SwitchToAsync("chatterbox");
            warnings = await ImportWarningsAsync(CardJson("af_nova", null));
            onChatterbox = await PlannedLeadInAsync();
            await SwitchToAsync("kokoro");
            backOnKokoro = (await PlannedLeadInAsync()).Voice;
        }

        [Fact]
        public void TheImportWarnsThereIsNoVoiceForTheEngine() =>
            Assert.Contains("no voice for the active voice engine", Assert.Single(warnings), StringComparison.Ordinal);

        [Fact]
        public void TheKokoroVoiceIsKeptVerbatim() => Assert.Equal("af_nova", StoredVoice);

        [Fact]
        public void TheTalkIsMarkedMissingWhileOnChatterbox() => Assert.True(onChatterbox?.Speaker?.VoiceMissing);

        [Fact]
        public void ItAirsWithTheKeptVoiceAfterTheSwitchBack() => Assert.Equal("af_nova", backOnKokoro);
    }

    // The same voice id on two engines is two entries: the install check blanks the kokoro row, chatterbox still has its own
    public sealed class ScenarioTheSameIdOnBothEngines : ImportedDjScenario
    {
        string onChatterbox = string.Empty;
        string onKokoro = string.Empty;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await ImportWarningsAsync(CardJson("shared", "{\"kokoro\":\"shared\",\"chatterbox\":\"shared\"}"));
            await SwitchToAsync("chatterbox");
            onChatterbox = (await PlannedLeadInAsync()).Voice;
            await SwitchToAsync("kokoro");
            onKokoro = (await PlannedLeadInAsync()).Voice;
        }

        [Fact]
        public void TheRowIsBlankedOnKokoro() => Assert.Equal(string.Empty, StoredVoice);

        [Fact]
        public void ChatterboxStillAirsItsOwnEntry() => Assert.Equal("shared", onChatterbox);

        [Fact]
        public void KokoroFallsBackToTheStationVoice() => Assert.Equal(StationVoice, onKokoro);
    }

    // Sad path — the preview endpoint refuses what it cannot voice
    public sealed class ScenarioPreviewWithNoUsableVoice : EngineSettingScenario
    {
        HttpResponseMessage noEntryForTheEngine = new();
        HttpResponseMessage malformedMap = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            noEntryForTheEngine = await PreviewAsync("{\"chatterbox\":\"reader-1\"}");
            malformedMap = await PreviewAsync("{\"kokoro\":");
        }

        async Task<HttpResponseMessage> PreviewAsync(string voice) =>
            await Client.PostAsJsonAsync("/api/tts/preview", new { text = "Hello there.", voice });

        static async Task<string> TitleAsync(HttpResponseMessage response)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("title").GetString() ?? string.Empty;
        }

        [Fact]
        public void AMapWithNoEntryForTheActiveEngineIs400() => Assert.Equal(HttpStatusCode.BadRequest, noEntryForTheEngine.StatusCode);

        [Fact]
        public async Task ItSaysThereIsNoVoiceForTheEngine() =>
            Assert.Equal("No voice for the active engine.", await TitleAsync(noEntryForTheEngine));

        [Fact]
        public void AMalformedMapIs400() => Assert.Equal(HttpStatusCode.BadRequest, malformedMap.StatusCode);

        [Fact]
        public async Task ItSaysTheMapIsMalformed() => Assert.Equal("Malformed voice.", await TitleAsync(malformedMap));

        [Fact]
        public void NoRenderRequestIsSent() => Assert.Empty(EngineRequests);
    }
}
