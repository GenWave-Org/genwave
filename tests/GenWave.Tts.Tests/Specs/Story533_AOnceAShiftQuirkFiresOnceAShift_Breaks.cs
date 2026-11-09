// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218.7 · PLAN T688)
//
// Entry point LlmCopyWriter.WriteAsync: many breaks in one shift spend the capped quirk once.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using static GenWave.Tts.Tests.Fakes.QuirkSpecHarness;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftBreaks
{
    const string CappedQuirk = "Signs off with a bird fact";
    static readonly ShiftKey Shift = ShiftKey.Scheduled(PersonaId, DayOfWeek.Tuesday, 360, new DateOnly(2026, 11, 10));

    public sealed class ScenarioTenBreaksInOneShiftSpendTheCappedQuirkOnce
    {
        // Given a card rule maxPerShift: 1, perBreakChance: 1.0
        // When  ten on-air breaks in one shift are written
        // Then  exactly one spend row is recorded
        // And   exactly one posted prompt carries the land-it line

        readonly RecordingQuirkLedger ledger = new();
        readonly List<string> prompts = [];

        public ScenarioTenBreaksInOneShiftSpendTheCappedQuirkOnce()
        {
            var card = Card(CappedQuirk, new QuirkRule(CappedQuirk, MaxPerShift: 1, PerBreakChance: 1.0));
            var writer = WriterFor(
                card, new QuirkSelector(ledger, new Random(1)),
                onSystemPrompt: p => { prompts.Add(p); return Task.CompletedTask; });
            for (var i = 0; i < 10; i++)
                writer.WriteAsync(Request(Shift.ToString()), CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void OneSpendRowIsRecorded() => Assert.Equal([$"{CappedQuirk} @ {Shift}"], ledger.Spends);

        [Fact]
        public void OnePromptCarriesTheLandItLine() =>
            Assert.Single(prompts, p => p.Contains(string.Format(LlmPromptBuilder.CappedQuirkLandItFormat, CappedQuirk)));
    }
}
