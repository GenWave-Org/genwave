namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Scriptable <see cref="IShowFlavorLineSource"/> double (STORY-308, PLAN T249): vends whatever was
/// <see cref="Enqueue"/>'d, one line per call, mirroring <see cref="FakeContextPatterFactSource"/>'s
/// own shape exactly one seam over. <see cref="CallCount"/> lets a spec prove exactly how many times
/// <see cref="LlmCopyWriter"/> ever called in — the CQS-trap guard (SPEC F116.3's own "context wins...
/// the show gate stays open" arbitration) that a break which loses the slot to a context fact must
/// never even ASK this seam at all, not merely discard whatever it returns.
/// </summary>
public sealed class FakeShowFlavorLineSource : IShowFlavorLineSource
{
    readonly Queue<ShowFlavorFact> facts = new();

    public int CallCount { get; private set; }

    public void Enqueue(ShowFlavorFact fact) => facts.Enqueue(fact);

    public ShowFlavorFact? TryTakeDueShowLine()
    {
        CallCount++;
        return facts.TryDequeue(out var fact) ? fact : null;
    }
}
