using GenWave.Core.Abstractions;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>In-memory <see cref="IAdAudioReferences"/> (gh-#3).</summary>
public sealed class FakeAdAudioReferences : IAdAudioReferences
{
    public HashSet<long> InUse { get; } = [];
    public HashSet<string> PreviewPaths { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlySet<long>> ListMediaIdsInUseAsync(DateTimeOffset retiredBefore, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<long>>(InUse);

    public Task<IReadOnlySet<string>> ListPreviewPathsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<string>>(PreviewPaths);
}
