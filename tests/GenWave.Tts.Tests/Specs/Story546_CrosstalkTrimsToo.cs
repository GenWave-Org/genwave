// STORY-546 — Crosstalk trims too (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T715)
//
// Entry point: CrosstalkScriptWriter.WriteExchangeAsync, with a scripted LLM, a seeded utterance store,
// the real RepetitionCheck and SentenceTrimmer, and a recording event sink.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureCrosstalkTrimsToo
{
    const long HostId = 7;
    const long NeighborId = 8;

    const string HostOne = "HOST: Welcome back everybody, the lights are low and the records are warm.";
    const string NeighborOne = "NEIGHBOR: Always a pleasure to sit beside you on a night like this.";
    const string NeighborTwo = "NEIGHBOR: Funny, I walked past that very place on my way in.";

    const string RepeatedSentence = "Let me tell you about the odd little shop around the corner.";
    const string FreshSentence = "Socks, though.";
    const string NeighborRepeatedSentence = "Nobody ever mentions the lighthouse keeper and his terrible accordion.";
    const string NeighborHistory = "Remember the lighthouse keeper and his terrible accordion, folks, from last week.";
    const string HostHistory = "Now let me tell you about the odd little shop around the corner today.";

    static string Script(params string[] lines) => string.Join('\n', lines);

    static string TextOf(string taggedLine) => taggedLine[(taggedLine.IndexOf(':') + 1)..].Trim();

    sealed record Run(CrosstalkWriteResult Result, List<string> UserPrompts, IReadOnlyList<RepeatCaught> Caught)
    {
        public IEnumerable<RepeatCaught> CaughtFor(long personaId) => Caught.Where(c => c.PersonaId == personaId);

        public IReadOnlyList<CrosstalkAiredLine> Lines => (Result as CrosstalkWriteResult.Accepted)?.Script.Lines ?? [];
    }

    static PersonaCard CardNamed(string name) =>
        new(PersonaCard.CurrentSchemaVersion, name, "", "Soul line.", [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    static Run Write(params string[] replies)
    {
        var prompts = new List<string>();
        var store = new RecordingUtteranceStore();
        store.Seeded.Add(new UtteranceEntry(
            1, HostId, null, UtteranceKind.Patter, "LeadIn",
            ShiftKey.Unscheduled(HostId, new DateOnly(2026, 11, 9)), HostHistory, false, DateTimeOffset.UnixEpoch, null));
        store.Seeded.Add(new UtteranceEntry(
            2, NeighborId, null, UtteranceKind.Patter, "LeadIn",
            ShiftKey.Unscheduled(NeighborId, new DateOnly(2026, 11, 9)), NeighborHistory, false, DateTimeOffset.UnixEpoch, null));
        var sink = new RecordingStationEventSink();
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        var writer = new CrosstalkScriptWriter(
            QuirkSpecHarness.ScriptedHttp(prompts, replies),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            NullLogger<CrosstalkScriptWriter>.Instance,
            TimeProvider.System,
            repetitionCheck: new RepetitionCheck(),
            utteranceStore: store,
            events: sink,
            sentenceTrimmer: new SentenceTrimmer());

        var request = new CrosstalkExchangeRequest(
            CardNamed("Nova"), CardNamed("Rex"), "GenWave", ShowName: "Night Shift", Daypart: null,
            StationLocalNow: new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero),
            HostPersonaId: HostId, NeighborPersonaId: NeighborId, ShowId: 3);
        var result = writer.WriteExchangeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return new Run(result, prompts, sink.Events.OfType<RepeatCaught>().ToList());
    }

    static string WithHostTwo(string hostTwo) => Script(HostOne, NeighborOne, hostTwo, NeighborTwo);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — a line keeps its fresh sentence
    public sealed class ScenarioALineKeepsItsFreshSentence
    {
        // Given speaker A's line has two sentences and the first repeats A's history
        // When  the exchange is assembled

        readonly Run run = Write(WithHostTwo($"HOST: {RepeatedSentence} {FreshSentence}"));

        [Fact]
        public void AsLineIsItsSecondSentence() =>
            Assert.Equal([TextOf(HostOne), FreshSentence], run.Lines.Where(l => l.Speaker == CrosstalkSpeaker.Host).Select(l => l.Text));

        [Fact]
        public void TheLLMWasCalledExactlyOnce() => Assert.Single(run.UserPrompts);

        [Fact]
        public void AsRowSaysTrimmed() => Assert.Equal(RepeatOutcome.Trimmed, Assert.Single(run.CaughtFor(HostId)).Outcome);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC2 — an empty line goes to the re-ask
    public sealed class ScenarioAnEmptyLineGoesToTheReAsk
    {
        // Given speaker A's line is one repeated sentence
        // When  the exchange is written

        readonly Run run = Write(
            WithHostTwo($"HOST: {RepeatedSentence}"),
            WithHostTwo($"HOST: {FreshSentence}"));

        [Fact]
        public void TheExchangeIsAskedAgainOnce() => Assert.Equal(2, run.UserPrompts.Count);

        [Fact]
        public void AsRowSaysReAsked() => Assert.Equal(RepeatOutcome.ReAsked, Assert.Single(run.CaughtFor(HostId)).Outcome);
    }

    // AC3 — still empty means dropped
    public sealed class ScenarioStillEmptyMeansDropped
    {
        // Given speaker A's line is all repeats after the re-ask
        // When  the exchange is assembled

        static readonly string AllRepeats = WithHostTwo($"HOST: {RepeatedSentence}");

        readonly Run run = Write(AllRepeats, AllRepeats);

        [Fact]
        public void AsLineIsAbsentAndBsLinesAreIntact() =>
            Assert.Equal(
                [(CrosstalkSpeaker.Host, TextOf(HostOne)), (CrosstalkSpeaker.Neighbor, TextOf(NeighborOne)),
                 (CrosstalkSpeaker.Neighbor, TextOf(NeighborTwo))],
                run.Lines.Select(l => (l.Speaker, l.Text)));

        [Fact]
        public void TheDroppedSpeakersRowSaysDropped() => Assert.Equal(RepeatOutcome.Dropped, run.CaughtFor(HostId).Last().Outcome);
    }

    // AC4 — a line trimmed after the re-ask still airs
    public sealed class ScenarioALineIsTrimmedAfterTheReAsk
    {
        // Given the first pass empties A's line
        //  And  the re-ask has A's line with a repeated sentence and a fresh one
        // When  the exchange is assembled

        readonly Run run = Write(
            WithHostTwo($"HOST: {RepeatedSentence}"),
            WithHostTwo($"HOST: {RepeatedSentence} {FreshSentence}"));

        [Fact]
        public void AsLineAirsAsTheFreshSentence() =>
            Assert.Equal([TextOf(HostOne), FreshSentence], run.Lines.Where(l => l.Speaker == CrosstalkSpeaker.Host).Select(l => l.Text));

        [Fact]
        public void BsLinesAreIntact() =>
            Assert.Equal([TextOf(NeighborOne), TextOf(NeighborTwo)], run.Lines.Where(l => l.Speaker == CrosstalkSpeaker.Neighbor).Select(l => l.Text));

        [Fact]
        public void TheLLMWasCalledExactlyTwice() => Assert.Equal(2, run.UserPrompts.Count);
    }

    // AC5 — each speaker's row carries its own outcome
    public sealed class ScenarioEachSpeakersRowCarriesItsOwnOutcome
    {
        // Given the first pass empties A's line
        //  And  after the re-ask A's line is trimmed and kept while one of B's two lines is all repeats
        // When  the exchange is assembled

        readonly Run run = Write(
            WithHostTwo($"HOST: {RepeatedSentence}"),
            Script(HostOne, NeighborOne, $"HOST: {RepeatedSentence} {FreshSentence}", $"NEIGHBOR: {NeighborRepeatedSentence}"));

        [Fact]
        public void AsRowSaysTrimmed() => Assert.Equal(RepeatOutcome.Trimmed, run.CaughtFor(HostId).Last().Outcome);

        [Fact]
        public void BsRowSaysDropped() => Assert.Equal(RepeatOutcome.Dropped, Assert.Single(run.CaughtFor(NeighborId)).Outcome);
    }

    // AC6 — a first pass that is re-asked throws away every row's line, not just the emptied one
    public sealed class ScenarioTheFirstPassIsReAskedForBothSpeakers
    {
        // Given the first pass empties A's line
        //  And  B's hit line keeps a fresh sentence
        // When  the exchange is written (the re-ask is clean)

        readonly Run run = Write(
            Script(HostOne, NeighborOne, $"HOST: {RepeatedSentence}", $"NEIGHBOR: {NeighborRepeatedSentence} {FreshSentence}"),
            WithHostTwo($"HOST: {FreshSentence}"));

        [Fact]
        public void BsFirstPassRowSaysReAsked() => Assert.Equal(RepeatOutcome.ReAsked, Assert.Single(run.CaughtFor(NeighborId)).Outcome);
    }

    // AC7 — one speaker can be both trimmed and dropped after the re-ask
    public sealed class ScenarioOneSpeakerHasAKeptAndADroppedLine
    {
        // Given the first pass empties A's line
        //  And  after the re-ask one of A's lines keeps a fresh sentence and another is all repeats
        //  And  the exchange still has enough lines and both speakers
        // When  the exchange is assembled

        readonly Run run = Write(
            WithHostTwo($"HOST: {RepeatedSentence}"),
            Script(HostOne, NeighborOne, $"HOST: {RepeatedSentence} {FreshSentence}", NeighborTwo, $"HOST: {RepeatedSentence}"));

        IEnumerable<RepeatCaught> AfterReAsk => run.CaughtFor(HostId).Skip(1);

        [Fact]
        public void AHasATrimmedRow() => Assert.Contains(AfterReAsk, c => c.Outcome == RepeatOutcome.Trimmed);

        [Fact]
        public void AHasADroppedRow() => Assert.Contains(AfterReAsk, c => c.Outcome == RepeatOutcome.Dropped);

        [Fact]
        public void AHasTwoRowsAfterTheReAsk() => Assert.Equal(2, AfterReAsk.Count());
    }

    // AC8 — a discarded exchange airs nothing, so nothing is Trimmed
    public sealed class ScenarioADiscardedExchangeWritesNoTrimmedRow
    {
        // Given the first pass empties A's line
        //  And  after the re-ask A's line is trimmed but all of B's lines are repeats
        // When  the survivor checks discard the exchange

        readonly Run run = Write(
            WithHostTwo($"HOST: {RepeatedSentence}"),
            Script(HostOne, $"NEIGHBOR: {NeighborRepeatedSentence}", $"HOST: {RepeatedSentence} {FreshSentence}", $"NEIGHBOR: {NeighborRepeatedSentence}"));

        [Fact]
        public void TheExchangeIsDiscarded() => Assert.IsType<CrosstalkWriteResult.Discarded>(run.Result);

        [Fact]
        public void AsRowSaysDropped() => Assert.Equal(RepeatOutcome.Dropped, run.CaughtFor(HostId).Last().Outcome);

        [Fact]
        public void NoTrimmedRowWasPublished() => Assert.DoesNotContain(run.Caught, c => c.Outcome == RepeatOutcome.Trimmed);
    }
}
