using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Writes accrued <see cref="MemoryKinds.Episode"/> rows (SPEC F218.15, F218.16). Every write evicts, in its own
/// transaction, that persona's accrued episodes older than 28 days and those beyond the cap of 50.
/// </summary>
public interface IEpisodeStore
{
    /// <summary>Writes one accrued episode for <see cref="NewEpisode.PersonaId"/>.</summary>
    Task AppendAsync(NewEpisode episode, CancellationToken ct);

    /// <summary>
    /// Writes one <paramref name="happening"/> and an accrued episode per entry of <paramref name="episodes"/>,
    /// each pointing at it, all in one transaction.
    /// </summary>
    Task AppendSharedAsync(NewMemoryEvent happening, IReadOnlyList<NewEpisode> episodes, CancellationToken ct);
}
