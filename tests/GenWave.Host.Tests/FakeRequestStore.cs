using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IRequestStore"/> double (STORY-224/225, PLAN T87/T88): records every
/// <see cref="InsertAsync"/>/<see cref="EvictOldestPendingAsync"/>/<see cref="MarkParsedAsync"/> call
/// so a spec can assert the intake controller's/parser's write behavior through the real pipeline
/// without a Postgres connection. <see cref="PendingCount"/> scripts the
/// <see cref="CountPendingAsync"/> read the pending-cap check depends on; eviction decrements it by
/// one, mirroring the real store's "evict the oldest pending row" contract closely enough for the
/// controller's own cap-then-insert logic to exercise correctly.
///
/// <see cref="InsertAsync"/> also seeds <see cref="UnparsedById"/> — the fake's own "unparsed"
/// bookkeeping (SPEC F87.4) — so a wish-parser spec can insert through this same fake and then drive
/// <c>RequestParserService.ParseOneAsync</c>/<c>RecoverPendingAsync</c> against it without a second,
/// separately-scripted store.
/// </summary>
sealed class FakeRequestStore : IRequestStore
{
    public int PendingCount { get; set; }
    public List<(string Wish, DateTimeOffset ExpiresAt)> Inserted { get; } = [];
    public int EvictionCalls { get; private set; }

    /// <summary>Rows still awaiting their first parse — scriptable directly by a wish-parser spec,
    /// and auto-populated by <see cref="InsertAsync"/> for a spec that drives the whole flow.
    /// <see cref="DateTime"/>, not <see cref="DateTimeOffset"/> — matches <see cref="UnparsedRequest.ExpiresAt"/>'s
    /// own "Postgres timestamptz reads back as DateTime" shape.</summary>
    public Dictionary<long, (string Wish, DateTime ExpiresAt)> UnparsedById { get; } = [];

    public List<(long Id, string? Artist, string? Title, IReadOnlyList<string> Moods, bool Unmatched)> MarkParsedCalls { get; } = [];

    public List<(long Id, long MediaId)> MarkMatchedCalls { get; } = [];
    public List<long> MarkUnmatchedCalls { get; } = [];

    public Task<long> InsertAsync(string wish, DateTimeOffset expiresAt, CancellationToken ct)
    {
        Inserted.Add((wish, expiresAt));
        PendingCount++;
        var id = (long)Inserted.Count;
        UnparsedById[id] = (wish, expiresAt.UtcDateTime);
        return Task.FromResult(id);
    }

    public Task<int> CountPendingAsync(CancellationToken ct) => Task.FromResult(PendingCount);

    public Task EvictOldestPendingAsync(CancellationToken ct)
    {
        EvictionCalls++;
        if (PendingCount > 0)
            PendingCount--;
        return Task.CompletedTask;
    }

    public Task<UnparsedRequest?> GetForParseAsync(long id, CancellationToken ct) =>
        Task.FromResult(UnparsedById.TryGetValue(id, out var row) ? new UnparsedRequest(id, row.Wish, row.ExpiresAt) : null);

    public Task<IReadOnlyList<long>> ListUnparsedPendingIdsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<long>>(UnparsedById.Keys.OrderBy(id => id).ToList());

    public Task MarkParsedAsync(
        long id, string? artist, string? title, IReadOnlyList<string> moods, bool unmatched, CancellationToken ct)
    {
        MarkParsedCalls.Add((id, artist, title, moods, unmatched));
        UnparsedById.Remove(id);
        return Task.CompletedTask;
    }

    public Task MarkMatchedAsync(long id, long mediaId, CancellationToken ct)
    {
        MarkMatchedCalls.Add((id, mediaId));
        return Task.CompletedTask;
    }

    public Task MarkUnmatchedAsync(long id, CancellationToken ct)
    {
        MarkUnmatchedCalls.Add(id);
        return Task.CompletedTask;
    }
}
