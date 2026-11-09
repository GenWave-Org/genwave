// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T686)
//
// Wiring of IQuirkSelector into LlmCopyWriter: on-air asks to spend, with the request's parsed shift
// key, before the completion is dispatched; the chosen lines (not a fresh sample) reach the prompt.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using static GenWave.Tts.Tests.Fakes.QuirkSpecHarness;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftWriter
{
    const string CappedQuirk = "Signs off with a bird fact";
    static readonly ShiftKey Shift = ShiftKey.Scheduled(PersonaId, DayOfWeek.Tuesday, 360, new DateOnly(2026, 11, 10));

    sealed class ScriptedSelector(List<string> events) : IQuirkSelector
    {
        public long? SeenPersonaId { get; private set; }
        public ShiftKey? SeenShiftKey { get; private set; }
        public bool? SeenSpend { get; private set; }

        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct)
        {
            events.Add("select");
            SeenPersonaId = personaId;
            SeenShiftKey = shiftKey;
            SeenSpend = spend;
            return Task.FromResult(new QuirkSelection(["Chosen quirk line"], []));
        }
    }

    sealed class RecordingLedger : IQuirkSpendLedger
    {
        public List<string> Spends { get; } = [];

        public Task<int> CountSpendsAsync(long personaId, string content, CancellationToken ct) =>
            Task.FromResult(Spends.Count(s => s == content));

        public Task RecordSpendAsync(long personaId, string content, CancellationToken ct)
        {
            Spends.Add(content);
            return Task.CompletedTask;
        }
    }

    public sealed class ScenarioOnAirSelectsBeforeTheRenderIsDispatched
    {
        // Given an on-air break request for persona 7 carrying a shift key
        // When  the writer renders it
        // Then  the selector is asked to spend, with the parsed key, before the completion is sent,
        //       and its chosen lines (not a fresh sample) reach the prompt

        readonly List<string> events = [];
        readonly ScriptedSelector selector;
        string? systemPrompt;

        public ScenarioOnAirSelectsBeforeTheRenderIsDispatched()
        {
            selector = new ScriptedSelector(events);
            var writer = WriterFor(
                Card("Raw card quirk"), selector,
                onSystemPrompt: prompt => { systemPrompt = prompt; return Task.CompletedTask; },
                onHttp: () => events.Add("http"));
            writer.WriteAsync(Request(Shift.ToString()), CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void SelectionHappensBeforeTheCompletionIsSent() => Assert.Equal(["select", "http"], events);

        [Fact]
        public void TheSelectorIsAskedToSpend() => Assert.True(selector.SeenSpend);

        [Fact]
        public void TheSelectorGetsThePersonaAndTheParsedShiftKey() =>
            Assert.Equal((PersonaId, Shift), (selector.SeenPersonaId, selector.SeenShiftKey));

        [Fact]
        public void TheChosenLinesReachThePromptInPlaceOfAFreshSample() =>
            Assert.Contains("Quirks: Chosen quirk line", systemPrompt);
    }

    public sealed class ScenarioAnAnnouncementWithoutAShiftKeyCannotAirACappedQuirk
    {
        // Given a card whose only quirk is maxPerShift: 1 with perBreakChance 1.0
        // And   an announcement request that carries no shift key (the planner stamps none)
        // When  the announcement is written on air
        // Then  the capped quirk is not in the prompt
        // And   no spend is recorded

        readonly RecordingLedger ledger = new();
        string? systemPrompt;

        public ScenarioAnAnnouncementWithoutAShiftKeyCannotAirACappedQuirk()
        {
            var card = Card(CappedQuirk, new QuirkRule(CappedQuirk, MaxPerShift: 1, PerBreakChance: 1.0));
            var writer = WriterFor(
                card, new QuirkSelector(ledger, new Random(1)),
                onSystemPrompt: prompt => { systemPrompt = prompt; return Task.CompletedTask; });
            writer.WriteAnnouncementAsync(Request(shiftKeyText: null), "The station is moving to a new address.", CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        [Fact]
        public void TheCappedQuirkIsNotInThePrompt() => Assert.DoesNotContain(CappedQuirk, systemPrompt ?? throw new InvalidOperationException("No completion request was captured"));

        [Fact]
        public void NoSpendIsRecorded() => Assert.Empty(ledger.Spends);
    }

    public sealed class ScenarioOnAirWithAShiftKeySpendsTheCappedQuirk
    {
        // Given the same maxPerShift: 1 / perBreakChance 1.0 card
        // When  an on-air break request carrying a shift key is written
        // Then  exactly one spend row for that quirk and shift is recorded (the preview sibling in Host.Tests records none)

        readonly RecordingLedger ledger = new();

        public ScenarioOnAirWithAShiftKeySpendsTheCappedQuirk()
        {
            var card = Card(CappedQuirk, new QuirkRule(CappedQuirk, MaxPerShift: 1, PerBreakChance: 1.0));
            WriterFor(card, new QuirkSelector(ledger, new Random(1)))
                .WriteAsync(Request(Shift.ToString()), CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void OneSpendRowIsRecorded() => Assert.Equal([$"{CappedQuirk} @ {Shift}"], ledger.Spends);
    }
}
