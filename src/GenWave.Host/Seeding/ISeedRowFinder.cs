namespace GenWave.Host.Seeding;

/// <summary>Finds the boot seed's row when no ledger entry names it (a station seeded before the sweep existed).</summary>
public interface ISeedRowFinder
{
    Task<long?> FindSeedMediaIdAsync(CancellationToken ct);
}
