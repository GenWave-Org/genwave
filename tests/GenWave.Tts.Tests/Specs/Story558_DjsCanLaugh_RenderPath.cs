// STORY-558 — DJs can laugh, the render path units (SPEC F219.17-F219.19 · PLAN T761)
//
// BDD specification — xUnit. The hygiene clean-up, the render gate and the engine boundary, below the host.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Loudness;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureDjsCanLaughOnTheRenderPathUnits
{
    static readonly IReadOnlyList<string> ChatterboxTags = VoiceEngineProfiles.Chatterbox.SoundTags;

    static SoundTagRenderGate GateOn(string activeEngine, int cap) =>
        new(TestActiveEngine.Named(activeEngine),
            new OptionsMonitorSoundTagCapProvider(new TestOptionsMonitor<TtsSoundTagsOptions>(new TtsSoundTagsOptions { MaxPerBreak = cap })));

    static VoiceEngine Engine(string name, VoiceEngineProfile profile) => new(name, $"http://{name}:8000", profile, 30);

    // Hazard: the DJ clean-up removed every bracket, so an offered tag never reached the render
    public sealed class ScenarioTheCleanUpKeepsOfferedTags
    {
        const string Raw = "Hi [laugh] there [wave] friend.";

        [Fact]
        public void AnOfferedTagSurvives() =>
            Assert.Equal("Hi [laugh] there friend.", LlmCopyWriter.ApplyCopyHygiene(Raw, ChatterboxTags));

        [Fact]
        public void WithNoOfferEveryBracketIsStillRemoved() =>
            Assert.Equal("Hi there friend.", LlmCopyWriter.ApplyCopyHygiene(Raw));

        [Fact]
        public void TheCaseOfAnOfferedTagDoesNotMatter() =>
            Assert.Equal("Hi [Laugh] there friend.", LlmCopyWriter.ApplyCopyHygiene("Hi [Laugh] there [wave] friend.", ChatterboxTags));

        [Fact]
        public void TheDjCleanupResultKeepsTheTag()
        {
            var cleanup = LlmCopyWriter.CleanupOf(new CompletionReply(Raw, "stop", 0), 500, null, ChatterboxTags);

            Assert.Equal("Hi [laugh] there friend.", LlmCopyWriter.TextOf(cleanup));
        }

        [Fact]
        public void TheCrosstalkParserKeepsTheTag()
        {
            const string Script = 
                "HOST: Well [laugh] that was something to hear today.\n" +
                "NEIGHBOR: It really was [wave] and then some more.\n" +
                "HOST: Stick around, more good times are coming right up.";
            var result = CrosstalkScriptParser.Parse(
                Script, 500, 30, new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero), keepTags: ChatterboxTags);

            var accepted = Assert.IsType<CrosstalkWriteResult.Accepted>(result);
            Assert.Equal("Well [laugh] that was something to hear today.", accepted.Script.Lines[0].Text);
        }
    }

    // Hazard: the speech normaliser ran on text that still had tags (T759 owns the normaliser fact; kept as a guard for the order)
    public sealed class ScenarioTheNormaliserKeepsATagMidSentence
    {
        [Fact]
        public void TheTagIsByteIntact() =>
            Assert.Contains("[clear throat]", SpeechText.Normalize("It is 9 [clear throat] o'clock now.", SpeechCorrectionSet.Empty));
    }

    // Hazard: the engine that renders is not always the active one
    public sealed class ScenarioTheGateFollowsTheRenderingEngine
    {
        const string Line = "[laugh] hi [sigh]";

        [Fact]
        public void OnTheActiveEngineTheCapApplies() =>
            Assert.Equal("[laugh] hi", GateOn("chatterbox", 1).Apply(Line, SegmentKind.LeadIn));

        [Fact]
        public void ANamedKokoroEngineGetsNoTagWhileChatterboxIsActive() =>
            Assert.Equal("hi", GateOn("chatterbox", 1).Apply(Line, SegmentKind.LeadIn, Engine("kokoro", VoiceEngineProfiles.Kokoro)));

        [Fact]
        public void ACrosstalkLineIsGatedLikeADjLine() =>
            Assert.Equal("[laugh] hi", GateOn("chatterbox", 1).Apply(Line, SegmentKind.Crosstalk));

        [Fact]
        public void CapZeroRemovesEveryTag() =>
            Assert.Equal("hi", GateOn("chatterbox", 0).Apply(Line, SegmentKind.LeadIn));

        [Fact]
        public void TextWithoutATagIsReturnedUnchanged() =>
            Assert.Equal("Just  words.", GateOn("chatterbox", 1).Apply("Just  words.", SegmentKind.LeadIn));
    }

    // F219.17 — the prompt promises the cap for the whole exchange, but lines render one by one
    public sealed class ScenarioTheExchangeCapIsOneRunningCount : IAsyncLifetime
    {
        IReadOnlyList<string> lines = [];

        public async Task InitializeAsync()
        {
            const string Script =
                "HOST: Well [laugh] that was something to hear today.\n" +
                "NEIGHBOR: It really was [sigh] and then some more.\n" +
                "HOST: Stick around [laugh] more good times are coming right up.";
            var writer = new CrosstalkScriptWriter(
                QuirkSpecHarness.ScriptedHttp([], [Script]),
                new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
                new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
                new LlmCallRecorder(
                    new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())), new LlmCallCauseCounters(TimeProvider.System)),
                new FakeDegradationModeReader(),
                NullLogger<CrosstalkScriptWriter>.Instance,
                TimeProvider.System,
                soundTagOffers: new ActiveEngineSoundTagOfferProvider(
                    TestActiveEngine.Named("chatterbox"),
                    new OptionsMonitorSoundTagCapProvider(new TestOptionsMonitor<TtsSoundTagsOptions>(new TtsSoundTagsOptions { MaxPerBreak = 1 }))));
            var request = new CrosstalkExchangeRequest(
                Card("Nova"), Card("Rex"), "GenWave", ShowName: "Night Shift", Daypart: null,
                StationLocalNow: new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero),
                HostPersonaId: 7, NeighborPersonaId: 8, ShowId: 3);

            var result = await writer.WriteExchangeAsync(request, CancellationToken.None);
            lines = [.. Assert.IsType<CrosstalkWriteResult.Accepted>(result).Script.Lines.Select(line => line.Text)];
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFirstLineKeepsItsTag() => Assert.Equal("Well [laugh] that was something to hear today.", lines[0]);

        [Fact]
        public void TheLaterLinesCarryNone() => Assert.DoesNotContain(lines.Skip(1), line => line.Contains('['));

        static PersonaCard Card(string name) =>
            new(PersonaCard.CurrentSchemaVersion, name, "", "Soul line.", [],
                new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);
    }

    // F219.19 — the real ad cast path: CrosstalkAssembler.AssembleCastAsync, the real normalising synthesizer and gate, on chatterbox
    public sealed class ScenarioAdsNeverCarryTags : IAsyncLifetime
    {
        readonly string outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeCrosstalkVoiceSynthesizer engine = new();

        public async Task InitializeAsync()
        {
            var normalizing = new NormalizingTtsSynthesizer(
                engine, NoCorrections.Provider(), NoCorrections.PersonaCache(), new CorrectionsFiredStats(),
                NullLogger<NormalizingTtsSynthesizer>.Instance, TestSoundTagGate.On("chatterbox", cap: 5));
            var assembler = new CrosstalkAssembler(
                normalizing, NoCorrections.PronunciationProvider(), new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                new FfmpegAudioMixer(new FakeLoudnessAnalyzer()),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = outputDirectory, Format = "wav" }),
                new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()), KokoroVoiceResolver.Instance,
                NullLogger<CrosstalkAssembler>.Instance);

            await assembler.AssembleCastAsync(
                new CastAssemblyRequest(
                    [new CastLine("ANNOUNCER", "[laugh] Everything must go [sigh] today.")],
                    [new CastMember("ANNOUNCER", new VoiceSpec("chatterbox", "announcer", 1.0, "en"))],
                    30.0, new AudioTags("GWAV", "Spot"), outputDirectory),
                CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            DeleteIfThere(outputDirectory);
            DeleteIfThere(engine.OutputDirectory);
            return Task.CompletedTask;
        }

        [Fact]
        public void TheEngineReceivesNoBracketTag() => Assert.DoesNotContain("[", Assert.Single(engine.Contexts).Text);
    }

    // Hazard: a fallback engine renders text that was gated for the primary
    public sealed class ScenarioTheEngineBoundaryHoldsTheLine : IAsyncLifetime
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), "gw-story558-" + Guid.NewGuid().ToString("N"));
        readonly List<string> inputs = [];

        public async Task InitializeAsync()
        {
            var options = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var handler = new FakeHttpMessageHandler(async (request, ct) =>
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                inputs.Add(JsonDocument.Parse(body).RootElement.GetProperty("input").GetString() ?? "");
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            });
            var client = new SpeechEngineClient(new HttpClient(handler), options);
            var context = new TtsRenderContext("[laugh] hi [dance]", "v", null);

            await client.RenderAsync(Engine("kokoro", VoiceEngineProfiles.Kokoro), context, default);
            await client.RenderAsync(Engine("chatterbox", VoiceEngineProfiles.Chatterbox), context, default);
        }

        public Task DisposeAsync()
        {
            DeleteIfThere(cacheRoot);
            return Task.CompletedTask;
        }

        [Fact]
        public void KokoroReceivesNoBracketTag() => Assert.DoesNotContain("[", inputs[0]);

        [Fact]
        public void ChatterboxKeepsItsOwnTagAndDropsAnUnknownOne() => Assert.Equal("[laugh] hi", inputs[1]);
    }

    static void DeleteIfThere(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
