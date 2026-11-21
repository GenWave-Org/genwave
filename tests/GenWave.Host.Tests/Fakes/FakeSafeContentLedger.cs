using GenWave.Host.Seeding;

namespace GenWave.Host.Tests.Fakes;

/// <summary>In-memory <see cref="ISafeContentLedger"/>: what the seeder recorded, and what a sweep rewrote.</summary>
sealed class FakeSafeContentLedger : ISafeContentLedger
{
    public IReadOnlyList<SafeContentRecord> Records { get; private set; } = [];

    public int SaveCount { get; private set; }

    /// <summary>Makes every save from now on throw, as a database that has gone away would.</summary>
    public bool FailSaves { get; set; }

    public Task<IReadOnlyList<SafeContentRecord>> ListAsync(CancellationToken ct) => Task.FromResult(Records);

    public Task SaveAsync(IReadOnlyList<SafeContentRecord> records, CancellationToken ct)
    {
        if (FailSaves)
            throw new InvalidOperationException("ledger save failed");

        Records = records.ToList();
        SaveCount++;
        return Task.CompletedTask;
    }
}
