using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRequestStore"/> double for STORY-227 (SPEC F87.6, PLAN T90) — models exactly
/// the three-outcome state machine <see cref="RequestFulfillmentProvider"/> drives
/// (pending → fulfilled/expired via <see cref="TryMarkFulfilledAsync"/>/<see cref="ExpireStaleAsync"/>)
/// closely enough to prove the provider's own selection/one-shot/expiry logic against fakes rather
/// than a real Postgres connection. Every member unrelated to the fulfillment rung
/// (<see cref="InsertAsync"/> and friends) throws — this fake's whole purpose is driving
/// <see cref="RequestFulfillmentProvider"/>, not the T86-T89 write path.
/// </summary>
sealed class FakeRequestStore : IRequestStore
{
    sealed class Row
    {
        public required long Id { get; init; }
        public required DateTimeOffset ReceivedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public long? MatchedMediaId { get; init; }
        public IReadOnlyList<string> Moods { get; init; } = [];
        public string Status { get; set; } = "pending";
    }

    readonly List<Row> rows = [];
    long nextId = 1;

    /// <summary>Seeds one pending row, returning its id.</summary>
    public long AddPending(
        DateTimeOffset expiresAt,
        long? matchedMediaId = null,
        IReadOnlyList<string>? moods = null,
        DateTimeOffset? receivedAt = null)
    {
        var id = nextId++;
        rows.Add(new Row
        {
            Id = id,
            ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            MatchedMediaId = matchedMediaId,
            Moods = moods ?? [],
        });
        return id;
    }

    /// <summary>The row's current status — <c>"pending"</c>/<c>"fulfilled"</c>/<c>"expired"</c>.</summary>
    public string StatusOf(long id) => rows.Single(r => r.Id == id).Status;

    public Task<FulfillableRequest?> GetOldestLiveAsync(DateTimeOffset now, CancellationToken ct)
    {
        var row = rows
            .Where(r => r.Status == "pending" && r.ExpiresAt >= now && (r.MatchedMediaId is not null || r.Moods.Count > 0))
            .OrderBy(r => r.ReceivedAt)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
        return Task.FromResult(row is null ? null : new FulfillableRequest(row.Id, row.MatchedMediaId, row.Moods));
    }

    public Task<int> ExpireStaleAsync(DateTimeOffset now, CancellationToken ct)
    {
        var stale = rows.Where(r => r.Status == "pending" && r.ExpiresAt < now).ToList();
        foreach (var row in stale)
            row.Status = "expired";
        return Task.FromResult(stale.Count);
    }

    public Task<bool> TryMarkFulfilledAsync(long id, CancellationToken ct)
    {
        var row = rows.SingleOrDefault(r => r.Id == id);
        if (row is null || row.Status != "pending") return Task.FromResult(false);

        row.Status = "fulfilled";
        return Task.FromResult(true);
    }

    // Not exercised by STORY-227 specs — this fake's whole purpose is the fulfillment rung above.
    public Task<long> InsertAsync(string wish, DateTimeOffset expiresAt, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");

    public Task<int> CountPendingAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");

    public Task EvictOldestPendingAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");

    public Task<UnparsedRequest?> GetForParseAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");

    public Task<IReadOnlyList<long>> ListUnparsedPendingIdsAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");

    public Task MarkParsedAsync(
        long id, string? artist, string? title, IReadOnlyList<string> moods, bool unmatched, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");

    public Task MarkMatchedAsync(long id, long mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");

    public Task MarkUnmatchedAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");
}
