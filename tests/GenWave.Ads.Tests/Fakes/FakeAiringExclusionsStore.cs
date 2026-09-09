using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAdSpotStore"/> double exercising only <see cref="ListAiringExclusionsAsync"/> —
/// <see cref="LibraryAdSpotSource"/>'s one call onto <see cref="IAdSpotStore"/> (PLAN T439) — via a
/// plain in-memory model: a set of paused sponsor ids, plus a sponsor-id-per-ready-media-id map
/// standing in for the join <c>AdSpotRepository.ListAiringExclusionsAsync</c> runs in SQL against
/// <c>station.ad_spot</c>/<c>station.sponsor</c> (that SQL itself is proven live, over a real
/// Postgres, by <c>GenWave.MediaLibrary.Tests.Specs.Story418_AiringExclusionsSql</c> — this fake
/// proves <see cref="LibraryAdSpotSource"/>'s OWN call shape and relaxation branch against a
/// deterministic double instead, the <see cref="FakeAdSpotCatalog"/> precedent one file over). Every
/// other <see cref="IAdSpotStore"/> member is unreachable from <see cref="LibraryAdSpotSource"/> and
/// throws if ever called, so an accidental new call site fails loudly instead of silently returning
/// nothing.
/// </summary>
public sealed class FakeAiringExclusionsStore : IAdSpotStore
{
    readonly HashSet<long> pausedSponsorIds = [];
    readonly Dictionary<long, long> sponsorIdByReadyMediaId = [];

    /// <summary>Every <c>(recentMediaIds, window)</c> pair a caller has passed to
    /// <see cref="ListAiringExclusionsAsync"/>, in call order — lets a spec assert exactly what
    /// <see cref="LibraryAdSpotSource"/> handed this seam on the strict pick versus the relaxed one.</summary>
    public List<(IReadOnlyList<long> Recent, int Window)> Calls { get; } = [];

    public FakeAiringExclusionsStore AddReadySpot(long sponsorId, long mediaId)
    {
        sponsorIdByReadyMediaId[mediaId] = sponsorId;
        return this;
    }

    public FakeAiringExclusionsStore SetPaused(long sponsorId, bool paused)
    {
        if (paused)
            pausedSponsorIds.Add(sponsorId);
        else
            pausedSponsorIds.Remove(sponsorId);
        return this;
    }

    /// <summary>Mirrors <c>AdSpotRepository.ListAiringExclusionsAsync</c>'s own predicate in plain
    /// C#: a seeded ready media id is withheld when its sponsor is paused, OR when its sponsor also
    /// owns any media id among the first <paramref name="window"/> entries of
    /// <paramref name="recentMediaIds"/> — the SAME <c>IEnumerable.Take</c>-on-a-non-positive-window
    /// shape the real query relies on (documented .NET behavior, no separate clamp needed).</summary>
    public Task<IReadOnlyList<long>> ListAiringExclusionsAsync(
        IReadOnlyList<long> recentMediaIds, int window, CancellationToken ct)
    {
        Calls.Add((recentMediaIds.ToArray(), window));

        var recentSponsorIds = recentMediaIds
            .Take(window)
            .Where(sponsorIdByReadyMediaId.ContainsKey)
            .Select(mediaId => sponsorIdByReadyMediaId[mediaId])
            .ToHashSet();

        IReadOnlyList<long> excluded = sponsorIdByReadyMediaId
            .Where(entry => pausedSponsorIds.Contains(entry.Value) || recentSponsorIds.Contains(entry.Value))
            .Select(entry => entry.Key)
            .ToArray();
        return Task.FromResult(excluded);
    }

    public Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpot?> GetByIdAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpotTransitionOutcome> ApproveAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpotTransitionOutcome> RetryAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpotTransitionOutcome> UpdateAsync(long id, AdSpotEdit edit, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpot?> StampVoicePlanIfNullAsync(long id, string voicePlanJson, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpot?> StampBedIfNullAsync(long id, long bedMediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<bool> MarkReadyAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpotPage> ListByStateAsync(AdState? state, long? sponsorId, int limit, int offset, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<int> CountStockGeneratedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<IReadOnlyList<AdSpot>> ListReadyOlderThanAsync(TimeSpan age, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<IReadOnlyList<long>> FindRenderingPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<bool> ReArmAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpotJobStampOutcome> StampJobAsync(long id, string kind, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<bool> ClearJobAsync(long id, string? error, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<bool> StampPreviewAsync(long id, string path, string key, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<AdSpot?> ClaimForPromotionAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");
}
