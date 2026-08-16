using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory, seedable <see cref="IStationImageStore"/> double — Story335's own T298 route facts use
/// this to arrange (or omit) an owner-customized station image ahead of the dj-token fallback read in
/// <c>SpectatorArtworkController</c>, through <c>WebApplicationFactory&lt;Program&gt;</c>, with no live
/// Postgres fixture. Mirrors <see cref="FakePersonaAvatarStore"/>'s own minimal-contract idiom — only
/// <see cref="GetAsync"/> is exercised by that route; the write paths this task never reaches throw
/// <see cref="NotSupportedException"/>.
/// </summary>
sealed class FakeStationImageStore : IStationImageStore
{
    StationImage? current;

    /// <summary>Arranges the row this double's <see cref="GetAsync"/> reports — <see langword="null"/>
    /// (the default) mirrors a station that has never customized its image.</summary>
    public void Seed(StationImage? image) => current = image;

    public Task<StationImage?> GetAsync(CancellationToken ct) => Task.FromResult(current);

    public Task UpsertAsync(byte[] bytes, string sha256, string token, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story335's T298 route facts.");

    public Task<bool> DeleteAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by Story335's T298 route facts.");
}
