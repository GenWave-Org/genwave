namespace GenWave.Context.Tests.Fakes;

using GenWave.Core.Abstractions;

/// <summary>
/// Mutable <see cref="IContextCacheRootProvider"/> double: <see cref="Root"/> is whatever it is
/// currently set to, read fresh on every access — mirrors <c>FakeStationLocationProvider</c> one seam
/// over.
/// </summary>
sealed class FakeContextCacheRootProvider : IContextCacheRootProvider
{
    public string Root { get; set; } = string.Empty;
}
