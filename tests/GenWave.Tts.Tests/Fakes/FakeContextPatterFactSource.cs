namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Scriptable <see cref="IContextPatterFactSource"/> double (STORY-298, PLAN T225): vends whatever
/// was <see cref="Enqueue"/>'d, one fact per call, exactly like the real
/// <c>GenWave.Context.ContextPipeline.TryTakeDuePatterFact</c> — a call against an empty queue
/// returns <see langword="null"/>, standing in for EVERY real "nothing due" cause (no provider
/// registered, none enabled, none fresh, already vended this slot) without this double needing to
/// know or care which one applies; the pipeline's own freshness/cadence mechanics are pinned in
/// <c>GenWave.Context.Tests</c>, never re-derived here (see
/// <c>FeatureOneFactPatterLane.ScenarioStaleFactsNeverSpeak</c>'s own remarks).
/// <see cref="CallCount"/> lets a spec prove exactly how many times <see cref="LlmCopyWriter"/> ever
/// called in — the CQS-trap guard (PLAN T225) that a preview render must never touch this seam at
/// all.
/// </summary>
public sealed class FakeContextPatterFactSource : IContextPatterFactSource
{
    readonly Queue<ContextPatterFact> facts = new();

    public int CallCount { get; private set; }

    public void Enqueue(ContextPatterFact fact) => facts.Enqueue(fact);

    public ContextPatterFact? TryTakeDuePatterFact()
    {
        CallCount++;
        return facts.TryDequeue(out var fact) ? fact : null;
    }
}
