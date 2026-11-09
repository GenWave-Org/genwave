// STORY-535 — Crosstalk does not repeat either (gh-#293 gh-#920 · SPEC F218.14 · PLAN T694)
//
// Entry point: CrosstalkScriptWriter.WriteExchangeAsync, with a scripted LLM, a recording utterance
// store seeded per persona, the real RepetitionCheck and a recording event sink.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureCrosstalkDoesNotRepeatEither
{
    const long HostId = 7;
    const long NeighborId = 8;
    const long ShowId = 3;

    const string HostOne = "HOST: Welcome back everybody, the lights are low and the records are warm.";
    const string NeighborOne = "NEIGHBOR: Always a pleasure to sit beside you on a night like this.";
    const string HostTwo = "HOST: Let me tell you about the odd little shop around the corner.";
    const string NeighborTwo = "NEIGHBOR: Funny, I walked past that very place on my way in.";

    /// <summary>Said by the host's own history, near-verbatim in <see cref="HostTwo"/>.</summary>
    const string HostHistory = "Now let me tell you about the odd little shop around the corner today.";
    /// <summary>Said by the neighbour's own history, copied by the host in the crossed exchange.</summary>
    const string NeighborHistory = "Sometimes the quiet hours on this station feel like a secret handshake.";

    const string FreshHostTwo = "HOST: Here is something nobody asked for, a long story about socks.";

    static string Script(params string[] lines) => string.Join('\n', lines);

    static string TextOf(string taggedLine) => taggedLine[(taggedLine.IndexOf(':') + 1)..].Trim();

    sealed record Run(
        CrosstalkWriteResult Result, List<string> UserPrompts, RecordingUtteranceStore Store,
        RecordingStationEventSink Events, LlmCallRing Ring)
    {
        public IReadOnlyList<CrosstalkAiredLine> Lines => (Result as CrosstalkWriteResult.Accepted)?.Script.Lines ?? [];

        public IEnumerable<string> TextsOf(CrosstalkSpeaker speaker) =>
            Lines.Where(l => l.Speaker == speaker).Select(l => l.Text);
    }

    static UtteranceEntry Seed(long id, long personaId, string text) =>
        new(id, personaId, null, UtteranceKind.Patter, "LeadIn",
            ShiftKey.Unscheduled(personaId, new DateOnly(2026, 11, 9)), text, false, DateTimeOffset.UnixEpoch, null);

    static PersonaCard CardNamed(string name) =>
        new(PersonaCard.CurrentSchemaVersion, name, "", "Soul line.", [],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    /// <param name="histories">Seeded into the store as given; each persona only ever reads its own.</param>
    static Run Write(
        string[] replies, IReadOnlyList<UtteranceEntry> histories, bool withIds = true, bool withMemory = true)
    {
        var prompts = new List<string>();
        var store = new RecordingUtteranceStore();
        store.Seeded.AddRange(histories);
        var events = new RecordingStationEventSink();
        var ring = new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions()));
        var writer = new CrosstalkScriptWriter(
            QuirkSpecHarness.ScriptedHttp(prompts, replies),
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
            new TestOptionsMonitor<CrosstalkOptions>(new CrosstalkOptions()),
            new LlmCallRecorder(ring, new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(),
            NullLogger<CrosstalkScriptWriter>.Instance,
            TimeProvider.System,
            repetitionCheck: withMemory ? new RepetitionCheck() : null,
            utteranceStore: withMemory ? store : null,
            events: events);

        var request = new CrosstalkExchangeRequest(
            CardNamed("Nova"), CardNamed("Rex"), "GenWave", ShowName: "Night Shift", Daypart: null,
            StationLocalNow: new DateTimeOffset(2026, 11, 9, 22, 0, 0, TimeSpan.Zero),
            HostPersonaId: withIds ? HostId : null, NeighborPersonaId: withIds ? NeighborId : null,
            ShowId: withIds ? ShowId : null);
        var result = writer.WriteExchangeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return new Run(result, prompts, store, events, ring);
    }

    static readonly UtteranceEntry[] OwnHistories =
    [
        Seed(1, HostId, HostHistory),
        Seed(2, NeighborId, NeighborHistory),
    ];

    static readonly string FourLines = Script(HostOne, NeighborOne, HostTwo, NeighborTwo);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — each speaker, their own history
    public sealed class ScenarioEachSpeakerTheirOwnHistory
    {
        // Given personas A and B with their own utterances, each said by the OTHER speaker's line now
        // When  an exchange is written

        // A's line copies B's history and B's line copies A's history: checked against the wrong
        // speaker's history they WOULD hit; checked against their own they do not.
        static readonly string Crossed = Script(
            HostOne,
            "NEIGHBOR: Now let me tell you about the odd little shop around the corner today.",
            "HOST: Sometimes the quiet hours on this station feel like a secret handshake, friends.",
            NeighborTwo);

        readonly Run run = Write([Crossed], OwnHistories);

        [Fact]
        public void ASLinesAreCheckedOnlyAgainstASHistoryAndBSAgainstBS() =>
            Assert.Equal(
                (1, 4, 0),
                (run.UserPrompts.Count, run.Lines.Count, run.Events.Events.OfType<RepeatCaught>().Count()));
    }

    // AC2 — each line is on the record
    public sealed class ScenarioEachLineIsOnTheRecord
    {
        // Given an accepted exchange of four lines
        // When  it is stored

        readonly Run run = Write([FourLines], []);

        [Fact]
        public void FourUtteranceRowsExistKindCrosstalkSegmentKindCrosstalkLineTwoPerPersona() =>
            Assert.Equal(
                [(HostId, UtteranceKind.Crosstalk, "CrosstalkLine"), (HostId, UtteranceKind.Crosstalk, "CrosstalkLine"),
                 (NeighborId, UtteranceKind.Crosstalk, "CrosstalkLine"), (NeighborId, UtteranceKind.Crosstalk, "CrosstalkLine")],
                run.Store.Recorded.Select(u => (u.PersonaId, u.Kind, u.SegmentKind)).OrderBy(t => t.PersonaId));

        [Fact]
        public void TheRowsCarryTheRawLineTextsInOrder() =>
            Assert.Equal(
                [TextOf(HostOne), TextOf(NeighborOne), TextOf(HostTwo), TextOf(NeighborTwo)],
                run.Store.Recorded.Select(u => u.Text));

        [Fact]
        public void TheRowsAreNotFallbacksAndCarryTheShow() =>
            Assert.Equal(
                Enumerable.Repeat((false, (long?)ShowId), 4),
                run.Store.Recorded.Select(u => (u.Fallback, u.ShowId)));
    }

    // AC1 (clears on re-ask) — one catch, the re-ask is fresh
    public sealed class ScenarioAReAskClearsTheRepeat
    {
        // Given speaker A's first line repeats A's history and the re-ask does not
        // When  an exchange is written

        static readonly string Repeating = Script(HostOne, NeighborOne, HostTwo, NeighborTwo);
        static readonly string Fresh = Script(HostOne, NeighborOne, FreshHostTwo, NeighborTwo);

        readonly Run run = Write([Repeating, Fresh], OwnHistories);

        [Fact]
        public void TheLlmWasCalledTwice() => Assert.Equal(2, run.UserPrompts.Count);

        [Fact]
        public void TheReAskNamesTheRepeatedPhrase() =>
            Assert.Contains("do not say: \"let me tell you about the odd little shop around the corner\"", run.UserPrompts[1]);

        [Fact]
        public void OneRepeatCaughtNamesPersonaAAndThePhrase() =>
            Assert.Equal(
                [(HostId, "let me tell you about the odd little shop around the corner")],
                run.Events.Events.OfType<RepeatCaught>().Select(e => (e.PersonaId, e.Phrase)));

        [Fact]
        public void TheCaughtFirstCallIsInTheRingWithCauseRepeat() =>
            Assert.Equal(
                [LlmCallCause.Repeat, LlmCallCause.Success],
                run.Ring.Snapshot().OrderBy(r => r.Seq).Select(r => r.Cause));

        [Fact]
        public void TheFreshReplyIsWhatAirs() =>
            Assert.Equal([TextOf(HostOne), TextOf(FreshHostTwo)], run.TextsOf(CrosstalkSpeaker.Host));

        [Fact]
        public void TheFreshReplyIsWhatIsStored() =>
            Assert.Equal(
                [TextOf(HostOne), TextOf(NeighborOne), TextOf(FreshHostTwo), TextOf(NeighborTwo)],
                run.Store.Recorded.Select(u => u.Text));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC3 — a stubborn line is dropped, not replaced
    public sealed class ScenarioAStubbornLineIsDroppedNotReplaced
    {
        // Given speaker A's line repeats after one re-ask
        // When  the exchange is assembled

        readonly Run run = Write([FourLines, FourLines], OwnHistories);

        [Fact]
        public void ASLineIsAbsentAndBSLinesAreIntact() =>
            Assert.Equal(
                [(CrosstalkSpeaker.Host, TextOf(HostOne)), (CrosstalkSpeaker.Neighbor, TextOf(NeighborOne)),
                 (CrosstalkSpeaker.Neighbor, TextOf(NeighborTwo))],
                run.Lines.Select(l => (l.Speaker, l.Text)));

        [Fact]
        public void NoTemplateTextIsInserted() =>
            Assert.Empty(run.Lines.Select(l => l.Text).Except(
                [TextOf(HostOne), TextOf(NeighborOne), TextOf(HostTwo), TextOf(NeighborTwo)]));

        [Fact]
        public void TheLlmWasCalledExactlyTwice() => Assert.Equal(2, run.UserPrompts.Count);

        [Fact]
        public void EachCaughtAttemptIsLoggedForPersonaA() =>
            Assert.Equal(
                [HostId, HostId],
                run.Events.Events.OfType<RepeatCaught>().Select(e => e.PersonaId));

        [Fact]
        public void OnlyTheSurvivingLinesAreStored() =>
            Assert.Equal(
                [TextOf(HostOne), TextOf(NeighborOne), TextOf(NeighborTwo)],
                run.Store.Recorded.Select(u => u.Text));
    }

    // AC3 — a drop that leaves too little of an exchange discards it
    public sealed class ScenarioTheDropLeavesTooFewLines
    {
        // Given one line of each speaker repeats after the re-ask, leaving two lines (both speakers still present)
        // When  the exchange is assembled

        static readonly string TwoRepeating = Script(
            "HOST: Now let me tell you about the odd little shop around the corner today, friends.",
            NeighborOne,
            FreshHostTwo,
            "NEIGHBOR: Sometimes the quiet hours on this station feel like a secret handshake, again.");

        readonly Run run = Write([TwoRepeating, TwoRepeating], OwnHistories);

        [Fact]
        public void TheExchangeIsDiscardedAsARepeat() =>
            Assert.Equal(LlmCallCause.Repeat, (run.Result as CrosstalkWriteResult.Discarded)?.Cause);

        [Fact]
        public void ADiscardedExchangeStoresNoRows() => Assert.Empty(run.Store.Recorded);

        [Fact]
        public void TheDiscardIsRecordedAsTheLastRingEntryWithCauseRepeat() =>
            Assert.Equal(LlmCallCause.Repeat, run.Ring.Snapshot().OrderBy(r => r.Seq).Last().Cause);
    }

    // AC3 — a drop that leaves only one speaker discards it
    public sealed class ScenarioTheDropLeavesOneSpeaker
    {
        // Given every neighbour line repeats the neighbour's history after the re-ask
        // When  the exchange is assembled

        static readonly string NeighborRepeats = Script(
            HostOne,
            "NEIGHBOR: Sometimes the quiet hours on this station feel like a secret handshake, friends.",
            FreshHostTwo,
            "NEIGHBOR: Sometimes the quiet hours on this station feel like a secret handshake, again.",
            "HOST: Anyway, the next record is a good one for the road.");

        readonly Run run = Write([NeighborRepeats, NeighborRepeats], OwnHistories);

        [Fact]
        public void TheExchangeIsDiscardedAsARepeat() =>
            Assert.Equal(LlmCallCause.Repeat, (run.Result as CrosstalkWriteResult.Discarded)?.Cause);
    }

    // Back-compat — without persona ids the writer is exactly what it was
    public sealed class ScenarioWithoutPersonaIdsNothingIsCheckedOrStored
    {
        // Given a request that carries no persona ids and a reply that repeats a seeded history
        // When  an exchange is written

        readonly Run run = Write([FourLines], OwnHistories, withIds: false);

        [Fact]
        public void TheWriterMakesOneCallAndStoresNothing() =>
            Assert.Equal((1, 4, 0), (run.UserPrompts.Count, run.Lines.Count, run.Store.Recorded.Count));
    }

    public sealed class ScenarioWithoutTheMemoryServicesNothingIsCheckedOrStored
    {
        // Given a writer built without a check or a store
        // When  an exchange is written

        readonly Run run = Write([FourLines], OwnHistories, withMemory: false);

        [Fact]
        public void TheWriterMakesOneCallAndStoresNothing() =>
            Assert.Equal((1, 4, 0), (run.UserPrompts.Count, run.Lines.Count, run.Store.Recorded.Count));
    }
}
