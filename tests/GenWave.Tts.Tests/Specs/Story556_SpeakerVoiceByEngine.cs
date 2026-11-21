// STORY-556 — Every speaker keeps a voice per engine, the render half (SPEC F219.9/.11 · PLAN T753)
//
// The voice rule is decided by ENGINE in SpeakerSnapshotSource, never by comparing voice id strings; a cast DJ
// with no voice for the active engine silences a crosstalk exchange before any line renders.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Loudness;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureSpeakerVoiceByEngine
{
    static PersonaCard Card(string name, IReadOnlyDictionary<string, string> voices) =>
        new(PersonaCard.CurrentSchemaVersion, name, Tagline: "", Soul: name, Quirks: [],
            new VoiceSpec("kokoro", voices["kokoro"], 1.0, "en"), EnergyDisposition: 0, Lore: [], Corrections: [])
        {
            Voices = voices,
        };

    static IVoiceResolver ResolverOn(string engine) => new ActiveEngineVoiceResolver(TestActiveEngine.Named(engine));

    static async Task<SpeakerSnapshot> SnapshotAsync(string engine, PersonaCard card)
    {
        var cards = new FakePersonaCardByIdSource();
        cards.Add(1, card);
        var source = new SpeakerSnapshotSource(
            cards,
            new FakeStationIdentityProvider(new StationIdentity("genwave-1", "Test", "af_heart")),
            ResolverOn(engine),
            NoCorrections.PronunciationProvider(),
            NoCorrections.Provider(),
            NullLogger<SpeakerSnapshotSource>.Instance);
        return await source.ForPersonaAsync(1, CancellationToken.None);
    }

    static readonly IReadOnlyDictionary<string, string> TwoEngines =
        new Dictionary<string, string> { ["kokoro"] = "af_nova", ["chatterbox"] = "reader-1" };

    public sealed class ScenarioTheActiveEngineIsNotTheCardsOwn
    {
        readonly Task<SpeakerSnapshot> snapshot = SnapshotAsync("chatterbox", Card("DJ", TwoEngines));

        [Fact]
        public async Task TheVoiceIsTheActiveEnginesEntry() => Assert.Equal("reader-1", (await snapshot).Voice);

        [Fact]
        public async Task ItOverridesTheRowVoice() => Assert.True((await snapshot).VoiceOverridesRow);
    }

    public sealed class ScenarioTheActiveEngineIsTheCardsOwn
    {
        readonly Task<SpeakerSnapshot> snapshot = SnapshotAsync("kokoro", Card("DJ", TwoEngines));

        [Fact]
        public async Task TheRowVoiceStaysAuthoritative() => Assert.False((await snapshot).VoiceOverridesRow);

        [Fact]
        public async Task TheVoiceIsTheCardsOwn() => Assert.Equal("af_nova", (await snapshot).Voice);
    }

    // F219.11: decided by engine, not by the id strings
    public sealed class ScenarioTwoEnginesShareOneVoiceId
    {
        readonly Task<SpeakerSnapshot> snapshot = SnapshotAsync(
            "chatterbox", Card("DJ", new Dictionary<string, string> { ["kokoro"] = "same-id", ["chatterbox"] = "same-id" }));

        [Fact]
        public async Task ItStillOverridesTheRowVoice() => Assert.True((await snapshot).VoiceOverridesRow);
    }

    // Sad path
    public sealed class ScenarioTheCardHasNoVoiceForTheActiveEngine
    {
        readonly Task<SpeakerSnapshot> snapshot = SnapshotAsync(
            "chatterbox", Card("DJ", new Dictionary<string, string> { ["kokoro"] = "af_nova" }));

        [Fact]
        public async Task TheSpeakerIsMissingItsVoice() => Assert.True((await snapshot).VoiceMissing);

        [Fact]
        public async Task NoOtherEnginesVoiceIsCarried() => Assert.Equal(string.Empty, (await snapshot).Voice);
    }

    // F219.9 — crosstalk: both voices or nobody
    static async Task<(CrosstalkAssemblyResult Result, RecordingStationEventSink Events)> AssembleOnChatterboxAsync(
        PersonaCard host, PersonaCard neighbor)
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var loudness = new FakeLoudnessAnalyzer();
        var events = new RecordingStationEventSink();
        var assembler = new CrosstalkAssembler(
            new FakeCrosstalkVoiceSynthesizer(), NoCorrections.PronunciationProvider(), loudness, new FakeCueAnalyzer(),
            new FfmpegAudioMixer(loudness),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            ResolverOn("chatterbox"), NullLogger<CrosstalkAssembler>.Instance, events);
        var script = new CrosstalkAiredScript(
        [
            new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hello.", IsInterjection: false),
            new CrosstalkAiredLine(CrosstalkSpeaker.Neighbor, "Hi there.", IsInterjection: false),
        ]);
        var result = await assembler.AssembleAsync(new CrosstalkAssemblyRequest(script, host, neighbor), CancellationToken.None);
        return (result, events);
    }

    static readonly IReadOnlyDictionary<string, string> KokoroOnly = new Dictionary<string, string> { ["kokoro"] = "af_nova" };

    public sealed class ScenarioBothCastDjsAreSilentOnTheActiveEngine
    {
        readonly Task<(CrosstalkAssemblyResult Result, RecordingStationEventSink Events)> assembled =
            AssembleOnChatterboxAsync(Card("Quiet Quentin", KokoroOnly), Card("Hushed Hana", KokoroOnly));

        [Fact]
        public async Task TheExchangeIsDiscarded() => Assert.IsType<CrosstalkAssemblyResult.Discarded>((await assembled).Result);

        [Fact]
        public async Task EachSilentDjGetsOneVoiceMissingEvent() =>
            Assert.Equal(["Quiet Quentin", "Hushed Hana"], (await assembled).Events.Events.OfType<VoiceMissing>().Select(e => e.Speaker));
    }

    public sealed class ScenarioOnlyTheHostIsSilentOnTheActiveEngine
    {
        readonly Task<(CrosstalkAssemblyResult Result, RecordingStationEventSink Events)> assembled =
            AssembleOnChatterboxAsync(Card("Quiet Quentin", KokoroOnly), Card("Loud Lou", TwoEngines));

        [Fact]
        public async Task TheExchangeIsStillDiscarded() => Assert.IsType<CrosstalkAssemblyResult.Discarded>((await assembled).Result);

        [Fact]
        public async Task OnlyTheSilentDjIsNamed() =>
            Assert.Equal("Quiet Quentin", Assert.Single((await assembled).Events.Events.OfType<VoiceMissing>()).Speaker);
    }
}
