using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.17, STORY-537) — takes accepted copies for the background life extractor. The copy writer
/// calls <see cref="Enqueue"/> after accepting a copy and moves on: it returns at once, never throws, and never
/// does the extraction on the air path.
/// </summary>
public interface ILifeExtractor
{
    /// <summary>Queues <paramref name="copy"/> for extraction; drops it when the queue is full.</summary>
    void Enqueue(AcceptedCopy copy);
}
