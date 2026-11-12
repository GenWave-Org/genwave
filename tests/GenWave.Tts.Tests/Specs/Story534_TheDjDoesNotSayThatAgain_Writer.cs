// STORY-534 — The DJ does not say that again (gh-#293 gh-#920 · SPEC F218 · PLAN T692 T693)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans two files; see tests/GenWave.Memory.Tests/Specs/Story534_TheDjDoesNotSayThatAgain_Check.cs for the map.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTheDjDoesNotSayThatAgainWriter
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — recent lines reach the prompt
    // entry point: LlmCopyWriter.WriteAsync (prompt posted to the LLM)
    public sealed class ScenarioRecentLinesReachThePrompt
    {
        // Given a persona with 8 stored utterances
        // When  the prompt is built

        [Fact]
        public void ItContainsARecentLinesDoNotReuseTheirPhrasingBlock() =>
            Assert.Contains("Recent lines, do not reuse their phrasing", Arrange.Prompt(Arrange.Eight()));

        [Fact]
        public void TheBlockListsThe6NewestUtterancesEachTrimmedTo160Characters()
        {
            var block = Arrange.Block(Arrange.Prompt(Arrange.Eight()));
            var expected = Enumerable.Range(1, 6).Select(i => "- " + Arrange.Line(i)[..Math.Min(160, Arrange.Line(i).Length)]);
            Assert.Equal(expected, block);
        }
    }

    // AC5 — one re-ask, then fresh copy airs
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioOneReAskThenFreshCopyAirs
    {
        // Given a fake LLM whose first copy repeats and second does not
        // When  WriteAsync runs

        [Fact]
        public void TheSecondCopyIsRendered() =>
            Assert.Equal(Arrange.Fresh, Arrange.Run(Arrange.Repeating, Arrange.Fresh).Copy.Text);

        [Fact]
        public void TheSecondCopyIsStoredAsNonFallback() =>
            Assert.Equal([(Arrange.Fresh, false)], Arrange.Run(Arrange.Repeating, Arrange.Fresh).Store.Recorded.Select(u => (u.Text, u.Fallback)));

        [Fact]
        public void TheLLMWasCalledExactlyTwice() =>
            Assert.Equal(2, Arrange.Run(Arrange.Repeating, Arrange.Fresh).UserPrompts.Count);

        [Fact]
        public void TheReAskPromptNamesTheOffendingPhraseAfterDoNotSay()
        {
            var reAsk = Arrange.Run(Arrange.Repeating, Arrange.Fresh).UserPrompts[1];

            Assert.Contains("do not say: \"a golden hour groove from the vault\"", reAsk);
        }

        [Fact]
        public void ATruthGateReAskFollowedByARepeatingReAskGivesTheTemplateAfterExactlyTwoCalls() =>
            // The first copy lies about the day, the re-ask clears truth but repeats: still one extra call only.
            Assert.Equal(2, Arrange.Run(Arrange.WrongDay, Arrange.Repeating).UserPrompts.Count);

        [Fact]
        public void ATruthGateReAskFollowedByARepeatingReAskStoresTheFallback() =>
            Assert.True(Arrange.Run(Arrange.WrongDay, Arrange.Repeating).Store.Recorded.Single().Fallback);

        [Fact]
        public void AQuirkSelectedThisBreakMakesOneCall() =>
            Assert.Single(Arrange.QuirkRun(selected: true).UserPrompts);

        [Fact]
        public void AQuirkSelectedThisBreakAirsItsCatchphrase() =>
            Assert.Equal(Arrange.CatchphraseCopy, Arrange.QuirkRun(selected: true).Copy.Text);

        [Fact]
        public void ACappedQuirkNotSelectedThisBreakIsCaught() =>
            Assert.Equal(2, Arrange.QuirkRun(selected: false).UserPrompts.Count);

        [Fact]
        public void ACappedQuirkNotSelectedThisBreakIsNamedInTheReAsk() =>
            Assert.Contains("do not say: \"officially a banger, log it\"", Arrange.QuirkRun(selected: false).UserPrompts[1]);

        [Fact]
        public async Task APreviewNeverReAsksOnARepeatResult()
        {
            var result = await PreviewOnARepeat();

            Assert.Equal(new PersonaPreviewResult.Success(Arrange.Repeating), result.Result);
        }

        [Fact]
        public async Task APreviewNeverReAsksOnARepeatCallCount()
        {
            var result = await PreviewOnARepeat();

            Assert.Single(result.Prompts);
        }

        [Fact]
        public async Task APreviewNeverReAsksOnARepeatEvents()
        {
            var result = await PreviewOnARepeat();

            Assert.Empty(result.Events.Events);
        }

        static async Task<(PersonaPreviewResult Result, List<string> Prompts, RecordingStationEventSink Events)> PreviewOnARepeat()
        {
            var prompts = new List<string>();
            var events = new RecordingStationEventSink();
            var writer = Arrange.Writer(prompts, [Arrange.Repeating], new RecordingUtteranceStore { Seeded = { Arrange.Entry(1, Arrange.Recent) } }, events);

            var result = await writer.WritePreviewAsync(QuirkSpecHarness.Request(null), personaOverride: null, CancellationToken.None);
            return (result, prompts, events);
        }
    }

    // AC6 — the catch is logged
    // entry point: BoothLogWriter via the event bus (repeat-caught); the mapping facts live in
    // tests/GenWave.MediaLibrary.Tests/Specs/Story534_RepeatCaughtBoothRow.cs
    public sealed class ScenarioTheCatchIsLogged
    {
        // Given a caught repeat
        // When  the re-ask is issued

        [Fact]
        public void ABoothLogRowKindRepeatCaughtNamesThePersonaAndThePhrase()
        {
            var run = Arrange.Run(Arrange.Repeating, Arrange.Fresh);

            Assert.Equal(
                [(QuirkSpecHarness.PersonaId, "a golden hour groove from the vault")],
                run.Events.Events.OfType<RepeatCaught>().Select(e => (e.PersonaId, e.Phrase)));
        }

        [Fact]
        public void TheLLMCallRecorderCountsOneCallWithCauseRepeat()
        {
            var run = Arrange.Run(Arrange.Repeating, Arrange.Fresh);

            Assert.Equal(1, run.Counters.Snapshot().Where(c => c.Cause == LlmCallCause.Repeat).Sum(c => c.Count));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — two hits means the template
    public sealed class ScenarioTwoHitsMeansTheTemplate
    {
        // Given a fake LLM that returns the same repeating copy twice
        // When  WriteAsync runs

        [Fact]
        public void TheTemplateFallbackIsRendered() =>
            Assert.Equal("Coming up: Astral Plane by Valerie June.", Arrange.Run(Arrange.Repeating, Arrange.Repeating).Copy.Text);

        [Fact]
        public void TheTemplateFallbackIsNotFreshPerAiring() =>
            Assert.False(Arrange.Run(Arrange.Repeating, Arrange.Repeating).Copy.FreshPerAiring);

        [Fact]
        public void ADoubleTruthGateRejectStillRecordsAFailedAttempt() =>
            Assert.Equal(1, Arrange.Run(Arrange.WrongDay, Arrange.WrongDay).Status.ConsecutiveFailureCount);

        [Fact]
        public void ATruthGateRejectThenARepeatingReAskRecordsNoFailure() =>
            Assert.Equal(0, Arrange.Run(Arrange.WrongDay, Arrange.Repeating).Status.ConsecutiveFailureCount);

        [Fact]
        public void ARepeatThenAReAskThatFailsTruthRecordsAFailure() =>
            Assert.Equal(1, Arrange.Run(Arrange.Repeating, Arrange.WrongDay).Status.ConsecutiveFailureCount);

        [Fact]
        public void ADoubleRepeatDoesNotRecordAFailedAttempt() =>
            Assert.Equal(0, Arrange.Run(Arrange.Repeating, Arrange.Repeating).Status.ConsecutiveFailureCount);

        [Fact]
        public void TheStoredUtteranceHasFallbackTrue() =>
            Assert.True(Arrange.Run(Arrange.Repeating, Arrange.Repeating).Store.Recorded.Single().Fallback);

        [Fact]
        public void TheLLMWasCalledExactlyTwice() =>
            Assert.Equal(2, Arrange.Run(Arrange.Repeating, Arrange.Repeating).UserPrompts.Count);

        [Fact]
        public void EachCaughtAttemptIsLogged() =>
            Assert.Equal(2, Arrange.Run(Arrange.Repeating, Arrange.Repeating).Events.Events.OfType<RepeatCaught>().Count());
    }

    // AC8 — memory off, block STILL on (rewritten by T716; supersedes the old "block off", see STORY-547 AC1)
    public sealed class ScenarioMemoryOffBlockStillOn
    {
        // Given stored utterances and no memory budget (the writer takes none: F218.37)
        // When  the prompt is built

        [Fact]
        public void TheRecentLinesBlockIsStillPresent() =>
            Assert.Contains("Recent lines", Arrange.Prompt(Arrange.Eight()));
    }

    // AC8 — memory off, the post-check still runs
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioMemoryOffThePostCheckStillRuns
    {
        // Given the writer has no memory budget
        // When  WriteAsync runs

        [Fact]
        public void ThePostCheckStillRuns()
        {
            var run = Arrange.Run(Arrange.Repeating, Arrange.Fresh);

            Assert.Equal(2, run.UserPrompts.Count);
        }
    }

    // F218.10 / F218.12 (re-ask half) - the block is bounded; the re-ask names phrases
    public sealed class ScenarioThePromptStaysBounded
    {
        [Fact]
        public void TheRecentLinesBlockIsAtMost6TimesTheLineCapPlusOverhead()
        {
            var long6 = Enumerable.Range(1, 6).Select(i => Arrange.Entry(i, new string('x', 500))).ToList();
            var block = Arrange.Block(Arrange.Prompt(long6));
            Assert.Equal(6 * (2 + 160) + 5, string.Join('\n', block).Length);
        }

        [Fact]
        public void TheRepeatReAskAppendsDoNotSayWithEachQuotedPhraseInHitOrder() =>
            Assert.EndsWith(
                "\n\ndo not say: \"golden hour\", \"back to back\"",
                LlmPromptBuilder.BuildRepeatReAsk("Original prompt.", ["golden hour", "back to back"]));
    }

    // F218.11 follow-up — the writer hands the track's album and year to the repeat check
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioTheWriterPassesTrackFactsToTheCheck
    {
        // Given a 1959 track "Astral Plane" whose recent line holds its year words, or a track on the album
        //       "Velvet Garden" whose recent line holds the album words
        // When  the reply repeats only the year words and the title, or only the album words and filler

        [Fact]
        public async Task ARepeatOfOnlyTheYearWordsAndTheTitleMakesOneCall()
        {
            var prompts = new List<string>();
            var store = new RecordingUtteranceStore { Seeded = { Arrange.Entry(1, "Back in nineteen fifty nine astral plane naturally") } };
            var writer = Arrange.Writer(prompts, ["Back in nineteen fifty nine astral plane naturally"], store, new RecordingStationEventSink());
            var track = new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June", Album: "Big Thing", Year: 1959);

            await writer.WriteAsync(QuirkSpecHarness.Request(null) with { Track = track }, CancellationToken.None);

            Assert.Single(prompts);
        }

        [Fact]
        public async Task ARepeatOfOnlyTheAlbumWordsAndFillerMakesOneCall()
        {
            var prompts = new List<string>();
            var store = new RecordingUtteranceStore { Seeded = { Arrange.Entry(1, "Velvet garden of tonight") } };
            var writer = Arrange.Writer(prompts, ["Velvet garden of tonight"], store, new RecordingStationEventSink());
            var track = new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June", Album: "Velvet Garden");

            await writer.WriteAsync(QuirkSpecHarness.Request(null) with { Track = track }, CancellationToken.None);

            Assert.Single(prompts);
        }
    }

    // F218.34 - the crossing track's title and artist are excluded from repeat runs too
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioTheWriterPassesTheCrossingTrackToTheCheck
    {
        // Given a sign-on whose crossing track is Duran Duran's "Do You Believe in Shame" and a recent line naming it
        // When  the reply names the crossing track again inside otherwise fresh copy

        readonly List<string> prompts = [];

        public ScenarioTheWriterPassesTheCrossingTrackToTheCheck()
        {
            const string said = "That was Duran Duran's Do You Believe in Shame";
            var store = new RecordingUtteranceStore { Seeded = { Arrange.Entry(1, said) } };
            var writer = Arrange.Writer(prompts, ["Duran Duran's Do You Believe in Shame, and that is how the late shift begins."], store, new RecordingStationEventSink());
            var request = QuirkSpecHarness.Request(null) with
            {
                Kind = SegmentKind.SignOn,
                CrossingTrackTitle = "Do You Believe in Shame",
                CrossingTrackArtist = "Duran Duran",
            };

            writer.WriteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void ARepeatOfOnlyTheCrossingTrackNameMakesOneCall() =>
            Assert.Single(prompts);
    }

    sealed class FixedSelector(QuirkSelection selection) : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(selection);
    }

    sealed class NoQuirkSelector : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    sealed record Outcome(
        SegmentCopy Copy, List<string> UserPrompts, RecordingUtteranceStore Store,
        RecordingStationEventSink Events, LlmCallCauseCounters Counters, LlmCopyStatusHolder Status);

    static class Arrange
    {
        public const string Recent = "Here comes a golden hour groove from the vault tonight.";
        public const string Repeating = "Settle in for a golden hour groove from the vault, folks.";
        public const string Fresh = "Next up, a real stunner for the late shift.";
        /// <summary>Names a weekday three days off, wrong in every timezone.</summary>
        public static readonly string WrongDay = $"Happy {DateTime.UtcNow.AddDays(3).DayOfWeek}, and here is a real stunner for the late shift.";

        static SegmentRequest StampedRequest() => QuirkSpecHarness.Request(
            ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)).ToString());
        public const string Quirk = "Say \"officially a banger, log it\" when a track slaps";
        public const string CatchphraseCopy = "That one is officially a banger, log it, and now the next.";

        /// <summary>The WriteAsync call fed these replies in order, with one stored recent line that the first reply repeats.</summary>
        public static Outcome Run(string first, string second)
        {
            var prompts = new List<string>();
            var store = new RecordingUtteranceStore { Seeded = { Entry(1, Recent) } };
            var events = new RecordingStationEventSink();
            var counters = new LlmCallCauseCounters(TimeProvider.System);
            var status = new LlmCopyStatusHolder();
            var writer = Writer(prompts, [first, second], store, events, counters, statusHolder: status);
            var copy = writer.WriteAsync(StampedRequest(), CancellationToken.None).GetAwaiter().GetResult();
            return new Outcome(copy, prompts, store, events, counters, status);
        }

        public static LlmCopyWriter Writer(
            List<string> prompts, string[] replies, RecordingUtteranceStore store, RecordingStationEventSink events,
            LlmCallCauseCounters? counters = null, IQuirkSelector? selector = null,
            PersonaCard? card = null, LlmCopyStatusHolder? statusHolder = null) =>
            QuirkSpecHarness.WriterFor(
                card ?? QuirkSpecHarness.Card([], null), selector ?? new NoQuirkSelector(),
                utteranceStore: store,
                httpOverride: QuirkSpecHarness.ScriptedHttp(prompts, replies), repetitionCheck: new RepetitionCheck(),
                events: events, recorder: QuirkSpecHarness.NewRecorder(counters), statusHolder: statusHolder);

        /// <summary>A capped quirk whose key phrase the reply says, either selected for this break or not.</summary>
        public static Outcome QuirkRun(bool selected)
        {
            var prompts = new List<string>();
            var store = new RecordingUtteranceStore();
            var events = new RecordingStationEventSink();
            var card = QuirkSpecHarness.Card([Quirk], [new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: 1.0)]);
            var selection = selected ? new QuirkSelection([Quirk], [Quirk]) : QuirkSelection.None;
            var writer = Writer(prompts, [CatchphraseCopy, Fresh], store, events, selector: new FixedSelector(selection), card: card);
            var copy = writer.WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None).GetAwaiter().GetResult();
            return new Outcome(copy, prompts, store, events, new LlmCallCauseCounters(TimeProvider.System), new LlmCopyStatusHolder());
        }

        public static string Line(int i) => i % 2 == 0 ? $"Line {i} " + new string('y', 300) : $"Line {i} short.";

        public static UtteranceEntry Entry(int i, string text) =>
            new(i, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn", ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)), text, false, DateTimeOffset.UnixEpoch, null);

        /// <summary>Eight utterances, newest first (Line 1 is newest).</summary>
        public static List<UtteranceEntry> Eight() => Enumerable.Range(1, 8).Select(i => Entry(i, Line(i))).ToList();

        public static string Prompt(List<UtteranceEntry> stored)
        {
            var store = new RecordingUtteranceStore();
            store.Seeded.AddRange(stored);
            string? prompt = null;
            QuirkSpecHarness.WriterFor(
                    QuirkSpecHarness.Card([], null), new NoQuirkSelector(),
                    onSystemPrompt: p => { prompt = p; return Task.CompletedTask; },
                    utteranceStore: store)
                .WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None).GetAwaiter().GetResult();
            return prompt ?? throw new InvalidOperationException("No completion request was captured");
        }

        /// <summary>The "- " lines directly under the heading.</summary>
        public static List<string> Block(string prompt) =>
            prompt.Split('\n').SkipWhile(l => !l.StartsWith("Recent lines", StringComparison.Ordinal)).Skip(1)
                .TakeWhile(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();
    }
}
