// STORY-547 — Recent lines without the 8k probe (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T716)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: LlmCopyWriter.WriteAsync; the prompt is read from the fake LLM's captured request

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureRecentLinesWithoutThe8kProbe
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — budget off, block on
    public sealed class ScenarioBudgetOffBlockOn
    {
        // Given a memory budget of Off and a persona with 3 stored utterances
        // When  the on-air prompt is built

        [Fact]
        public void ItContainsTheRecentLinesDoNotReuseTheirPhrasingBlockWith3Lines() =>
            Assert.Equal(3, Arrange.Block(Arrange.Prompt(Arrange.Lines(3))).Count);
    }

    // AC2 — six at most
    public sealed class ScenarioSixAtMost
    {
        // Given a persona with 9 stored utterances
        // When  the on-air prompt is built

        [Fact]
        public void TheBlockListsThe6Newest() =>
            Assert.Equal(Enumerable.Range(1, 6).Select(i => "- " + Arrange.Text(i)), Arrange.Block(Arrange.Prompt(Arrange.Lines(9))));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC3 — no lines, no block
    public sealed class ScenarioNoLinesNoBlock
    {
        // Given a persona with no stored utterances
        // When  the on-air prompt is built

        [Fact]
        public void NoRecentLinesBlockIsPresent() =>
            Assert.DoesNotContain(LlmPromptBuilder.RecentLinesHeading, Arrange.Prompt(Arrange.Lines(0)));
    }

    // AC4 — memory stays behind the budget
    public sealed class ScenarioMemoryStaysBehindTheBudget
    {
        // Given a memory budget of Off (no memory blocks exist yet; T699 adds them, this pins their absence)
        // When  the on-air prompt is built

        [Fact]
        public void NoWhatYouRememberBlockIsPresent() =>
            Assert.DoesNotContain("What you remember", Arrange.Prompt(Arrange.Lines(3)));
    }

    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    static class Arrange
    {
        public static string Text(int i) => $"Spec line {i} short.";

        /// <summary>Stored utterances, newest first (line 1 is newest).</summary>
        public static List<UtteranceEntry> Lines(int count) =>
            Enumerable.Range(1, count)
                .Select(i => new UtteranceEntry(i, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                    ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)), Text(i), false, DateTimeOffset.UnixEpoch, null))
                .ToList();

        /// <summary>The on-air system prompt; the writer takes no memory budget, so the block can only depend on the stored lines.</summary>
        public static string Prompt(List<UtteranceEntry> stored)
        {
            var store = new RecordingUtteranceStore();
            store.Seeded.AddRange(stored);
            string? prompt = null;
            QuirkSpecHarness.WriterFor(
                    QuirkSpecHarness.Card([], null), new NoQuirks(),
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
