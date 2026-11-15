using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// One queued accrual (SPEC F218.15): the episodes to write, and the shared happening they point at when
/// there is one (a crosstalk), so the store can write the whole thing in one transaction.
/// </summary>
/// <param name="Happening">The shared <c>memory_event</c>, or <see langword="null"/> for a solo episode.</param>
/// <param name="Episodes">The episodes to write, one per persona that remembers it.</param>
internal sealed record EpisodeWrite(NewMemoryEvent? Happening, IReadOnlyList<NewEpisode> Episodes);
