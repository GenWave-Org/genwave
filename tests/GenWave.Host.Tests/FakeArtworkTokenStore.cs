using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Minimal <see cref="IArtworkTokenStore"/> double for push-path specs (STORY-223, PLAN T85):
/// mints a stable, deterministic token from <c>mediaId</c> so a test can assert on the exact
/// resulting <c>url=</c> annotation without a database. Mirrors <see cref="FakeStationIdentityProvider"/>
/// /<see cref="FakeOptionsMonitor{T}"/> — shared here for specs that don't need their own isolated
/// copy. <see cref="ResolveAsync"/> is not exercised by any push-path spec — Story222's own
/// artwork-endpoint specs script that half with a purpose-built fake instead.
/// </summary>
sealed class FakeArtworkTokenStore : IArtworkTokenStore
{
    public Task<string> GetOrCreateTokenAsync(long mediaId, CancellationToken ct) =>
        Task.FromResult($"tok{mediaId.ToString(CultureInfo.InvariantCulture)}");

    public Task<ArtworkTokenResolution?> ResolveAsync(string token, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by any push-path spec (STORY-223/T85).");
}
