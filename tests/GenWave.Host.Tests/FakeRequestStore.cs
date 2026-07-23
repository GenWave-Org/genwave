using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IRequestStore"/> double (STORY-224, PLAN T87): records every
/// <see cref="InsertAsync"/>/<see cref="EvictOldestPendingAsync"/> call so a spec can assert the
/// intake controller's write behavior through the real pipeline without a Postgres connection.
/// <see cref="PendingCount"/> scripts the <see cref="CountPendingAsync"/> read the pending-cap
/// check depends on; eviction decrements it by one, mirroring the real store's "evict the oldest
/// pending row" contract closely enough for the controller's own cap-then-insert logic to exercise
/// correctly.
/// </summary>
sealed class FakeRequestStore : IRequestStore
{
    public int PendingCount { get; set; }
    public List<(string Wish, DateTimeOffset ExpiresAt)> Inserted { get; } = [];
    public int EvictionCalls { get; private set; }

    public Task<long> InsertAsync(string wish, DateTimeOffset expiresAt, CancellationToken ct)
    {
        Inserted.Add((wish, expiresAt));
        PendingCount++;
        return Task.FromResult((long)Inserted.Count);
    }

    public Task<int> CountPendingAsync(CancellationToken ct) => Task.FromResult(PendingCount);

    public Task EvictOldestPendingAsync(CancellationToken ct)
    {
        EvictionCalls++;
        if (PendingCount > 0)
            PendingCount--;
        return Task.CompletedTask;
    }
}
