using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests.Fakes;

/// <summary>An in-memory <see cref="ITalkDroppedLateReader"/> holding the <c>occurred_at</c> of each late-talk row; counts like the SQL does (at or after <c>since</c>).</summary>
public sealed class FakeTalkDroppedLateReader(params DateTimeOffset[] occurredAt) : ITalkDroppedLateReader
{
    public Task<int?> CountSinceAsync(DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult<int?>(occurredAt.Count(at => at >= since));
}
