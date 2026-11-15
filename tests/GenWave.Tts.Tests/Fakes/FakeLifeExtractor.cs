namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// An <see cref="ILifeExtractor"/> that only records what it was handed. <see cref="ILifeExtractor.Enqueue"/> is a
/// synchronous <c>void</c>, so the copy writer cannot wait on the extraction; how slow the real extractor's model
/// is lives in the Memory-side <c>ScenarioTheModelIsSlow</c>. This fake must not block inside <c>Enqueue</c>: that
/// would delay the render.
/// </summary>
public sealed class FakeLifeExtractor : ILifeExtractor
{
    public List<AcceptedCopy> Received { get; } = [];

    public void Enqueue(AcceptedCopy copy) => Received.Add(copy);
}
