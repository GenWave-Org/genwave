// STORY-559 — Old audio is replaced quietly, the speech cache keys (SPEC F219.21 · PLAN T762)
//
// The ad preview key and render marker facts live in GenWave.Ads.Tests/Specs/Story559_KeysCarryTheEngine.cs
// (the Tts project has no reference to Ads).

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureKeysCarryTheEngine
{
    static SpeakerSnapshot Snapshot(string voice) =>
        new(PersonaId: 7, PersonaName: "Probe", Voice: voice, Pace: 1.0, Rules: [], Corrections: [], ContentHash: "hash-a");

    // The real routed seam: the active engine plus a Tts:EngineByKind pin and a fallback chain that may or may not
    // have a hop for it (the same two inputs FallbackTtsSynthesizer routes on).
    static IActiveVoiceEngine Routed(string pinsJson, string fallbackEndpoint)
    {
        var active = TestActiveEngine.Named("kokoro");
        var registry = new VoiceEngineRegistry(Options.Create(new VoiceEnginesOptions
        {
            Engines = { ["kokoro"] = new VoiceEngineEntry { Endpoint = "http://kokoro:8000", Profile = "kokoro" } },
        }));
        return new RoutedActiveVoiceEngine(
            new ActiveVoiceEngine(registry, new TestOptionsMonitor<TtsOptions>(new TtsOptions { Engine = "kokoro" })),
            new TtsEngineByKindProvider(
                new TestOptionsMonitor<TtsEngineByKindOptions>(new TtsEngineByKindOptions { EngineByKind = pinsJson }),
                NullLogger<TtsEngineByKindProvider>.Instance),
            new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Endpoint = fallbackEndpoint }));
    }

    static Task<string?> RenderedKeyAsync(string engine, string voice, SpeakerSnapshot? speaker = null) =>
        RenderedKeyAsync(TestActiveEngine.Named(engine), voice, speaker);

    static async Task<string?> RenderedKeyAsync(IActiveVoiceEngine engine, string voice, SpeakerSnapshot? speaker = null)
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var synth = new FakeTtsSynthesizer();
        try
        {
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Coming up next."), synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                NoCorrections.Provider(), NoCorrections.PersonaCache(), NoCorrections.PronunciationProvider(),
                NoCorrections.PersonaPronunciationCache(), NoCorrections.PersonaPaceCache(),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
                NullLogger<TtsSegmentSource>.Instance, engine);
            var request = new SegmentRequest(SegmentKind.StationId, voice, "GenWave", null, DateTimeOffset.UtcNow, "test-station")
                with { Speaker = speaker };
            return (await source.RenderAsync(request, CancellationToken.None))?.MediaId;
        }
        finally
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // AC1 — same text and voice, engine changed
    public sealed class ScenarioTheEngineChanges
    {
        [Fact]
        public async Task TheSegmentHashChanges() =>
            Assert.NotEqual(await RenderedKeyAsync("kokoro", "af_heart"), await RenderedKeyAsync("chatterbox", "af_heart"));

        [Fact]
        public void TheRenderKeyChanges() =>
            Assert.NotEqual(
                TtsRenderKey.ComputeSnapshotHash("hi", "nova", "kokoro", "s1", "speaker-hash"),
                TtsRenderKey.ComputeSnapshotHash("hi", "nova", "chatterbox", "s1", "speaker-hash"));

        [Fact]
        public void TheRenderKeyKeepsItsValueForTheSameInputs() =>
            Assert.Equal(
                TtsRenderKey.ComputeSnapshotHash("hi", "nova", "kokoro", "s1", "speaker-hash"),
                TtsRenderKey.ComputeSnapshotHash("hi", "nova", "kokoro", "s1", "speaker-hash"));
    }

    // same text and engine, voice changed
    public sealed class ScenarioTheVoiceChanges
    {
        [Fact]
        public async Task TheSegmentHashChangesWithTheVoice() =>
            Assert.NotEqual(await RenderedKeyAsync("chatterbox", "nova"), await RenderedKeyAsync("chatterbox", "ember"));
    }

    // the planned-speaker (snapshot) arm keys the engine too
    public sealed class ScenarioAPlannedSpeakerRendersOnADifferentEngine
    {
        [Fact]
        public async Task TheSnapshotKeyChanges() =>
            Assert.NotEqual(
                await RenderedKeyAsync("kokoro", "af_heart", Snapshot("af_heart")),
                await RenderedKeyAsync("chatterbox", "af_heart", Snapshot("af_heart")));
    }

    // Tts:EngineByKind sends a kind to another hop: the key names the engine that renders it (SPEC F219.21)
    public sealed class ScenarioAKindIsPinnedToAnotherHop
    {
        const string PinStationIdToPiper = "{\"StationId\":\"piper\"}";
        const string PiperHop = "http://piper:5000";

        [Fact]
        public async Task PinningTheKindChangesItsKey() =>
            Assert.NotEqual(
                await RenderedKeyAsync(Routed("", PiperHop), "af_heart"),
                await RenderedKeyAsync(Routed(PinStationIdToPiper, PiperHop), "af_heart"));

        [Fact]
        public async Task UnpinningTheKindRestoresItsKey() =>
            Assert.Equal(
                await RenderedKeyAsync(Routed("", PiperHop), "af_heart"),
                await RenderedKeyAsync(Routed("{}", PiperHop), "af_heart"));

        [Fact]
        public async Task APinToAHopTheChainLacksKeepsTheKey() =>
            Assert.Equal(
                await RenderedKeyAsync(Routed("", ""), "af_heart"),
                await RenderedKeyAsync(Routed(PinStationIdToPiper, ""), "af_heart"));

        [Fact]
        public async Task TheOtherKindsKeepTheirKey() =>
            Assert.Equal(
                await RenderedKeyAsync(Routed("", PiperHop), "af_heart"),
                await RenderedKeyAsync(Routed("{\"Weather\":\"piper\"}", PiperHop), "af_heart"));
    }
}
