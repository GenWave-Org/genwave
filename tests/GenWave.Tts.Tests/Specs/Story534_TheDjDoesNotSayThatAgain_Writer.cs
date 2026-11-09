// STORY-534 — The DJ does not say that again (gh-#293 gh-#920 · SPEC F218 · PLAN T692 T693)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans two files; see tests/GenWave.Memory.Tests/Specs/Story534_TheDjDoesNotSayThatAgain_Check.cs for the map.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
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
        // Given a persona with 8 stored utterances and a non-zero memory budget
        // When  the prompt is built

        [Fact]
        public void ItContainsARecentLinesDoNotReuseTheirPhrasingBlock() =>
            Assert.Contains("Recent lines, do not reuse their phrasing", Arrange.Prompt(Arrange.Eight(), MemoryBudget.Standard));

        [Fact]
        public void TheBlockListsThe6NewestUtterancesEachTrimmedTo160Characters()
        {
            var block = Arrange.Block(Arrange.Prompt(Arrange.Eight(), MemoryBudget.Standard));
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

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheSecondCopyIsStoredAndRendered() => Assert.Fail("pending: T693 — the second copy is stored and rendered");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyTwice() => Assert.Fail("pending: T693 — the LLM was called exactly twice");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheReAskPromptNamesTheOffendingPhraseAfterDoNotSay() => Assert.Fail("pending: T693 — the re-ask prompt names the offending phrase after \"do not say:\"");
    }

    // AC6 — the catch is logged
    // entry point: BoothLogWriter via the event bus (repeat-caught)
    public sealed class ScenarioTheCatchIsLogged
    {
        // Given a caught repeat
        // When  the re-ask is issued

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void ABoothLogRowKindRepeatCaughtNamesThePersonaAndThePhrase() => Assert.Fail("pending: T693 — a booth-log row kind repeat-caught names the persona and the phrase");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheLLMCallRecorderCountsOneCallWithCauseRepeat() => Assert.Fail("pending: T693 — the LLM call recorder counts one call with cause repeat");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — two hits means the template
    public sealed class ScenarioTwoHitsMeansTheTemplate
    {
        // Given a fake LLM that returns the same repeating copy twice
        // When  WriteAsync runs

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheTemplateFallbackIsRendered() => Assert.Fail("pending: T693 — the template fallback is rendered");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheStoredUtteranceHasFallbackTrue() => Assert.Fail("pending: T693 — the stored utterance has fallback = true");

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void TheLLMWasCalledExactlyTwice() => Assert.Fail("pending: T693 — the LLM was called exactly twice");
    }

    // AC8 — memory off, block off
    public sealed class ScenarioMemoryOffBlockOff
    {
        // Given a zero memory budget
        // When  the prompt is built

        [Fact]
        public void NoRecentLinesBlockIsPresent() =>
            Assert.DoesNotContain("Recent lines", Arrange.Prompt(Arrange.Eight(), MemoryBudget.Off));
    }

    // AC8 — memory off, the post-check still runs
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioMemoryOffThePostCheckStillRuns
    {
        // Given a zero memory budget
        // When  WriteAsync runs

        [Fact(Skip = "pending: T693 — see docs/PLAN.md")]
        public void ThePostCheckStillRuns() => Assert.Fail("pending: T693 — the post-check still runs");
    }

    // F218.10 / F218.12 (re-ask half) - the block is bounded; the re-ask names phrases
    public sealed class ScenarioThePromptStaysBounded
    {
        [Fact]
        public void TheRecentLinesBlockIsAtMost6TimesTheLineCapPlusOverhead()
        {
            var long6 = Enumerable.Range(1, 6).Select(i => Arrange.Entry(i, new string('x', 500))).ToList();
            var block = Arrange.Block(Arrange.Prompt(long6, MemoryBudget.Standard));
            Assert.Equal(6 * (2 + 160) + 5, string.Join('\n', block).Length);
        }

        [Fact]
        public void TheRepeatReAskAppendsDoNotSayWithEachQuotedPhraseInHitOrder() =>
            Assert.EndsWith(
                "\n\ndo not say: \"golden hour\", \"back to back\"",
                LlmPromptBuilder.BuildRepeatReAsk("Original prompt.", ["golden hour", "back to back"]));
    }

    sealed class NoQuirkSelector : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    static class Arrange
    {
        public static string Line(int i) => i % 2 == 0 ? $"Line {i} " + new string('y', 300) : $"Line {i} short.";

        public static UtteranceEntry Entry(int i, string text) =>
            new(i, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn", ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)), text, false, DateTimeOffset.UnixEpoch, null);

        /// <summary>Eight utterances, newest first (Line 1 is newest).</summary>
        public static List<UtteranceEntry> Eight() => Enumerable.Range(1, 8).Select(i => Entry(i, Line(i))).ToList();

        public static string Prompt(List<UtteranceEntry> stored, MemoryBudget budget)
        {
            var store = new RecordingUtteranceStore();
            store.Seeded.AddRange(stored);
            string? prompt = null;
            QuirkSpecHarness.WriterFor(
                    QuirkSpecHarness.Card([], null), new NoQuirkSelector(),
                    onSystemPrompt: p => { prompt = p; return Task.CompletedTask; },
                    utteranceStore: store, memoryBudget: budget)
                .WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None).GetAwaiter().GetResult();
            return prompt ?? throw new InvalidOperationException("No completion request was captured");
        }

        /// <summary>The "- " lines directly under the heading.</summary>
        public static List<string> Block(string prompt) =>
            prompt.Split('\n').SkipWhile(l => !l.StartsWith("Recent lines", StringComparison.Ordinal)).Skip(1)
                .TakeWhile(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();
    }
}
