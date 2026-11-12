namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using GenWave.Tts.Tests.Fakes;

/// <summary>
/// STORY-543 arrangement: a persona whose card tagline is <see cref="Tagline"/>, utterances that used it at
/// chosen positions (1 = newest, all inside the last hour), and a fake LLM that answers the copy then a fresh line.
/// </summary>
sealed class TaglineRun
{
    public const string Tagline = "Every record is a footnote to another record.";
    public const string CopyWithTagline = "Every record is a footnote to another record. Now the next one up.";
    const string Fresh = "Next up, a real stunner for the late shift.";

    public required SegmentCopy Copy { get; init; }
    public required RecordingUtteranceStore Store { get; init; }
    public required RecordingStationEventSink Events { get; init; }
    public required List<string> SystemPrompts { get; init; }

    public static TaglineRun Arrange(int[] usedAt, string copy, int entries = 10, int minutesApart = 1)
    {
        var store = new RecordingUtteranceStore();
        var now = DateTimeOffset.UtcNow;
        for (var position = 1; position <= entries; position++)
        {
            var text = usedAt.Contains(position) ? $"Settle in. {Tagline} Number {position}." : $"Plain talk number {position} with nothing repeated at all.";
            store.Seeded.Add(new UtteranceEntry(
                position, QuirkSpecHarness.PersonaId, null, UtteranceKind.Patter, "LeadIn",
                ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)), text, false,
                now.AddMinutes(-position * minutesApart), null));
        }

        var prompts = new List<string>();
        var systemPrompts = new List<string>();
        var events = new RecordingStationEventSink();
        var card = QuirkSpecHarness.Card(["Sighs, often"], null) with { Tagline = Tagline };
        var writer = QuirkSpecHarness.WriterFor(
            card, new NoSelection(), utteranceStore: store, httpOverride: QuirkSpecHarness.ScriptedHttp(prompts, [copy, Fresh], systemPrompts),
            repetitionCheck: new RepetitionCheck(), events: events, taglineLedger: new TaglineLedger());
        var request = QuirkSpecHarness.Request(ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 9)).ToString());
        var result = writer.WriteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return new TaglineRun { Copy = result, Store = store, Events = events, SystemPrompts = systemPrompts };
    }

    sealed class NoSelection : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(new QuirkSelection(["Sighs, often"], []));
    }
}
