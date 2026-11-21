// STORY-555 — One setting switches the engine (SPEC F219.3 · PLAN T749)
//
// The composed Tts services over a live-reloading configuration: the active engine is looked up per
// read, the seeded kokoro follows a live Tts:Endpoint, and a piper-primary install keeps rendering
// through piper while Tts:Engine still reads its default.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Tts.Tests.Specs;

public sealed class ReloadableProvider : ConfigurationProvider
{
    public void SetAndReload(string key, string value)
    {
        Set(key, value);
        OnReload();
    }
}

public sealed class ReloadableSource(ReloadableProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

public sealed class ActiveEngineStation
{
    readonly ReloadableProvider live = new();

    public ActiveEngineStation(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Add(new ReloadableSource(live))
            .Build();
        var services = new ServiceCollection().AddLogging().AddGenWaveTts(configuration).BuildServiceProvider();
        Active = services.GetRequiredService<IActiveVoiceEngine>();
    }

    public IActiveVoiceEngine Active { get; }

    public void Save(string key, string value) => live.SetAndReload(key, value);
}

public static class FeatureActiveEngineIsReadPerRender
{
    static readonly Dictionary<string, string?> WithChatterbox = new()
    {
        ["Tts:Endpoint"] = "http://kokoro:8880",
        ["Tts:Engines:chatterbox:Endpoint"] = "http://chatterbox:8004",
        ["Tts:Engines:chatterbox:Profile"] = "chatterbox",
    };

    public sealed class ScenarioASavedEngineApplies
    {
        readonly ActiveEngineStation station = new(WithChatterbox);

        [Fact]
        public void KokoroIsActiveUntilTheSettingChanges() =>
            Assert.Equal("kokoro", station.Active.Current?.Name);

        [Fact]
        public void TheNextReadSeesTheSavedEngine()
        {
            station.Save("Tts:Engine", "chatterbox");

            Assert.Equal("chatterbox", station.Active.Current?.Name);
        }
    }

    public sealed class ScenarioTheSeededKokoroFollowsTheLiveEndpoint
    {
        readonly ActiveEngineStation station = new(WithChatterbox);

        [Fact]
        public void TheNextReadUsesTheSavedEndpoint()
        {
            station.Save("Tts:Endpoint", "http://kokoro-new:8880");

            Assert.Equal("http://kokoro-new:8880", station.Active.Current?.Endpoint);
        }
    }

    public sealed class ScenarioAPiperPrimaryInstall
    {
        static readonly Dictionary<string, string?> PiperPrimary = new(WithChatterbox)
        {
            ["Tts:PiperPrimaryEndpoint"] = "http://piper:5001",
        };

        [Fact]
        public void TheDefaultEngineResolvesToPiper() =>
            Assert.Equal("piper", new ActiveEngineStation(PiperPrimary).Active.Current?.Name);

        [Fact]
        public void AnExplicitEngineStillWins()
        {
            var station = new ActiveEngineStation(PiperPrimary);
            station.Save("Tts:Engine", "chatterbox");

            Assert.Equal("chatterbox", station.Active.Current?.Name);
        }
    }
}
