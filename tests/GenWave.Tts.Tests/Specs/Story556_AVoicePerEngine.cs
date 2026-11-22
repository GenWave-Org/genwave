// STORY-556 — Every speaker keeps a voice per engine, resolver half (SPEC F219.8 · PLAN T752)

using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureAVoicePerEngine
{
    static readonly IReadOnlyDictionary<string, string> TwoEngines =
        new Dictionary<string, string> { ["kokoro"] = "af_nova", ["chatterbox"] = "reader-1" };

    static IVoiceResolver ResolverOn(string engine) => new ActiveEngineVoiceResolver(TestActiveEngine.Named(engine));

    public sealed class ScenarioTheMapHasTheActiveEngine
    {
        readonly string? voice = ResolverOn("chatterbox").Resolve(VoiceMap.FromEngineVoices(TwoEngines));

        [Fact]
        public void ItsVoiceIsReturned() => Assert.Equal("reader-1", voice);
    }

    public sealed class ScenarioALegacyCardWithAnEmptyEngineName
    {
        readonly string? voice = ResolverOn("kokoro").Resolve(
            VoiceMap.FromEngineVoices(new Dictionary<string, string> { [""] = "af_nova" }));

        [Fact]
        public void ItResolvesOnKokoro() => Assert.Equal("af_nova", voice);
    }

    public sealed class ScenarioTheEngineIsWrittenInOtherCase
    {
        readonly string? voice = ResolverOn("chatterbox").Resolve(
            VoiceMap.FromEngineVoices(new Dictionary<string, string> { ["Chatterbox"] = "reader-1" }));

        [Fact]
        public void ItStillMatches() => Assert.Equal("reader-1", voice);
    }

    public sealed class ScenarioTheActiveEngineChangesBetweenCalls
    {
        readonly List<string?> voices = [];

        public ScenarioTheActiveEngineChangesBetweenCalls()
        {
            var engines = new VoiceEnginesOptions();
            engines.Engines["chatterbox"] = new VoiceEngineEntry { Endpoint = "http://chatterbox:8004", Profile = "chatterbox" };
            engines.Engines["kokoro"] = new VoiceEngineEntry { Endpoint = "http://kokoro:8880", Profile = "kokoro" };
            var tts = new ChangeableOptionsMonitor<TtsOptions>(new TtsOptions { Engine = "kokoro" });
            var resolver = new ActiveEngineVoiceResolver(
                new ActiveVoiceEngine(new VoiceEngineRegistry(Options.Create(engines)), tts));
            var map = VoiceMap.FromEngineVoices(TwoEngines);

            voices.Add(resolver.Resolve(map));
            tts.Change(new TtsOptions { Engine = "chatterbox" });
            voices.Add(resolver.Resolve(map));
            tts.Change(new TtsOptions { Engine = "kokoro" });
            voices.Add(resolver.Resolve(map));
        }

        [Fact]
        public void EachCallFollowsTheEngineThen() => Assert.Equal(["af_nova", "reader-1", "af_nova"], voices);
    }

    // An engine that cannot pick a voice per request never silences a speaker.
    public sealed class ScenarioPiperIsActiveAndTheMapIsKokoroOnly
    {
        readonly string? voice = ResolverOn("piper").Resolve(VoiceMap.FromEngineVoices(TwoEngines));

        [Fact]
        public void ItTakesTheKokoroVoice() => Assert.Equal("af_nova", voice);
    }

    public sealed class ScenarioPiperIsActiveAndTheMapHasAnEmptyEngineName
    {
        readonly string? voice = ResolverOn("piper").Resolve(
            VoiceMap.FromEngineVoices(new Dictionary<string, string> { [""] = "af_bella" }));

        [Fact]
        public void ItTakesThatVoice() => Assert.Equal("af_bella", voice);
    }

    public sealed class ScenarioPiperIsActiveAndTheMapOnlyHasChatterbox
    {
        readonly string? voice = ResolverOn("piper").Resolve(
            VoiceMap.FromEngineVoices(new Dictionary<string, string> { ["chatterbox"] = "reader-1" }));

        [Fact]
        public void ItTakesThatVoice() => Assert.Equal("reader-1", voice);
    }

    // The profile decides leniency, not the engine name (SPEC F219.1).
    public sealed class ScenarioAPiperProfileEngineHasAnotherName
    {
        readonly string? voice = new ActiveEngineVoiceResolver(TestActiveEngine.Named("piper-uk", "piper")).Resolve(
            VoiceMap.FromEngineVoices(new Dictionary<string, string> { ["kokoro"] = "af_nova" }));

        [Fact]
        public void ItTakesTheKokoroVoice() => Assert.Equal("af_nova", voice);
    }

    // Sad path
    public sealed class ScenarioAnEngineNamedPiperHasAVoicePickingProfile
    {
        readonly string? voice = new ActiveEngineVoiceResolver(TestActiveEngine.Named("piper", "kokoro")).Resolve(
            VoiceMap.FromEngineVoices(new Dictionary<string, string> { ["kokoro"] = "af_nova" }));

        [Fact]
        public void ItNeedsItsOwnEntry() => Assert.Null(voice);
    }

    public sealed class ScenarioTheMapLacksTheActiveEngine
    {
        readonly string? voice = ResolverOn("chatterbox").Resolve(
            VoiceMap.FromEngineVoices(new Dictionary<string, string> { ["kokoro"] = "af_nova" }));

        [Fact]
        public void ThereIsNoVoice() => Assert.Null(voice);
    }

    public sealed class ScenarioPiperIsActiveAndTheMapIsEmpty
    {
        readonly string? voice = ResolverOn("piper").Resolve(VoiceMap.FromEngineVoices(new Dictionary<string, string>()));

        [Fact]
        public void ThereIsNoVoice() => Assert.Null(voice);
    }

    public sealed class ScenarioACastListNamesOneVoiceTwice
    {
        [Theory]
        [InlineData("[{\"kokoro\":\"af_nova\"},{ \"kokoro\" : \"af_nova\" }]")]
        [InlineData("[{\"kokoro\":\"a\",\"chatterbox\":\"r\"},{\"chatterbox\":\"r\",\"kokoro\":\"b\"}]")]
        [InlineData("[\"af_nova\",{\"kokoro\":\"af_nova\"}]")]
        [InlineData("af_nova,af_nova")]
        public void ItDoesNotParse(string setting) => Assert.Null(VoiceMap.TryParseCastSetting(setting));
    }

    public sealed class ScenarioACastListHasDistinctSpeakers
    {
        readonly IReadOnlyList<VoiceMap>? cast = VoiceMap.TryParseCastSetting(
            "[\"af_nova\",{\"kokoro\":\"am_michael\",\"chatterbox\":\"reader-1\"}]");

        [Fact]
        public void EverySpeakerParses() => Assert.Equal(2, cast?.Count);
    }

    public sealed class ScenarioTheLegacyCommaListParses
    {
        [Fact]
        public void EachIdIsAKokoroSpeaker() =>
            Assert.Equal(["af_nova", "am_michael"], VoiceMap.TryParseCastSetting("af_nova,am_michael")?.Select(m => m.VoiceFor("kokoro")));
    }
}
