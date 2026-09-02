using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAdSpotStore"/> double for <see cref="AdRenderService"/> specs (T401 review F1) —
/// <see cref="AdRenderService"/> only ever calls <see cref="MarkReadyAsync"/> (via its confirmAsync
/// delegate) and <see cref="MarkFailedAsync"/> (via its own TryMarkFailedAsync); every other member
/// throws if ever called, so an accidental new call site fails loudly instead of silently returning
/// nothing (the <c>FakeAdSpotCatalog</c> precedent, this same Fakes directory).
/// </summary>
public sealed class FakeAdSpotStore : IAdSpotStore
{
    public int MarkReadyCalls { get; private set; }
    public long? LastMarkReadySpotId { get; private set; }
    public long? LastMarkReadyMediaId { get; private set; }
    public bool MarkReadyResult { get; set; } = true;

    public int MarkFailedCalls { get; private set; }
    public long? LastMarkFailedSpotId { get; private set; }
    public string? LastMarkFailedReason { get; private set; }
    public bool MarkFailedResult { get; set; } = true;

    public Task<bool> MarkReadyAsync(long id, long mediaId, CancellationToken ct)
    {
        MarkReadyCalls++;
        LastMarkReadySpotId = id;
        LastMarkReadyMediaId = mediaId;
        return Task.FromResult(MarkReadyResult);
    }

    public Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct)
    {
        MarkFailedCalls++;
        LastMarkFailedSpotId = id;
        LastMarkFailedReason = failReason;
        return Task.FromResult(MarkFailedResult);
    }

    public Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotTransitionOutcome> ApproveAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotTransitionOutcome> RetryAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotPage> ListByStateAsync(AdState? state, int limit, int offset, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<int> CountReadyGeneratedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<AdSpot>> ListReadyOlderThanAsync(TimeSpan age, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<long>> FindRenderingPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> ReArmAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");
}
