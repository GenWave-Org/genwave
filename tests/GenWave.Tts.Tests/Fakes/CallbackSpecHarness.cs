namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Memory;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// STORY-548/550 writer arrangement: a real <see cref="CallbackPlanner"/> with a draw that always passes, over an
/// in-memory <see cref="ICallbackStore"/> holding one eligible memory, wired into an <see cref="LlmCopyWriter"/> whose
/// model answers with <paramref name="reply"/>. The specs read what the planner was asked, what the prompt carried and
/// what the store holds afterwards.
/// </summary>
sealed class CallbackSpecHarness
{
    public const string MemoryText = "has a dog named Daisy";

    public CallbackSpecHarness(
        string reply = "Coming up next a classic from the vault.", MemoryBudget? budget = null,
        bool storeThrows = false)
    {
        Store = new RecordingCallbackStore(MemoryText) { Throws = storeThrows };
        Utterances = new RecordingUtteranceStore();
        Writer = QuirkSpecHarness.WriterFor(
            QuirkSpecHarness.Card([], null), new NoQuirks(),
            utteranceStore: Utterances,
            httpOverride: QuirkSpecHarness.ScriptedHttp(UserPrompts, [reply]),
            contextWindow: new FixedContextWindow(budget ?? MemoryBudget.Standard),
            memoryRecall: new NoMemoryRecall(),
            callbacks: new BreakCallbacks(
                new CallbackPlanner(Store, new AlwaysPassDraw()), new FixedRate(6),
                new FakePersonaRoster("DJ Nova", "Rusty Strings"), NullLogger<BreakCallbacks>.Instance));
    }

    public RecordingCallbackStore Store { get; }

    public RecordingUtteranceStore Utterances { get; }

    public LlmCopyWriter Writer { get; }

    public List<string> UserPrompts { get; } = [];

    public string Prompts => string.Join("\n---\n", UserPrompts);

    public static SegmentRequest Request(SegmentKind kind) => QuirkSpecHarness.Request(null) with { Kind = kind };

    /// <summary>A request that can be recorded: the persona and the shift key are stamped.</summary>
    public static SegmentRequest RecordableRequest(SegmentKind kind) =>
        QuirkSpecHarness.Request(ShiftKey.Unscheduled(QuirkSpecHarness.PersonaId, new DateOnly(2026, 11, 16)).ToString()) with { Kind = kind };

    sealed class AlwaysPassDraw : Random
    {
        public override double NextDouble() => 0.0;
    }

    sealed class FixedRate(int perHour) : ICallbackRate
    {
        public int CallbacksPerHour => perHour;
    }

    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    sealed class NoMemoryRecall : IMemoryRecallSource
    {
        public Task<MemoryPacket> TakeAsync(long personaId, MemoryBudget budget, CancellationToken ct) => Task.FromResult(MemoryPacket.Empty);

        public Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct) =>
            Task.FromResult(MemoryPacket.Empty);
    }
}
