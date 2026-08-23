using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Stateful double for <see cref="IAnnouncementStore"/> (STORY-357/359, PLAN T339) — mirrors
/// <c>FakeShowStore</c>'s own "record every call, script every outcome" shape one seam over.
/// <see cref="InsertCalls"/> is what <c>FeatureAnnouncementEndpoint.NoCappedRequestEverReachesTheStore</c>
/// asserts against: a 400/403/429 refusal from <c>AnnouncementsController</c> must never grow this
/// list.
/// </summary>
sealed class FakeAnnouncementStore : IAnnouncementStore
{
    public List<(string Message, bool Verbatim, string? RequestedVoice, AnnouncementSubmitter Submitter, TimeSpan? Ttl)> InsertCalls { get; } = [];

    /// <summary>Scripts <see cref="CountPendingAsync"/>'s return — the depth-cap fact drives this
    /// directly rather than actually inserting 12 rows first.</summary>
    public int PendingCount { get; set; }

    /// <summary>When true, <see cref="InsertOrCollapseAsync"/> returns <see langword="null"/> without
    /// recording a call — scripts the store's own 280-char CHECK backstop
    /// (<see cref="IAnnouncementStore.InsertOrCollapseAsync"/>'s own remarks).</summary>
    public bool DeclineNextInsert { get; set; }

    /// <summary>Scripts <see cref="HistoryAsync"/>'s own return (PLAN T344) — a scenario populates
    /// this to prove <c>AnnouncementsController.GetHistory</c>'s own cap/DTO-projection behavior
    /// without a real Postgres row to query.</summary>
    public IReadOnlyList<AnnouncementHistoryEntry> HistoryRows { get; set; } = [];

    /// <summary>Every <c>limit</c> <see cref="HistoryAsync"/> was called with, in call order — proves
    /// the controller's own capping (50 default, 200 max) reaches this seam rather than passing a
    /// caller-supplied value straight through.</summary>
    public List<int> HistoryCalls { get; } = [];

    long nextId = 1;

    public Task<long?> InsertOrCollapseAsync(
        string message, bool verbatim, string? requestedVoice, AnnouncementSubmitter submitter, TimeSpan? ttl, CancellationToken ct)
    {
        if (DeclineNextInsert)
            return Task.FromResult((long?)null);

        InsertCalls.Add((message, verbatim, requestedVoice, submitter, ttl));
        return Task.FromResult((long?)nextId++);
    }

    public Task<int> CountPendingAsync(CancellationToken ct) => Task.FromResult(PendingCount);

    public Task<IReadOnlyList<AnnouncementHistoryEntry>> HistoryAsync(int limit, CancellationToken ct)
    {
        HistoryCalls.Add(limit);
        return Task.FromResult(HistoryRows);
    }
}
