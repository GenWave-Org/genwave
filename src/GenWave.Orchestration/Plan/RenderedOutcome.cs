using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>The slot rendered, or was already ready, and reached the buffer as <see cref="Item"/>.</summary>
/// <param name="Item">The item that reached the buffer.</param>
public sealed record RenderedOutcome(MediaItem Item) : SlotOutcome;
