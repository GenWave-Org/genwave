// STORY-545 — Cut the sentence, keep the DJ (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T714)
//
// BDD specification — xUnit. T714 activated the facts below.
// entry point: LlmCopyWriter.WriteAsync / WritePreviewAsync with a fake LLM, fake utterance store and fake booth log

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureCutTheSentenceKeepTheDJWriter
{
    const string Recent = "Here comes a golden hour groove from the vault tonight.";
    const string Repeated = "Settle in for a golden hour groove from the vault, folks.";
    const string Kept = "Next up is a real stunner for the very late shift, friends.";
    const string Fresh = "Next up, a real stunner for the late shift.";
    const string OneRepeatedSentence = Repeated + " " + Kept;

    /// <summary>One on-air WriteAsync against a fake LLM that answers <c>replies</c> in order; the persona's newest line is <see cref="Recent"/>.</summary>
    sealed class Run
    {
        public required SegmentCopy Copy { get; init; }
        public required List<string> Prompts { get; init; }
        public required RecordingUtteranceStore Store { get; init; }
        public required RecordingStationEventSink Events { get; init; }

        public static Run Arrange(
            string[] replies, ISentenceTrimmer? trimmer = null, MediaItem? track = null, string? recent = null,
            IStationClockProvider? clock = null, PersonaCard? card = null)
        {
            var prompts = new List<string>();
            var store = new RecordingUtteranceStore();
            store.Seeded.Add(new UtteranceEntry(
                1, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)), recent ?? Recent, false,
                DateTimeOffset.UnixEpoch, null));
            var events = new RecordingStationEventSink();
            var writer = QuirkSpecHarness.WriterFor(
                card ?? QuirkSpecHarness.Card([], null), new NoQuirks(), utteranceStore: store,
                httpOverride: QuirkSpecHarness.ScriptedHttp(prompts, replies), repetitionCheck: new RepetitionCheck(),
                events: events, sentenceTrimmer: trimmer ?? new SentenceTrimmer(), stationClock: clock);
            var request = QuirkSpecHarness.Request(
                ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)).ToString());
            if (track is not null)
                request = request with { Track = track };
            var copy = writer.WriteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            return new Run { Copy = copy, Prompts = prompts, Store = store, Events = events };
        }

        public RepeatOutcome? LastOutcome => Events.Events.OfType<RepeatCaught>().Last().Outcome;
    }

    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    /// <summary>A trimmer that hands back fixed text, to prove the writer re-checks what the trimmer returns.</summary>
    sealed class FixedTrimmer(string text) : ISentenceTrimmer
    {
        public TrimResult Trim(string copy, IReadOnlyList<RepetitionHit> hits) => new(text, 1);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC4 — a trimmed line airs with no second call
    public sealed class ScenarioATrimmedLineAirsWithNoSecondCall
    {
        // Given a fake LLM whose copy has one repeated sentence and 12 other words
        // When  WriteAsync runs on air
        readonly Run run = Run.Arrange([OneRepeatedSentence]);

        [Fact]
        public void TheStoredUtteranceIsTheCopyWithoutThatSentence() => Assert.Equal(Kept, Assert.Single(run.Store.Recorded).Text);

        [Fact]
        public void TheLLMWasCalledExactlyOnce() => Assert.Single(run.Prompts);

        [Fact]
        public void TheUtteranceHasFallbackFalse() => Assert.False(Assert.Single(run.Store.Recorded).Fallback);
    }

    // AC5 — the booth row says trimmed
    public sealed class ScenarioTheBoothRowSaysTrimmed
    {
        // Given that trimmed break
        // When  the booth log is read (the row's summary is built from the event's outcome; see Story534_RepeatCaughtBoothRow)
        readonly Run run = Run.Arrange([OneRepeatedSentence]);

        [Fact]
        public void TheRepeatCaughtEventSaysTrimmed() => Assert.Equal(RepeatOutcome.Trimmed, Assert.Single(run.Events.Events.OfType<RepeatCaught>()).Outcome);
    }

    // AC6 — too little left means one re-ask
    public sealed class ScenarioTooLittleLeftMeansOneReAsk
    {
        // Given a fake LLM whose first copy is one repeated sentence and whose second is fresh
        // When  WriteAsync runs on air
        readonly Run run = Run.Arrange([Repeated, Fresh]);

        [Fact]
        public void TheSecondCopyIsStoredAndRendered() =>
            Assert.Equal((Fresh, Fresh), (run.Copy.Text, Assert.Single(run.Store.Recorded).Text));

        [Fact]
        public void TheRepeatCaughtRowsSummaryEndsWithReAsked() => Assert.Equal(RepeatOutcome.ReAsked, run.LastOutcome);
    }

    // AC7 — the re-ask is trimmed too
    public sealed class ScenarioTheReAskIsTrimmedToo
    {
        // Given a fake LLM whose first copy is all repeats and whose second has one repeated sentence and 12 other words
        // When  WriteAsync runs on air
        readonly Run run = Run.Arrange([Repeated, OneRepeatedSentence]);

        [Fact]
        public void TheStoredUtteranceIsTheSecondCopyWithoutThatSentence() => Assert.Equal(Kept, Assert.Single(run.Store.Recorded).Text);

        [Fact]
        public void TheLLMWasCalledExactlyTwice() => Assert.Equal(2, run.Prompts.Count);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC9 — nothing usable twice means the template
    public sealed class ScenarioNothingUsableTwiceMeansTheTemplate
    {
        // Given a fake LLM that returns the same all-repeat copy twice
        // When  WriteAsync runs on air
        readonly Run run = Run.Arrange([Repeated, Repeated]);

        [Fact]
        public void TheTemplateFallbackIsRendered() => Assert.Equal("Coming up: Astral Plane by Valerie June.", run.Copy.Text);

        [Fact]
        public void TheRepeatCaughtRowsSummaryEndsWithTemplate() => Assert.Equal(RepeatOutcome.Template, run.LastOutcome);

        [Fact]
        public void TheLLMWasCalledExactlyTwice() => Assert.Equal(2, run.Prompts.Count);
    }

    // AC10 — seven words is too little
    public sealed class ScenarioSevenWordsIsTooLittle
    {
        // Given a copy whose remainder after the trim is one seven-word sentence
        // When  WriteAsync runs on air
        readonly Run run = Run.Arrange([Repeated + " Next up is a real stunner, folks.", Fresh]);

        [Fact]
        public void TheLLMIsAskedAgain() => Assert.Equal(2, run.Prompts.Count);
    }

    // AC11 — a remainder the truth gate rejects is not aired
    public sealed class ScenarioARemainderTheTruthGateRejectsIsNotAired
    {
        // Given a trimmed remainder that fails the truth gate (a trimmer double hands back a wrong weekday)
        // When  WriteAsync runs on air
        static readonly string WrongDay = $"Happy {DateTime.UtcNow.AddDays(3).DayOfWeek}, and here is a real stunner for the late shift.";
        readonly Run run = Run.Arrange([OneRepeatedSentence, Fresh], trimmer: new FixedTrimmer(WrongDay));

        [Fact]
        public void TheLLMIsAskedAgain() => Assert.Equal(2, run.Prompts.Count);
    }

    // AC12 — preview never trims
    public sealed class ScenarioPreviewNeverTrims
    {
        // Given a preview request whose copy repeats
        // When  WritePreviewAsync runs
        readonly PersonaPreviewResult result;

        public ScenarioPreviewNeverTrims()
        {
            var store = new RecordingUtteranceStore();
            store.Seeded.Add(new UtteranceEntry(
                1, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)), Recent, false,
                DateTimeOffset.UnixEpoch, null));
            var writer = QuirkSpecHarness.WriterFor(
                QuirkSpecHarness.Card([], null), new NoQuirks(), utteranceStore: store,
                httpOverride: QuirkSpecHarness.ScriptedHttp([], [OneRepeatedSentence]), repetitionCheck: new RepetitionCheck(),
                events: new RecordingStationEventSink(), sentenceTrimmer: new SentenceTrimmer());
            result = writer.WritePreviewAsync(QuirkSpecHarness.Request(null), personaOverride: null, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        [Fact]
        public void TheCopyComesBackWhole() => Assert.Equal(new PersonaPreviewResult.Success(OneRepeatedSentence), result);
    }

    // AC13 — the trim may not lose the song
    public sealed class ScenarioTheTrimMayNotLoseTheSong
    {
        // Given the copy "That was ABBA's Knowing Me, Knowing You, nineteen seventy-six, naturally. What a record. Stay right here, there is plenty more coming up tonight." with "naturally" over quota (a capped quirk the selector did not hand this break)
        // When  WriteAsync runs on air
        const string Quirk = "Says \"naturally\" after a year";
        const string Copy = "That was ABBA's Knowing Me, Knowing You, nineteen seventy-six, naturally. What a record. Stay right here, there is plenty more coming up tonight.";
        readonly Run run = Run.Arrange(
            [Copy, Fresh],
            track: new MediaItem("m1", "/media/x.mp3", "Knowing Me, Knowing You", default, "ABBA"),
            card: QuirkSpecHarness.Card([Quirk], [new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: 1.0)]),
            clock: new FakeStationClockProvider(new DateTimeOffset(2026, 11, 11, 22, 0, 0, TimeSpan.Zero)));

        [Fact]
        public void TheLLMIsAskedAgain() =>
            Assert.Contains("do not say:", Assert.Single(run.Prompts.Skip(1)), StringComparison.Ordinal);
    }
}
