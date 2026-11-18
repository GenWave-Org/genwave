// STORY-554 — Engines are a list, not a hard-coded pair (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

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
        [Fact(Skip = "pending: T746")]
        public void TheKokoroBodyCarriesSpeed() { }

        [Fact(Skip = "pending: T746")]
        public void TheKokoroBodyCarriesKokoroMarkup() { }

        [Fact(Skip = "pending: T746")]
        public void TheChatterboxBodyHasNoSpeed() { }

        [Fact(Skip = "pending: T746")]
        public void TheChatterboxBodyHasNoPauseMarkup() { }

        [Fact(Skip = "pending: T746")]
        public void TheRequestGoesToTheEnginesSpeechPath() { }
    }

    // AC5 — the engine lists two voices
    public sealed class ScenarioTheVoicesList
    {
        [Fact(Skip = "pending: T746")]
        public void BothVoicesAreReturned() { }
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
