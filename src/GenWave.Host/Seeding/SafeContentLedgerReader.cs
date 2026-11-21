namespace GenWave.Host.Seeding;

/// <summary>
/// Reads the safe content ledger the way the sweep and the Status count must agree on: a station seeded before the
/// ledger existed has no entry, so its seed row is adopted as one unmarked (always stale) record.
/// </summary>
static class SafeContentLedgerReader
{
    public static async Task<List<SafeContentRecord>> ListWithAdoptedSeedAsync(
        ISafeContentLedger ledger, ISeedRowFinder seedRowFinder, CancellationToken ct)
    {
        var records = (await ledger.ListAsync(ct)).ToList();
        if (records.Count == 0 && await seedRowFinder.FindSeedMediaIdAsync(ct) is { } seedId)
            records.Add(new SafeContentRecord(seedId, Marker: string.Empty));
        return records;
    }
}
