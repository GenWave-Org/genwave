using GenWave.Host.Seeding;

namespace GenWave.Host.Tests.Fakes;

/// <summary><see cref="ISeedRowFinder"/> that finds nothing.</summary>
sealed class FakeSeedRowFinder : ISeedRowFinder
{
    public Task<long?> FindSeedMediaIdAsync(CancellationToken ct) => Task.FromResult<long?>(null);
}
