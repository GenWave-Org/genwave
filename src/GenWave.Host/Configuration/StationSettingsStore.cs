using System.Data.Common;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Host.Configuration;

/// <summary>
/// Writes allowlisted settings to <c>station.settings</c> and signals the
/// <see cref="StationSettingsConfigurationProvider"/> to reload so
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> re-binds without restart.
///
/// Registered as a singleton in DI. Thread-safe (each <see cref="StationSettingsRepository"/> call
/// opens its own connection per-operation).
///
/// gh-#406 slice 3: the raw <c>station.settings</c> row I/O lives in
/// <see cref="StationSettingsRepository"/> (<c>GenWave.MediaLibrary.Station</c>) now — this class
/// builds that repository internally from the same <see cref="connectionString"/> it always took
/// (no DI wiring change; <c>StationSettingsHostingExtensions</c> still constructs this type exactly
/// as before) and keeps only the concerns that are genuinely this store's own: the write-side
/// allowlist guard, the live-reload signal, the change event, and the read-side degrade posture
/// below.
/// </summary>
public sealed class StationSettingsStore : IStationSettingsStore
{
    readonly string connectionString;
    readonly StationSettingsRepository repository;
    readonly StationSettingsConfigurationSource source;
    readonly IStationEventSink events;
    readonly ILogger<StationSettingsStore> logger;

    public StationSettingsStore(
        string connectionString,
        StationSettingsConfigurationSource source,
        IStationEventSink? events = null,
        ILogger<StationSettingsStore>? logger = null)
    {
        this.connectionString = connectionString;
        repository = new StationSettingsRepository(connectionString);
        this.source = source;
        this.events = events ?? NoOpStationEventSink.Instance;
        this.logger = logger ?? NullLogger<StationSettingsStore>.Instance;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not on the station settings allowlist.", nameof(key));

        await repository.WriteAsync(key, value, cancellationToken);

        // Signal the provider; IOptionsMonitor listeners will see the new value.
        source.BuiltProvider?.Reload();

        // Key only, never the value (gitea-#246) — see SettingChanged's own doc.
        events.Publish(new SettingChanged(key));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Degrades to an empty result (every key reads as <c>source="default"</c> in
    /// <c>GET /api/settings</c>) rather than throwing when the station DB is unreachable or
    /// unconfigured — the settings page must still render with defaults while Postgres is briefly
    /// down, mirroring <see cref="StationSettingsConfigurationProvider.Load"/>'s identical
    /// degrade-to-empty-overlay behavior at boot. An empty <see cref="connectionString"/> throws
    /// <see cref="InvalidOperationException"/> before a <see cref="DbException"/> is even reachable
    /// (same guard the provider's <c>Load()</c> documents), so both cases are covered.
    ///
    /// Catches <see cref="DbException"/> — the provider-neutral ADO.NET base type
    /// <see cref="Npgsql.NpgsqlException"/> itself derives from — rather than
    /// <c>Npgsql.NpgsqlException</c> directly: this class carries no Npgsql reference at all now that
    /// <see cref="StationSettingsRepository"/> owns the Postgres specifics (gh-#406 slice 3), and
    /// <see cref="DbException"/> catches every failure the original catch did (and nothing broader —
    /// it still lets an <see cref="OperationCanceledException"/> from <paramref name="cancellationToken"/>
    /// propagate untouched, same as before).
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("No Station connection string; overlay reads as empty");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var rows = await repository.ReadAllAsync(cancellationToken);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in rows)
            {
                if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
                    continue;   // never surface a key that slipped through write-path guards

                result[key] = value;
            }

            return result;
        }
        catch (DbException ex)
        {
            // DB down, wrong password, no station schema yet — none of these should turn
            // GET /api/settings into a 500; the overlay is empty until the DB is reachable again.
            logger.LogWarning(ex, "Overlay read failed; treating as empty");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
