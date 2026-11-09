// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T687)
//
// Spans four files; see tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

namespace GenWave.Tts.Tests.Specs;

using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using static GenWave.Tts.Tests.Fakes.QuirkSpecHarness;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftPrompt
{
    const string CappedQuirk = "Signs off with a bird fact";
    const string SecondCappedQuirk = "Counts the cars on the road";
    const string PlainQuirk = "Hums between songs";
    const string LandItPrefix = "This is your one";
    static readonly ShiftKey Shift = ShiftKey.Scheduled(PersonaId, DayOfWeek.Tuesday, 360, new DateOnly(2026, 11, 10));

    /// <summary>A ledger that reports a fixed count of earlier spends and records nothing.</summary>
    sealed class NoSpendLedger(int alreadySpent) : IQuirkSpendLedger
    {
        public Task<int> CountSpendsAsync(long personaId, string content, CancellationToken ct) => Task.FromResult(alreadySpent);

        public Task RecordSpendAsync(long personaId, string content, CancellationToken ct) => Task.CompletedTask;
    }

    static string PromptWith(PersonaCard card, int alreadySpent = 0) =>
        PromptFor(card, new QuirkSelector(new NoSpendLedger(alreadySpent), new Random(1)), Shift.ToString());

    static string[] Lines(string prompt) => prompt.Split('\n');

    static QuirkRule CappedOnce(string text) => new(text, MaxPerShift: 1, PerBreakChance: 1.0);

    /// <summary>
    /// The quirks block as the builder owns it, for the quirks the prompt actually shows (all capped
    /// here). Throws if the posted prompt does not carry exactly that block.
    /// </summary>
    static string PostedQuirksBlock(string prompt)
    {
        var quirksLine = Lines(prompt).FirstOrDefault(l => l.StartsWith("Quirks: ", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("No Quirks line in the prompt");
        var shown = quirksLine["Quirks: ".Length..].Split("; ");
        var block = string.Join('\n', LlmPromptBuilder.BuildQuirksBlock(shown, shown));
        return prompt.Contains(block, StringComparison.Ordinal)
            ? block
            : throw new InvalidOperationException("The posted prompt does not carry the builder's quirks block");
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC5 — the positive line
    // entry point: LlmCopyWriter.WriteAsync (prompt posted to the LLM)
    public sealed class ScenarioThePositiveLine
    {
        // Given the break where the maxPerShift quirk is chosen
        // When  the prompt is built

        readonly string prompt = PromptWith(Card(CappedQuirk, CappedOnce(CappedQuirk)));

        [Fact]
        public void ItContainsOneLineOfTheThisIsYourOneOfTheShiftLandItClass() =>
            Assert.Equal(
                [string.Format(CultureInfo.InvariantCulture, LlmPromptBuilder.CappedQuirkLandItFormat, CappedQuirk)],
                Lines(prompt).Where(l => l.StartsWith(LandItPrefix, StringComparison.Ordinal)).ToArray());

        [Fact]
        public void TheQuirksBlockContainsNoDoNotOrNeverPhrasing() =>
            Assert.DoesNotMatch(@"\b(do not|never)\b", PostedQuirksBlock(prompt).ToLowerInvariant());
    }

    // AC5 — one line per capped quirk, however many are chosen
    public sealed class ScenarioTwoCappedLinesThisBreak
    {
        // Given two maxPerShift quirks both chosen this break
        // When  the prompt is built

        readonly string prompt = PromptWith(Card(
            [CappedQuirk, SecondCappedQuirk], [CappedOnce(CappedQuirk), CappedOnce(SecondCappedQuirk)]));

        [Fact]
        public void ItContainsALandItLineForEach() =>
            Assert.Equal(
                [SecondCappedQuirk, CappedQuirk],
                Lines(prompt).Where(l => l.StartsWith(LandItPrefix, StringComparison.Ordinal)).Select(l => l.Split('"')[1]).Order().ToArray());

        [Fact]
        public void TheQuirksBlockContainsNoDoNotOrNeverPhrasing() =>
            Assert.DoesNotMatch(@"\b(do not|never)\b", PostedQuirksBlock(prompt).ToLowerInvariant());
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 (sad) — a break with no capped line chosen carries no annotation
    public sealed class ScenarioNoCappedLineThisBreak
    {
        // Given a maxPerShift: 1 quirk that is already spent this shift, beside a plain quirk
        // When  the prompt is built

        readonly string prompt = PromptWith(
            Card([CappedQuirk, PlainQuirk], [CappedOnce(CappedQuirk)]), alreadySpent: 1);

        [Fact]
        public void ThePromptCarriesThePlainQuirk() => Assert.Contains($"Quirks: {PlainQuirk}", prompt);

        [Fact]
        public void ThePromptCarriesNoLandItLine() => Assert.DoesNotContain(LandItPrefix, prompt);
    }

    // AC5 (sad) — a card with no rules at all carries no annotation either
    public sealed class ScenarioNoRulesOnTheCard
    {
        readonly string prompt = PromptWith(Card(PlainQuirk));

        [Fact]
        public void ThePromptCarriesNoLandItLine() => Assert.DoesNotContain(LandItPrefix, prompt);
    }
}
