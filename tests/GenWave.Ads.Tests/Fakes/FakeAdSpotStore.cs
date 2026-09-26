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
    public int? LastMarkReadyRenderVersion { get; private set; }
    public bool MarkReadyResult { get; set; } = true;

    public int MarkFailedCalls { get; private set; }
    public long? LastMarkFailedSpotId { get; private set; }
    public string? LastMarkFailedReason { get; private set; }
    public bool MarkFailedResult { get; set; } = true;

    /// <summary>gh-#854 — <see cref="AdRenderService.RenderStaleAsync"/>'s own confirm call, the SAME
    /// "record + return a settable result" shape <see cref="MarkReadyAsync"/> already keeps above.</summary>
    public int SwapRenderedMediaCalls { get; private set; }
    public long? LastSwapSpotId { get; private set; }
    public long? LastSwapOldMediaId { get; private set; }
    public long? LastSwapNewMediaId { get; private set; }
    public int? LastSwapRenderVersion { get; private set; }
    public bool SwapRenderedMediaResult { get; set; } = true;

    public Task<bool> MarkReadyAsync(long id, long mediaId, int renderVersion, CancellationToken ct)
    {
        MarkReadyCalls++;
        LastMarkReadySpotId = id;
        LastMarkReadyMediaId = mediaId;
        LastMarkReadyRenderVersion = renderVersion;
        return Task.FromResult(MarkReadyResult);
    }

    public Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct)
    {
        MarkFailedCalls++;
        LastMarkFailedSpotId = id;
        LastMarkFailedReason = failReason;
        return Task.FromResult(MarkFailedResult);
    }

    public Task<bool> SwapRenderedMediaAsync(long id, long oldMediaId, long newMediaId, int renderVersion, CancellationToken ct)
    {
        SwapRenderedMediaCalls++;
        LastSwapSpotId = id;
        LastSwapOldMediaId = oldMediaId;
        LastSwapNewMediaId = newMediaId;
        LastSwapRenderVersion = renderVersion;
        return Task.FromResult(SwapRenderedMediaResult);
    }

    public Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpot?> GetByIdAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotTransitionOutcome> UpdateAsync(long id, AdSpotEdit edit, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotTransitionOutcome> ApproveAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotTransitionOutcome> RetryAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpot?> StampVoicePlanIfNullAsync(long id, string voicePlanJson, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpot?> StampBedIfNullAsync(long id, long bedMediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotPage> ListByStateAsync(AdState? state, long? sponsorId, int limit, int offset, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<int> CountStockGeneratedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<AdSpot>> ListReadyOlderThanAsync(TimeSpan age, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpot?> FindStaleReadyAsync(int currentVersion, IReadOnlyCollection<long> excludeIds, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<long>> FindRenderingPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> ReArmAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<long>> ListAiringExclusionsAsync(
        IReadOnlyList<long> recentMediaIds, int window, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpotJobStampOutcome> StampJobAsync(long id, string kind, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> ClearJobAsync(long id, string? error, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> StampPreviewAsync(long id, string path, string key, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<AdSpot?> ClaimForPromotionAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<AdSpot>> ListPreviewsToSweepAsync(TimeSpan retention, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> ClearPreviewAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<PendingAdSpotRetire>> ListPendingRetiresAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> ClearPendingRetireAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task ClearReferencedPendingRetiresAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<IReadOnlyList<PendingAdSpotConfirm>> ListPendingConfirmsAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> ClearPendingConfirmAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");

    public Task<bool> IsReadyOnMediaAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdRenderService.");
}
