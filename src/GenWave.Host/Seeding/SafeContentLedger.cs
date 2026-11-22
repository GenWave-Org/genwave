using System.Text.Json;
using GenWave.MediaLibrary.Station;

namespace GenWave.Host.Seeding;

/// <summary><see cref="ISafeContentLedger"/> over one <c>station.settings</c> row, reached the way <see cref="SafeLoopSeedMarkerStore"/> reaches its own.</summary>
public sealed class SafeContentLedger(string connectionString, ILogger<SafeContentLedger> logger) : ISafeContentLedger
{
    /// <summary>Outside the <c>Station:*</c> namespace and off the settings allowlist, like <see cref="SafeLoopSeedMarkerStore.Key"/>.</summary>
    public const string Key = "Internal:BootSeed:SafeContent";

    readonly StationSettingsRepository repository = new(connectionString);

    public async Task<IReadOnlyList<SafeContentRecord>> ListAsync(CancellationToken ct)
    {
        var json = await repository.ReadValueAsync(Key, ct);
        if (json is null)
            return [];

        try
        {
            var records = JsonSerializer.Deserialize<List<SafeContentRecord>>(json) ?? [];

            // A hand-edited record can lack its marker; it reads as made in no voice, so it is replaced like any stale item.
            return records.Select(r => r with { Marker = r.Marker ?? string.Empty }).ToList();
        }
        catch (JsonException ex)
        {
            // A damaged ledger must not stop the sweep: treated as empty, the seed row is found again by title.
            logger.LogWarning(ex, "Safe content ledger is unreadable; treating it as empty");
            return [];
        }
    }

    public Task SaveAsync(IReadOnlyList<SafeContentRecord> records, CancellationToken ct) =>
        repository.WriteAsync(Key, records, ct);
}
