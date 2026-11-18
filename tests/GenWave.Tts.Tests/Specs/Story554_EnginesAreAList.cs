// STORY-554 — Engines are a list, not a hard-coded pair (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            var client = new SpeechEngineClient(new HttpClient(handler), options);

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
                new HttpClient(handler), new TestOptionsMonitor<TtsOptions>(new TtsOptions()));
            var engine = new VoiceEngine("chatterbox", "http://chatterbox:8004", VoiceEngineProfiles.Chatterbox, 30);
            voices = client.For(engine).ListVoicesAsync(default).GetAwaiter().GetResult();
        }

        [Fact]
        public void BothVoicesAreReturned() =>
            Assert.Equal(["ref-a", "ref-b"], voices);
    }

    // AC6 — a render that takes 60 s (fake clock)
    public sealed class ScenarioTheBudgetIsPerEngine
    {
        [Fact(Skip = "pending: T747")]
        public void ItPassesOnChatterbox() { }

        [Fact(Skip = "pending: T747")]
        public void ItTimesOutOnKokoro() { }
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

    static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        return new ServiceCollection().AddLogging().AddGenWaveTts(configuration).BuildServiceProvider();
    }
}
