// STORY-558 — DJs can laugh (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. T759 and T760 activated every fact.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureDjsCanLaugh
{
    const string NineTags =
        "[clear throat] [sigh] [shush] [cough] [groan] [sniff] [gasp] [chuckle] [laugh]";

    static SoundTagOffer? OfferFor(string engine, int cap) =>
        new ActiveEngineSoundTagOfferProvider(
            TestActiveEngine.Named(engine),
            new OptionsMonitorSoundTagCapProvider(new TestOptionsMonitor<TtsSoundTagsOptions>(new TtsSoundTagsOptions { MaxPerBreak = cap })))
            .Current;

    static string DjPrompt(SoundTagOffer? offer) =>
        LlmPromptBuilder.BuildSystemPrompt(personaSection: null, maxCopyChars: 450, soundTags: offer);

    // Today's call shape: no offer argument at all.
    static string TodaysDjPrompt() => LlmPromptBuilder.BuildSystemPrompt(personaSection: null, maxCopyChars: 450);

    static string TwoDjPrompt(SoundTagOffer? offer) =>
        CrosstalkPromptBuilder.BuildSystemPrompt(
            Card("Nova"), Card("Rex"), 30, new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero), soundTags: offer);

    static PersonaCard Card(string name) =>
        new(PersonaCard.CurrentSchemaVersion, name, "", "Soul line.", [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    // AC1 — chatterbox profile, cap 1
    public sealed class ScenarioThePromptOffersTags
    {
        readonly string djPrompt = DjPrompt(OfferFor("chatterbox", 1));
        readonly string twoDjPrompt = TwoDjPrompt(OfferFor("chatterbox", 1));

        [Fact]
        public void TheDjPromptListsTheNineEventTags() => Assert.Contains(NineTags, djPrompt);

        [Fact]
        public void TheDjPromptStatesTheCap() => Assert.Contains("Use at most 1 of these exact tags", djPrompt);

        [Fact]
        public void TheTwoDjPromptListsTheTags() => Assert.Contains(NineTags, twoDjPrompt);

        [Fact]
        public void TheTwoDjPromptCapsTheWholeExchange() => Assert.Contains("Use at most 1 of these exact tags in the whole exchange", twoDjPrompt);
    }

    // The patter path (LlmCopyWriter to BreakPromptComposer) carries the offer into the prompt it sends
    public sealed class ScenarioThePatterWriterSendsTheOffer
    {
        readonly List<string> systemPrompts = [];

        public ScenarioThePatterWriterSendsTheOffer()
        {
            var writer = QuirkSpecHarness.WriterFor(
                QuirkSpecHarness.Card([], null), new NoQuirks(),
                onSystemPrompt: prompt =>
                {
                    systemPrompts.Add(prompt);
                    return Task.CompletedTask;
                },
                soundTagOffers: new ActiveEngineSoundTagOfferProvider(
                    TestActiveEngine.Named("chatterbox"),
                    new OptionsMonitorSoundTagCapProvider(new TestOptionsMonitor<TtsSoundTagsOptions>(new TtsSoundTagsOptions { MaxPerBreak = 1 }))));

            writer.WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheSystemPromptListsTheNineEventTags() => Assert.Contains(NineTags, systemPrompts[0]);
    }

    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    // The caller reads the cap fresh on every prompt
    public sealed class ScenarioALiveCapChangeReachesTheNextPrompt
    {
        readonly List<string> systemPrompts = [];

        public ScenarioALiveCapChangeReachesTheNextPrompt()
        {
            var options = new TestOptionsMonitor<TtsSoundTagsOptions>(new TtsSoundTagsOptions { MaxPerBreak = 1 });
            var writer = new CrosstalkScriptWriter(
                QuirkSpecHarness.ScriptedHttp([], ["not a script"], systemPrompts),
                new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
                new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
                new LlmCallRecorder(
                    new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())), new LlmCallCauseCounters(TimeProvider.System)),
                new FakeDegradationModeReader(),
                NullLogger<CrosstalkScriptWriter>.Instance,
                TimeProvider.System,
                soundTagOffers: new ActiveEngineSoundTagOfferProvider(
                    TestActiveEngine.Named("chatterbox"), new OptionsMonitorSoundTagCapProvider(options)));
            var request = new CrosstalkExchangeRequest(
                Card("Nova"), Card("Rex"), "GenWave", ShowName: "Night Shift", Daypart: null,
                StationLocalNow: new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero),
                HostPersonaId: 7, NeighborPersonaId: 8, ShowId: 3);

            writer.WriteExchangeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            options.CurrentValue = new TtsSoundTagsOptions { MaxPerBreak = 2 };
            writer.WriteExchangeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void TheSecondPromptStatesTheNewCap() => Assert.Contains("Use at most 2 of these exact tags in the whole exchange", systemPrompts[^1]);
    }

    // AC2 — "[laugh] hi [sigh] [dance]", cap 1
    public sealed class ScenarioTagsKeptUpToTheCap
    {
        readonly string text = SoundTags.Apply("[laugh] hi [sigh] [dance]", VoiceEngineProfiles.Chatterbox, cap: 1);

        [Fact]
        public void TheTextIsLaughHi() => Assert.Equal("[laugh] hi", text);
    }

    // AC3 — "[clear throat] hello"
    public sealed class ScenarioTheNormaliserKeepsThem
    {
        readonly string normalized = SpeechText.Normalize("[clear throat] hello", SpeechCorrectionSet.Empty);

        [Fact]
        public void TheTagIsByteIntact() => Assert.Equal("[clear throat] hello", normalized);
    }

    // AC4 — a line that differs from a recent one only by a tag
    public sealed class ScenarioChecksIgnoreThem
    {
        const string Recent = "Now let me tell you about the odd little shop around the corner today.";
        const string Plain = "Let me tell you about the odd little shop around the corner.";
        const string Tagged = "Let me tell you about the [sigh] odd little shop around the corner.";

        readonly CrosstalkLineMemory memory = new(
            new RepetitionCheck(), utteranceStore: null, new RecordingStationEventSink(), NullLogger.Instance);

        readonly CrosstalkLineMemory.SpeakerHistory history =
            new(CrosstalkSpeaker.Host, 7, "Nova", [Recent]);

        readonly ClaimCheckResult gate = LlmCopyWriter.CheckTruthGate(
            "The show [sigh] starts at nine tonight.", factBlock: null,
            new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.Zero), trackTitle: null, stationName: "GenWave",
            requiredCore: "the show starts at nine");

        IReadOnlyList<string> PhrasesOf(string line) =>
            memory.FindRepeats(
                    new CrosstalkAiredScript([new CrosstalkAiredLine(CrosstalkSpeaker.Host, line, false)]),
                    "GenWave", [history])
                .SelectMany(hit => hit.Phrases)
                .ToList();

        [Fact]
        public void TheRepetitionCheckSeesTheSameWords() => Assert.Equal(PhrasesOf(Plain), PhrasesOf(Tagged));

        [Fact]
        public void TheTruthGateReadsTagFreeCopy() => Assert.True(gate.Passed);
    }

    // Sad path
    // AC5, AC6
    public sealed class ScenarioTagsOff
    {
        // The Story548 full-text pin is the byte guarantee; these two compare against the builder called as before tags existed.
        readonly string today = TodaysDjPrompt();

        [Fact]
        public void WithCapZeroTheDjPromptIsByteEqualToTodays() => Assert.Equal(today, DjPrompt(OfferFor("chatterbox", 0)));

        [Fact]
        public void OnKokoroThePromptIsByteEqualToTodays() => Assert.Equal(today, DjPrompt(OfferFor("kokoro", 1)));

        [Fact]
        public void OnKokoroTheTextIsHi() =>
            Assert.Equal("hi", SoundTags.Apply("[laugh] hi [sigh] [dance]", VoiceEngineProfiles.Kokoro, cap: 1));
    }
}
