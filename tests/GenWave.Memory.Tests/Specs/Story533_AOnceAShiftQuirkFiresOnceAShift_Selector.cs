// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T686)
//
// IQuirkSelector over an in-memory spend ledger. Randomness is pinned with perBreakChance 1.0 (the
// roll `NextDouble() >= 1.0` can never fail) and a seeded Random, so nothing here relies on luck.
//
// Spans four files; see tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftSelector
{
    const long PersonaId = 7;
    const string Quirk = "Signs off every shift with a bird fact";

    static readonly ShiftKey TuesdayShift = ShiftKey.Scheduled(PersonaId, DayOfWeek.Tuesday, 360, new DateOnly(2026, 11, 10));
    static readonly ShiftKey NextTuesdayShift = ShiftKey.Scheduled(PersonaId, DayOfWeek.Tuesday, 360, new DateOnly(2026, 11, 17));

    static PersonaCard PlainCard(params string[] quirks) =>
        new(PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "Soul line.", quirks,
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);

    static PersonaCard CardWith(QuirkRule rule) => PlainCard(rule.Text) with { QuirkRules = [rule] };

    static QuirkSelector SelectorOver(RecordingLedger ledger) => new(ledger, new Random(1));

    /// <summary>In-memory ledger that also keeps the order of its calls.</summary>
    sealed class RecordingLedger : IQuirkSpendLedger
    {
        public List<string> Spends { get; } = [];
        public List<string> Calls { get; } = [];
        public Task RecordGate { get; init; } = Task.CompletedTask;

        public Task<int> CountSpendsAsync(long personaId, string content, CancellationToken ct)
        {
            Calls.Add("count");
            return Task.FromResult(Spends.Count(s => s == content));
        }

        public async Task RecordSpendAsync(long personaId, string content, CancellationToken ct)
        {
            Calls.Add("record");
            await RecordGate;
            Spends.Add(content);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC3 — the shift key
    // entry point: IQuirkSelector via LlmCopyWriter.WriteAsync (ShiftKey from the break planner)
    public sealed class ScenarioTheShiftKey
    {
        // Given a schedule segment Tuesday 06:00–10:00 for persona 7 on 2026-11-10
        // When  two breaks happen inside it

        [Fact]
        public async Task BothCarryShiftKey7Tuesday36020261110()
        {
            var ledger = new RecordingLedger();
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 5, PerBreakChance: 1.0));
            var selector = SelectorOver(ledger);

            await selector.SelectAsync(PersonaId, card, TuesdayShift, spend: true, CancellationToken.None);
            await selector.SelectAsync(PersonaId, card, TuesdayShift, spend: true, CancellationToken.None);

            Assert.Equal([$"{Quirk} @ 7|Tuesday|360|2026-11-10", $"{Quirk} @ 7|Tuesday|360|2026-11-10"], ledger.Spends);
        }

        [Fact]
        public async Task ABreakInTheSameSlotOn20261117CarriesADifferentKey()
        {
            var ledger = new RecordingLedger();
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 5, PerBreakChance: 1.0));

            await SelectorOver(ledger).SelectAsync(PersonaId, card, NextTuesdayShift, spend: true, CancellationToken.None);

            Assert.Equal([$"{Quirk} @ 7|Tuesday|360|2026-11-17"], ledger.Spends);
        }
    }

    // AC4 — spent means gone for the shift
    public sealed class ScenarioSpentMeansGoneForTheShift
    {
        // Given a maxPerShift: 1 quirk and 100 breaks in one shift
        // When  the selector samples quirks for each break

        [Fact]
        public async Task TheQuirkIsChosenExactlyOnce()
        {
            var ledger = new RecordingLedger();
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: 1.0));
            var selector = SelectorOver(ledger);

            var chosen = 0;
            for (var i = 0; i < 100; i++)
            {
                var selection = await selector.SelectAsync(PersonaId, card, TuesdayShift, spend: true, CancellationToken.None);
                chosen += selection.Lines.Count(l => l == Quirk);
            }

            Assert.Equal(1, chosen);
        }

        [Fact]
        public async Task ItsSpendRowExistsInPersonaMemoryKindQuirkBeforeTheRenderIsDispatched()
        {
            var release = new TaskCompletionSource();
            var ledger = new RecordingLedger { RecordGate = release.Task };
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: 1.0));

            var pending = SelectorOver(ledger).SelectAsync(PersonaId, card, TuesdayShift, spend: true, CancellationToken.None);
            var completedBeforeRelease = pending.IsCompleted;
            release.SetResult();
            await pending;

            // The selection cannot be returned (so no render dispatched) while the spend is unfinished.
            Assert.False(completedBeforeRelease);
        }
    }

    // ---------------------------------------------------------------------
    // EDGE CASES (T686 task text)
    // ---------------------------------------------------------------------

    public sealed class ScenarioSpendMarksThatMustNotBeNeeded
    {
        // Given a maxPerShift: 1 quirk with perBreakChance 1.0 and a known shift key
        // When  the selector runs (with or without spend / shift key)
        // Then  spend rows follow the spend flag and the shift key, always after the count check
        [Fact]
        public async Task ASpendIsRecordedAfterTheCountCheck()
        {
            var ledger = new RecordingLedger();
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: 1.0));

            await SelectorOver(ledger).SelectAsync(PersonaId, card, TuesdayShift, spend: true, CancellationToken.None);

            Assert.Equal(["count", "record"], ledger.Calls);
        }

        [Fact]
        public async Task WithoutSpendNothingIsRecorded()
        {
            var ledger = new RecordingLedger();
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: 1.0));

            await SelectorOver(ledger).SelectAsync(PersonaId, card, TuesdayShift, spend: false, CancellationToken.None);

            Assert.Empty(ledger.Spends);
        }

        [Fact]
        public async Task WithoutAShiftKeyNothingIsRecorded()
        {
            var ledger = new RecordingLedger();
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: 1.0));

            await SelectorOver(ledger).SelectAsync(PersonaId, card, shiftKey: null, spend: true, CancellationToken.None);

            Assert.Empty(ledger.Spends);
        }

        [Fact]
        public async Task AZeroishChanceKeepsAMaxPerShiftQuirkOutOfThePool()
        {
            var ledger = new RecordingLedger();
            var card = CardWith(new QuirkRule(Quirk, MaxPerShift: 1, PerBreakChance: double.Epsilon));

            var selection = await SelectorOver(ledger).SelectAsync(PersonaId, card, TuesdayShift, spend: true, CancellationToken.None);

            Assert.Empty(selection.Lines);
        }

        [Fact]
        public async Task ACardWithoutRulesSamplesTwoOrThreeOfFive()
        {
            // Given a card with five plain quirks and no rules
            // When  50 breaks are sampled by one selector
            // Then  every break shows 2 or 3 of them
            var card = PlainCard("a", "b", "c", "d", "e");
            var sizes = new HashSet<int>();
            var selector = SelectorOver(new RecordingLedger());

            for (var i = 0; i < 50; i++)
            {
                var selection = await selector.SelectAsync(PersonaId, card, TuesdayShift, spend: true, CancellationToken.None);
                sizes.Add(selection.Lines.Count);
            }

            Assert.Subset(new HashSet<int> { 2, 3 }, sizes);
        }
    }
}
