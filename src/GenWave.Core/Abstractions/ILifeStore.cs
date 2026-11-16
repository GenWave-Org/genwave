using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Writes extracted <see cref="MemoryKinds.Life"/> rows (SPEC F218.18). A claim whose normalised text
/// (<see cref="LifeMemoryRules.Normalise"/>) already exists for the persona, forgotten rows included, is not
/// written again. Every write evicts, in its own transaction, the persona's oldest never-aired extracted rows
/// beyond <see cref="LifeMemoryRules.Cap"/>; the rows just written are never the ones evicted.
/// </summary>
public interface ILifeStore
{
    /// <summary>Writes <paramref name="claim"/> unless the persona already has it. Returns whether a row was written.</summary>
    Task<bool> AddAsync(NewLifeClaim claim, CancellationToken ct);

    /// <summary>
    /// Writes <paramref name="outing"/> and a life row per new claim, each pointing at it, in one transaction.
    /// Claims the persona already has are skipped; when none is new no event is written either.
    /// </summary>
    Task AddOutingAsync(NewMemoryEvent outing, IReadOnlyList<NewLifeClaim> claims, CancellationToken ct);

    /// <summary>
    /// The persona's current (not forgotten) life rows, authored and extracted, newest first, at most
    /// <paramref name="limit"/>. What the extractor already knows about the DJ.
    /// </summary>
    Task<IReadOnlyList<string>> RecentLifeAsync(long personaId, int limit, CancellationToken ct);

    /// <summary>
    /// The persona's forgotten life rows, newest first, at most <paramref name="limit"/>. Never shown to the
    /// extractor model; the code-side duplicate and name checks read them so a forgotten claim cannot return reworded.
    /// </summary>
    Task<IReadOnlyList<string>> ForgottenLifeAsync(long personaId, int limit, CancellationToken ct);

    /// <summary>
    /// How many extracted life rows the persona gained at or after <paramref name="since"/>, forgotten rows
    /// included (the extraction happened either way); the per-window cap counts this.
    /// </summary>
    Task<int> CountExtractedSinceAsync(long personaId, DateTimeOffset since, CancellationToken ct);
}
