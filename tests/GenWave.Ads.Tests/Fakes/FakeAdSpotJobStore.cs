using System.Collections.Concurrent;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAdSpotStore"/> double for <see cref="AdSpotJobService"/> unit specs (PLAN T441) —
/// only the four members that class ever calls (<see cref="GetByIdAsync"/>, <see cref="StampJobAsync"/>,
/// <see cref="ClearJobAsync"/>, <see cref="UpdateAsync"/>) are implemented for real; every other member
/// throws (the <see cref="FakeAdSpotStore"/> "narrow double, throw on unused" precedent, same Fakes
/// directory) since this fake exists for one class's own job-lifecycle race, not the full state
/// machine.
///
/// <para>
/// <b><see cref="OnClearJob"/> is the deterministic race-reproduction hook.</b> A real requeue-during-
/// cleanup race (STORY-422, PLAN T441) needs a caller to re-enter
/// <see cref="AdSpotJobService.TryEnqueueAsync"/> for the SAME spot id from INSIDE the original job's
/// own <c>ClearJobAsync</c> call — a window no amount of wall-clock/thread timing reproduces reliably.
/// <see cref="ClearJobAsync"/> mutates the row FIRST (the row genuinely reads as unstamped, exactly as
/// it would once the real store's write commits), THEN awaits this hook before returning — so a spec
/// wiring the hook to a fresh <c>TryEnqueueAsync</c> call reproduces the exact topology: the new job's
/// token lands in <c>AdSpotJobService</c>'s own dictionary while the OLD job is still executing, not
/// yet at its own <c>finally</c>.
/// </para>
/// </summary>
public sealed class FakeAdSpotJobStore : IAdSpotStore
{
    // ConcurrentDictionary, not plain Dictionary: AdSpotJobService's own consumer thread writes
    // through ClearJobAsync while a spec's own test thread polls ClearJobCallCount concurrently
    // (Story422_JobTokenSurvivesAnImmediateRequeue's own bounded wait) — a plain Dictionary read
    // racing that write is undefined behaviour, not just a stale read.
    readonly ConcurrentDictionary<long, AdSpot> spots = new();
    readonly ConcurrentDictionary<long, int> clearJobCallCounts = new();

    /// <summary>Fires from inside <see cref="ClearJobAsync"/>, after that call's own row mutation is
    /// already applied but before it returns — a spec sets this to re-enter
    /// <see cref="AdSpotJobService.TryEnqueueAsync"/> for the same id and should clear this back to
    /// <see langword="null"/> on its own first use so the requeued job's OWN <see cref="ClearJobAsync"/>
    /// call does not recurse forever.</summary>
    public Func<long, Task>? OnClearJob { get; set; }

    /// <summary>Seeds a draft spot with a sponsor, brief, and no job in flight — the one shape
    /// <see cref="AdSpotJobService.RunWriteAsync"/>'s happy path needs to run to completion.</summary>
    public void Seed(long id, long sponsorId, string brief, int spotSeconds = 30) =>
        spots[id] = new AdSpot(
            id, sponsorId, "Sponsor", "Spot", brief, Script: null, AdSource.Owner, PackSlug: null,
            spotSeconds, VoicePlan: null, BedMediaId: null, AdState.Draft, FailReason: null, MediaId: null,
            Generation: 0, CreatedAt: DateTime.UtcNow, StateChangedAt: DateTime.UtcNow, RenderedAt: null,
            RetiredAt: null, Version: "1");

    /// <summary>How many times <see cref="ClearJobAsync"/> has been called for <paramref name="id"/> —
    /// the race-reproduction fact's own assertion reads this: a job silently skipped by a by-key
    /// removal evicting a newer job's own token never reaches its own <c>ClearJobAsync</c> call at
    /// all.</summary>
    public int ClearJobCallCount(long id) => clearJobCallCounts.GetValueOrDefault(id);

    public Task<AdSpot?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(spots.GetValueOrDefault(id));

    public Task<AdSpotJobStampOutcome> StampJobAsync(long id, string kind, CancellationToken ct)
    {
        if (!spots.TryGetValue(id, out var spot))
            return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.NotFound, null));

        if (spot.JobKind is not null)
            return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.Busy, null));

        spot = spot with { JobKind = kind, JobStartedAt = DateTime.UtcNow, JobError = null, JobFailedKind = null };
        spots[id] = spot;
        return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.Stamped, spot));
    }

    public async Task<bool> ClearJobAsync(long id, string? error, CancellationToken ct)
    {
        if (spots.TryGetValue(id, out var spot))
            spots[id] = spot with
            {
                JobKind = null, JobStartedAt = null, JobError = error,
                JobFailedKind = error is null ? null : spot.JobKind,
            };

        clearJobCallCounts.AddOrUpdate(id, 1, (_, count) => count + 1);

        var hook = OnClearJob;
        if (hook is not null)
            await hook(id);

        return true;
    }

    public Task<AdSpotTransitionOutcome> UpdateAsync(long id, AdSpotEdit edit, string expectedVersion, CancellationToken ct)
    {
        if (!spots.TryGetValue(id, out var spot))
            return Task.FromResult(new AdSpotTransitionOutcome(AdSpotWriteResult.NotFound, null));

        if (spot.Version != expectedVersion)
            return Task.FromResult(new AdSpotTransitionOutcome(AdSpotWriteResult.Conflict, null));

        spot = spot with
        {
            SponsorId = edit.SponsorId ?? spot.SponsorId,
            Title = edit.Title ?? spot.Title,
            Brief = edit.Brief ?? spot.Brief,
            Script = edit.Script ?? spot.Script,
            VoicePlan = edit.VoicePlan ?? spot.VoicePlan,
            SpotSeconds = edit.SpotSeconds ?? spot.SpotSeconds,
            BedMediaId = edit.BedMediaId ?? spot.BedMediaId,
            Version = (int.Parse(spot.Version) + 1).ToString(),
        };
        spots[id] = spot;
        return Task.FromResult(new AdSpotTransitionOutcome(AdSpotWriteResult.Updated, spot));
    }

    public Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpotTransitionOutcome> ApproveAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpotTransitionOutcome> RetryAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpot?> StampVoicePlanIfNullAsync(long id, string voicePlanJson, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpot?> StampBedIfNullAsync(long id, long bedMediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> MarkReadyAsync(long id, long mediaId, int renderVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpot?> FindStaleReadyAsync(int currentVersion, IReadOnlyCollection<long> excludeIds, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> SwapRenderedMediaAsync(long id, long oldMediaId, long newMediaId, int renderVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpotPage> ListByStateAsync(AdState? state, long? sponsorId, int limit, int offset, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<int> CountStockGeneratedAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<IReadOnlyList<AdSpot>> ListReadyOlderThanAsync(TimeSpan age, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<IReadOnlyList<long>> FindRenderingPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> ReArmAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<IReadOnlyList<long>> ListAiringExclusionsAsync(
        IReadOnlyList<long> recentMediaIds, int window, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> StampPreviewAsync(long id, string path, string key, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<AdSpot?> ClaimForPromotionAsync(long id, string expectedVersion, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<IReadOnlyList<AdSpot>> ListPreviewsToSweepAsync(TimeSpan retention, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> ClearPreviewAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<IReadOnlyList<PendingAdSpotRetire>> ListPendingRetiresAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> ClearPendingRetireAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task ClearReferencedPendingRetiresAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<IReadOnlyList<PendingAdSpotConfirm>> ListPendingConfirmsAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> ClearPendingConfirmAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");

    public Task<bool> IsReadyOnMediaAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotJobService.");
}
