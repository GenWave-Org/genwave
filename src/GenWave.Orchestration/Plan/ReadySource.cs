using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// The slot is already a playable item with no render at plan or air time — a crosstalk clip, a
/// pooled station id, or a vended ad spot (SPEC F186's Ready column). Carries no speaker
/// (SPEC F187.4).
/// </summary>
/// <param name="Item">The ready-to-play item.</param>
public sealed record ReadySource(MediaItem Item) : SlotSource;
