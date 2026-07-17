using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
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

    public StationSettingsStore(
        string connectionString,
        StationSettingsConfigurationSource source,
        IStationEventSink? events = null)
    {
        this.connectionString = connectionString;
        this.source = source;
        this.events = events ?? NoOpStationEventSink.Instance;
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
    public async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        return result;
    }
}
