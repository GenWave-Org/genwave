// STORY-554 — Engines are a list, not a hard-coded pair (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Orchestration;
using GenWave.TestSupport;
using GenWave.TestSupport.Fakes;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureEnginesAreAList
{
    // AC1, AC2 — Tts:Engines:chatterbox with budget 120; another engine with none
    public sealed class ScenarioTheRegistry
    {
        readonly IVoiceEngineRegistry registry;

        public ScenarioTheRegistry()
        {
            registry = FeatureEnginesAreAList.Build(new Dictionary<string, string?>
            {
                ["Tts:Endpoint"] = "http://kokoro-legacy:8880",
                ["Tts:Engines:chatterbox:Endpoint"] = "http://chatterbox:8004",
                ["Tts:Engines:chatterbox:Profile"] = "chatterbox",
                ["Tts:Engines:chatterbox:RenderBudgetSeconds"] = "120",
                ["Tts:Engines:other:Endpoint"] = "http://other:1",
                ["Tts:Engines:other:Profile"] = "piper",
            }).GetRequiredService<IVoiceEngineRegistry>();
        }

        [Fact]
        public void TheEngineIsReturnedByName() =>
            Assert.Equal("chatterbox", registry.Get("chatterbox")?.Profile.Name);

        [Fact]
        public void ItsBudgetIs120Seconds() =>
            Assert.Equal(120, registry.Get("chatterbox")?.RenderBudgetSeconds);

        [Fact]
        public void AnEngineWithNoBudgetGets30Seconds() =>
            Assert.Equal(30, registry.Get("other")?.RenderBudgetSeconds);

        [Fact]
        public void KokoroIsSeededFromTtsEndpointWhenNoEntryExists() =>
            Assert.Equal("http://kokoro-legacy:8880", registry.Get("kokoro")?.Endpoint);
    }

    // AC3, AC4 — one line rendered per profile against a recording handler
    public sealed class ScenarioTheRequestFollowsTheProfile
    {
        const string Line = "Good evening. This is the station.";

        readonly JsonDocument kokoroBody;
        readonly JsonDocument chatterboxBody;
        readonly Uri chatterboxUri;
        readonly string kokoroLegacyBody;
        readonly string clientKokoroBody;

        public ScenarioTheRequestFollowsTheProfile()
        {
            var cacheRoot = Path.Combine(Path.GetTempPath(), "gw-story554-" + Guid.NewGuid().ToString("N"));
            var options = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });

            var sent = new List<(Uri Uri, string Body)>();
            var handler = new FakeHttpMessageHandler(async (request, ct) =>
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                sent.Add((request.RequestUri ?? throw new InvalidOperationException("no uri"), body));
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            });
            var client = new SpeechEngineClient(new HttpClient(handler), options, new EngineRenderActivity());

            client.RenderAsync(Engine("kokoro", "http://kokoro:8880", VoiceEngineProfiles.Kokoro), Context(), default)
                .GetAwaiter().GetResult();
            client.RenderAsync(Engine("chatterbox", "http://chatterbox:8004", VoiceEngineProfiles.Chatterbox), Context(), default)
                .GetAwaiter().GetResult();
            TestKokoro.Synthesizer(new HttpClient(handler), options)
                .SynthesizeAsync(Context(), default).GetAwaiter().GetResult();

            kokoroBody = JsonDocument.Parse(sent[0].Body);
            chatterboxBody = JsonDocument.Parse(sent[1].Body);
            chatterboxUri = sent[1].Uri;
            clientKokoroBody = sent[0].Body;
            kokoroLegacyBody = sent[2].Body;
        }

        [Fact]
        public void TheKokoroBodyCarriesSpeed() =>
            Assert.Equal(1.25, kokoroBody.RootElement.GetProperty("speed").GetDouble());

        [Fact]
        public void TheKokoroBodyCarriesPauseMarkup() =>
            Assert.Contains("[pause:", kokoroBody.RootElement.GetProperty("input").GetString());

        [Fact]
        public void TheKokoroBodyCarriesIpaMarkup() =>
            Assert.Contains("[station](/", kokoroBody.RootElement.GetProperty("input").GetString());

        [Fact]
        public void TheChatterboxBodyHasNoSpeed() =>
            Assert.False(chatterboxBody.RootElement.TryGetProperty("speed", out _));

        [Fact]
        public void TheChatterboxBodyIsThePlainLine() =>
            Assert.Equal(Line, chatterboxBody.RootElement.GetProperty("input").GetString());

        [Fact]
        public void TheRequestGoesToTheEnginesSpeechPath() =>
            Assert.Equal("http://chatterbox:8004/v1/audio/speech", chatterboxUri.ToString());

        [Fact]
        public void TheKokoroBodyIsByteEqualToTheLegacyKokoroSynthesizers() =>
            Assert.Equal(kokoroLegacyBody, clientKokoroBody);

        static VoiceEngine Engine(string name, string endpoint, VoiceEngineProfile profile) =>
            new(name, endpoint, profile, 30);

        static TtsRenderContext Context() => new(Line, "af_heart", null)
        {
            Pace = 1.25,
            Rules = [new GenWave.Core.Domain.PronunciationRule("station", "station", "/ˈsteɪʃən/")],
        };
    }

    // AC5 — the engine lists two voices
    public sealed class ScenarioTheVoicesList
    {
        readonly IReadOnlyList<string> voices;

        public ScenarioTheVoicesList()
        {
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"voices\":[\"ref-a\",\"ref-b\"]}", System.Text.Encoding.UTF8, "application/json"),
            }));
            var client = new SpeechEngineClient(
                new HttpClient(handler), new TestOptionsMonitor<TtsOptions>(new TtsOptions()), new EngineRenderActivity());
            var engine = new VoiceEngine("chatterbox", "http://chatterbox:8004", VoiceEngineProfiles.Chatterbox, 30);
            voices = client.ListVoicesAsync(engine, default).GetAwaiter().GetResult();
        }

        [Fact]
        public void BothVoicesAreReturned() =>
            Assert.Equal(["ref-a", "ref-b"], voices);
    }

    // AC6 — a render that takes 60 s (fake clock), planned and rendered through the real chain
    public sealed class ScenarioTheBudgetIsPerEngine : IAsyncLifetime
    {
        static readonly TimeSpan Render = TimeSpan.FromSeconds(60);
        static readonly DateTimeOffset Start = new(2030, 1, 7, 9, 0, 0, TimeSpan.Zero);

        SlotOutcome chatterbox = new FailedOutcome();
        SlotOutcome kokoro = new FailedOutcome();

        public async Task InitializeAsync()
        {
            var services = FeatureEnginesAreAList.Build(new Dictionary<string, string?>
            {
                ["Tts:Engines:chatterbox:Endpoint"] = "http://chatterbox:8004",
                ["Tts:Engines:chatterbox:Profile"] = "chatterbox",
                ["Tts:Engines:chatterbox:RenderBudgetSeconds"] = "120",
            });
            chatterbox = await RenderThroughBreakAsync(services, "chatterbox");
            kokoro = await RenderThroughBreakAsync(services, "kokoro");
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItPassesOnChatterbox() => Assert.IsType<RenderedOutcome>(chatterbox);

        [Fact]
        public void ItTimesOutOnKokoro() => Assert.IsType<TimedOutOutcome>(kokoro);

        // Plans one lead-in with the real active-engine budget, renders it through BreakRenderer
        // against a TTS fake that finishes after 60 s on the shared fake clock.
        static async Task<SlotOutcome> RenderThroughBreakAsync(ServiceProvider services, string engine)
        {
            var clock = new FakeTimeProvider(Start);
            var budget = new ActiveEngineRenderBudgetProvider(
                new NamedEngine(services.GetRequiredService<IVoiceEngineRegistry>(), engine),
                services.GetRequiredService<IOptionsMonitor<TtsOptions>>());
            var chain = new BreakPlannerBuilder().WithTime(clock).WithRenderBudget(budget).Build();
            var track = TestData.MakeTrackRef("t1").ToMediaItem();
            var context = new BreakContext(
                1, track, track, "DJ Alpha", new CadenceConfig { LeadInBeforeEachTrack = true, BackAnnounceAfterEachTrack = false },
                new StationIdentity("station-1", "GenWave", "voice-station"), Start, null,
                new HashSet<SpeechDeferralKind>(), TimeSpan.Zero);
            var plan = await chain.Planner.PlanAsync(context, CancellationToken.None);

            var tts = new FakeTtsSegmentSource { RenderDelay = Render, TimeProvider = clock };
            var rendering = new BreakRenderer(tts, clock).RenderAsync(plan, CancellationToken.None);

            // Advance to whichever deadline (render or budget) comes first, so the race has one winner.
            clock.Advance(plan.RenderBudget < Render ? plan.RenderBudget : Render);
            var outcomes = await rendering;
            return Assert.Single(outcomes);
        }

        sealed class NamedEngine(IVoiceEngineRegistry registry, string name) : IActiveVoiceEngine
        {
            public string Name => name;
            public VoiceEngine? Current => registry.Get(name);
        }
    }

    // T750a — Tts:EngineRenderBudgets overrides an engine's own budget, live
    public sealed class ScenarioASettingsBudgetForTheActiveEngine
    {
        readonly TestOptionsMonitor<TtsOptions> options = new(new TtsOptions
        {
            RenderBudgetSeconds = 30,
            EngineRenderBudgets = "{\"chatterbox\":45}",
        });
        readonly ActiveEngineRenderBudgetProvider provider;

        public ScenarioASettingsBudgetForTheActiveEngine() =>
            provider = new ActiveEngineRenderBudgetProvider(new FixedEngine(ChatterboxWithBudget120), options);

        [Fact]
        public void ItWinsOverTheEnginesConfigValue() =>
            Assert.Equal(TimeSpan.FromSeconds(45), provider.Current);

        [Fact]
        public void ItMatchesTheEngineNameIgnoringCase()
        {
            options.CurrentValue = new TtsOptions { EngineRenderBudgets = "{\"Chatterbox\":50}" };

            Assert.Equal(TimeSpan.FromSeconds(50), provider.Current);
        }

        [Fact]
        public void ALiveChangeAppliesToTheNextRead()
        {
            options.CurrentValue = new TtsOptions { EngineRenderBudgets = "{\"chatterbox\":200}" };

            Assert.Equal(TimeSpan.FromSeconds(200), provider.Current);
        }

        [Fact]
        public void ClearingItRestoresTheEnginesConfigValue()
        {
            options.CurrentValue = new TtsOptions { EngineRenderBudgets = "" };

            Assert.Equal(TimeSpan.FromSeconds(120), provider.Current);
        }
    }

    // T750a — an override for some other engine leaves the active engine's own value alone
    public sealed class ScenarioASettingsBudgetForAnotherEngine
    {
        [Fact]
        public void TheActiveEngineKeepsItsConfigValue()
        {
            var options = new TtsOptions { RenderBudgetSeconds = 30, EngineRenderBudgets = "{\"kokoro\":10}" };
            var provider = new ActiveEngineRenderBudgetProvider(new FixedEngine(ChatterboxWithBudget120), new TestOptionsMonitor<TtsOptions>(options));

            Assert.Equal(TimeSpan.FromSeconds(120), provider.Current);
        }
    }

    // T750a — no active engine: the Tts budget, even with overrides present
    public sealed class ScenarioNoActiveEngineWithOverrides
    {
        [Fact]
        public void ItFallsBackToTheTtsBudget()
        {
            var options = new TtsOptions { RenderBudgetSeconds = 33, EngineRenderBudgets = "{\"chatterbox\":45}" };
            var provider = new ActiveEngineRenderBudgetProvider(new FixedEngine(null), new TestOptionsMonitor<TtsOptions>(options));

            Assert.Equal(TimeSpan.FromSeconds(33), provider.Current);
        }
    }

    // T750a sad path — a stored value the reader cannot use never breaks a render
    public sealed class ScenarioAnUnreadableSettingsBudget
    {
        [Theory]
        [InlineData("not json")]
        [InlineData("[45]")]
        [InlineData("{\"chatterbox\":0}")]
        [InlineData("{\"chatterbox\":601}")]
        [InlineData("{\"chatterbox\":\"45\"}")]
        public void TheEnginesConfigValueStillApplies(string stored)
        {
            var options = new TtsOptions { EngineRenderBudgets = stored };
            var provider = new ActiveEngineRenderBudgetProvider(new FixedEngine(ChatterboxWithBudget120), new TestOptionsMonitor<TtsOptions>(options));

            Assert.Equal(TimeSpan.FromSeconds(120), provider.Current);
        }
    }

    static readonly VoiceEngine ChatterboxWithBudget120 =
        new("chatterbox", "http://chatterbox:8004", VoiceEngineProfiles.Find("chatterbox") ?? throw new InvalidOperationException("no chatterbox profile"), 120);

    sealed class FixedEngine(VoiceEngine? engine) : IActiveVoiceEngine
    {
        public string Name => engine?.Name ?? "gone";
        public VoiceEngine? Current => engine;
    }

    // Sad path
    // AC7, AC8 — unknown profile; blank endpoint
    public sealed class ScenarioABadEngineEntry
    {
        [Fact]
        public void AnUnknownProfileFailsBootNamingTheKey()
        {
            var ex = Assert.Throws<OptionsValidationException>(() => FeatureEnginesAreAList.Build(new Dictionary<string, string?>
            {
                ["Tts:Engines:x:Endpoint"] = "http://x:1",
                ["Tts:Engines:x:Profile"] = "nope",
            }).GetRequiredService<IVoiceEngineRegistry>());
            Assert.Contains("Tts:Engines:x:Profile", ex.Message);
        }

        [Fact]
        public void ABlankEndpointFailsBootNamingTheKey()
        {
            var ex = Assert.Throws<OptionsValidationException>(() => FeatureEnginesAreAList.Build(new Dictionary<string, string?>
            {
                ["Tts:Engines:y:Endpoint"] = " ",
                ["Tts:Engines:y:Profile"] = "chatterbox",
            }).GetRequiredService<IVoiceEngineRegistry>());
            Assert.Contains("Tts:Engines:y:Endpoint", ex.Message);
        }
    }

    // Sad path — a budget past the ceiling would overflow CancelAfter
    public sealed class ScenarioAHugeBudget
    {
        [Fact]
        public void ItFailsBootNamingTheKey()
        {
            var ex = Assert.Throws<OptionsValidationException>(() => FeatureEnginesAreAList.Build(new Dictionary<string, string?>
            {
                ["Tts:Engines:z:Endpoint"] = "http://z:1",
                ["Tts:Engines:z:Profile"] = "chatterbox",
                ["Tts:Engines:z:RenderBudgetSeconds"] = "601",
            }).GetRequiredService<IVoiceEngineRegistry>());
            Assert.Contains("Tts:Engines:z:RenderBudgetSeconds", ex.Message);
        }
    }

    static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        return new ServiceCollection().AddLogging().AddGenWaveTts(configuration).BuildServiceProvider();
    }
}
