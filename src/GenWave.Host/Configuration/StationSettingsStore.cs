using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.Host.Configuration;

/// <summary>
/// Writes allowlisted settings to <c>station.settings</c> and signals the
/// <see cref="StationSettingsConfigurationProvider"/> to reload so
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> re-binds without restart.
///
/// Registered as a singleton in DI. Thread-safe (Npgsql connections are created per-operation).
/// </summary>
public sealed class StationSettingsStore : IStationSettingsStore
{
    readonly string connectionString;
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
        this.source = source;
        this.events = events ?? NoOpStationEventSink.Instance;
        this.logger = logger ?? NullLogger<StationSettingsStore>.Instance;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not on the station settings allowlist.", nameof(key));

        var json = JsonSerializer.Serialize(value);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO station.settings (key, value, updated_at)
            VALUES (@key, @value::jsonb, now())
            ON CONFLICT (key) DO UPDATE
              SET value      = EXCLUDED.value,
                  updated_at = EXCLUDED.updated_at
            """;
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

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
    /// <see cref="InvalidOperationException"/> before a <see cref="NpgsqlException"/> is even
    /// reachable (same guard the provider's <c>Load()</c> documents), so both cases are covered.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("No Station connection string; overlay reads as empty");
            return result;
        }

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM station.settings";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = reader.GetString(0);
                if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
                    continue;   // never surface a key that slipped through write-path guards

                result[key] = reader.GetString(1);
            }
        }
        catch (NpgsqlException ex)
        {
            // DB down, wrong password, no station schema yet — none of these should turn
            // GET /api/settings into a 500; the overlay is empty until the DB is reachable again.
            logger.LogWarning(ex, "Overlay read failed; treating as empty");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }
}
