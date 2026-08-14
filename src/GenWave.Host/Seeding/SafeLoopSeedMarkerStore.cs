using GenWave.MediaLibrary.Station;

namespace GenWave.Host.Seeding;

/// <summary>
/// <see cref="ISafeLoopSeedMarkerStore"/> backed by <see cref="StationSettingsRepository"/> against
/// the same <c>station.settings</c> table <see cref="GenWave.Host.Configuration.StationSettingsStore"/>
/// writes to, but reached through a separate, narrower seam so the marker key can never be
/// allowlisted by accident (F27.10).
///
/// gh-#406 slice 4: the raw <c>station.settings</c> row I/O this class used to open directly via
/// <c>NpgsqlConnection</c> now lives in <see cref="StationSettingsRepository"/> — this class builds
/// that repository internally from the same <see cref="connectionString"/> it always took (no DI
/// wiring change; <c>SafeLoopSeedServiceCollectionExtensions</c> still constructs this type exactly
/// as before) and keeps only the marker-key scoping that is genuinely this store's own concern.
/// </summary>
public sealed class SafeLoopSeedMarkerStore(string connectionString) : ISafeLoopSeedMarkerStore
{
    readonly StationSettingsRepository repository = new(connectionString);

    /// <summary>
    /// The marker key. Lives outside the <c>Station:*</c> config namespace (so it can never collide
    /// with a bound options section) and is absent from
    /// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/> by construction — nothing
    /// on the <c>GET</c>/<c>PUT /api/settings</c> path ever references it.
    /// </summary>
    public const string Key = "Internal:BootSeed:SafeLoopCompletedAt";

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(CancellationToken ct) => repository.ExistsAsync(Key, ct);

    /// <inheritdoc/>
    public Task MarkCompletedAsync(CancellationToken ct)
    {
        // The value carries a UTC timestamp for operator diagnosability (visible only via a direct
        // psql query — never through the settings API); its content is otherwise unused. This is the
        // same upsert WriteAsync already performs for every other settings key — no new repository
        // SQL needed for the write side (gh-#406 slice 4 only added ExistsAsync).
        return repository.WriteAsync(Key, DateTimeOffset.UtcNow, ct);
    }
}
