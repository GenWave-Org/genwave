// STORY-534 — motif-word cap through the writer (PLAN T724): handled like any F218.11 hit.
// entry point: LlmCopyWriter.WriteAsync with a fake LLM, fake utterance store and recording event sink

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureMotifWordCapWriter
{
    const string Kept = "Next up is a real stunner for the very late shift, friends.";
    const string Fresh = "Next up, a real stunner for the late shift.";

    static readonly DateOnly Today = new(2026, 11, 15);

    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    sealed class ThrowingStore : IUtteranceStore
    {
        public Task<long> RecordAsync(NewUtterance utterance, CancellationToken ct) => Task.FromResult(1L);
        public Task MarkAiredAsync(long id, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<UtteranceEntry>> RecentAsync(long personaId, int take, CancellationToken ct) =>
            throw new InvalidOperationException("store down");
        public Task<IReadOnlyList<UtteranceEntry>> SinceAsync(long personaId, DateTimeOffset since, CancellationToken ct) =>
            throw new InvalidOperationException("store down");
        public Task<int> SweepAsync(CancellationToken ct) => Task.FromResult(0);
    }

    static ShiftKey Shift(DateOnly day) => ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, day);

    static UtteranceEntry Line(long id, string text, DateOnly day) =>
        new(id, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn", Shift(day), text, false,
            DateTimeOffset.UnixEpoch, null);

    static RecordingUtteranceStore StoreWith(params UtteranceEntry[] lines)
    {
        var store = new RecordingUtteranceStore();
        store.Seeded.AddRange(lines);
        return store;
    }

    static (SegmentCopy Copy, List<string> Prompts, RecordingStationEventSink Events) Write(IUtteranceStore store, params string[] replies)
    {
        var prompts = new List<string>();
        var events = new RecordingStationEventSink();
        var writer = QuirkSpecHarness.WriterFor(
            QuirkSpecHarness.Card([], null), new NoQuirks(), utteranceStore: store,
            httpOverride: QuirkSpecHarness.ScriptedHttp(prompts, replies), repetitionCheck: new RepetitionCheck(),
            events: events, sentenceTrimmer: new SentenceTrimmer());
        var copy = writer.WriteAsync(QuirkSpecHarness.Request(Shift(Today).ToString()), CancellationToken.None)
            .GetAwaiter().GetResult();
        return (copy, prompts, events);
    }

    static RecordingUtteranceStore ThreeBallroomLines(DateOnly day, string? extra = null) => StoreWith(
        extra is null ? Line(9, "Plain.", day) : Line(9, extra, day),
        Line(1, "Welcome to the ballroom, everybody out there tonight.", day),
        Line(2, "The ballroom is warm and the floor is full.", day),
        Line(3, "Grab a partner because this ballroom never sleeps.", day));

    public sealed class ScenarioTheMotifSentenceIsTrimmed
    {
        readonly (SegmentCopy Copy, List<string> Prompts, RecordingStationEventSink Events) run =
            Write(ThreeBallroomLines(Today), "The ballroom doors are open again. " + Kept);

        [Fact]
        public void TheRemainderAirsWithNoSecondCall() => Assert.Equal((Kept, 1), (run.Copy.Text, run.Prompts.Count));

        [Fact]
        public void TheRowIsAMotifRowSayingTrimmed()
        {
            var caught = Assert.Single(run.Events.Events.OfType<RepeatCaught>());

            Assert.Equal(("", true, RepeatOutcome.Trimmed), (caught.Phrase, caught.MotifWords is ["ballroom"], caught.Outcome));
        }
    }

    public sealed class ScenarioAPhraseAndAMotifWordInOneBreakAreOneRow
    {
        const string Shared = "golden hour groove with the late shift crowd";

        readonly (SegmentCopy Copy, List<string> Prompts, RecordingStationEventSink Events) run =
            Write(ThreeBallroomLines(Today, Shared), "The ballroom doors open. " + Shared + ". " + Kept);

        [Fact]
        public void ExactlyOneRowCarriesBoth()
        {
            var caught = Assert.Single(run.Events.Events.OfType<RepeatCaught>());

            Assert.True(caught.Phrase.Length > 0 && caught.MotifWords is ["ballroom"]);
        }
    }

    public sealed class ScenarioTooLittleLeftMeansAReAsk
    {
        readonly (SegmentCopy Copy, List<string> Prompts, RecordingStationEventSink Events) run =
            Write(ThreeBallroomLines(Today), "The ballroom doors are open again.", Fresh);

        [Fact]
        public void TheSecondCopyAirsAndTheReAskNamesTheWord() =>
            Assert.Equal((Fresh, true), (run.Copy.Text, run.Prompts[1].Contains("do not say: \"ballroom\"", StringComparison.Ordinal)));

        [Fact]
        public void TheRowSaysReAsked() => Assert.Equal(RepeatOutcome.ReAsked, run.Events.Events.OfType<RepeatCaught>().Single().Outcome);
    }

    public sealed class ScenarioAnotherShiftsLinesDoNotCount
    {
        readonly (SegmentCopy Copy, List<string> Prompts, RecordingStationEventSink Events) run =
            Write(ThreeBallroomLines(Today.AddDays(-1)), "The ballroom doors are open again. " + Kept);

        [Fact]
        public void TheCopyAirsUntouched() =>
            Assert.Equal(("The ballroom doors are open again. " + Kept, 1, 0),
                (run.Copy.Text, run.Prompts.Count, run.Events.Events.OfType<RepeatCaught>().Count()));
    }

    public sealed class ScenarioAFailedShiftReadMeansNoMotifCheck
    {
        readonly (SegmentCopy Copy, List<string> Prompts, RecordingStationEventSink Events) run =
            Write(new ThrowingStore(), "The ballroom doors are open again. " + Kept);

        [Fact]
        public void TheBreakAirsAsWritten() =>
            Assert.Equal(("The ballroom doors are open again. " + Kept, 0),
                (run.Copy.Text, run.Events.Events.OfType<RepeatCaught>().Count()));
    }
}
