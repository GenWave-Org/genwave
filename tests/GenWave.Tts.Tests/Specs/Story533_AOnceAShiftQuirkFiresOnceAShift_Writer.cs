// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T686)
//
// Wiring of IQuirkSelector into LlmCopyWriter: on-air asks to spend, with the request's parsed shift
// key, before the completion is dispatched; the chosen lines (not a fresh sample) reach the prompt.

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using System.Text;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftWriter
{
    const long PersonaId = 7;
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

    static PersonaCard Card(string quirk, QuirkRule? rule = null) =>
        new(PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "Soul line.", [quirk],
            new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], [])
        {
            QuirkRules = rule is null ? null : [rule],
        };

    static SegmentRequest Request(string? shiftKeyText) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station")
        {
            PersonaId = PersonaId,
            ShiftKeyText = shiftKeyText,
        };

    static LlmCopyWriter WriterFor(
        PersonaCard card, IQuirkSelector selector, Func<string, Task>? onSystemPrompt = null, Action? onHttp = null)
    {
        var http = new SingleHandlerHttpClientFactory(new FakeHttpMessageHandler(async (req, _) =>
        {
            onHttp?.Invoke();
            if (req.Content is { } content && onSystemPrompt is not null)
            {
                var body = await content.ReadAsStringAsync();
                var prompt = JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
                await onSystemPrompt(prompt ?? "");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "Coming up next a classic from the vault." } } } }),
                    Encoding.UTF8, "application/json"),
            };
        }));
        return new LlmCopyWriter(
            new TemplateCopyWriter(new PatterTemplateRenderer()), http,
            new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://fake-llm.local", Model = "m", TimeoutSeconds = 5, MaxCopyChars = 450 }),
            new LlmCopyStatusHolder(), new FakeActivePersonaAccessor { Card = card }, NullLogger<LlmCopyWriter>.Instance, TimeProvider.System,
            new LlmCallRecorder(
                new LlmCallRing(new TestOptionsMonitor<LlmOptions>(new LlmOptions())),
                new LlmCallCauseCounters(TimeProvider.System)),
            new FakeDegradationModeReader(), quirkSelector: selector);
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
