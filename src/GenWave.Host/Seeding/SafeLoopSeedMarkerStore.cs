using System.Text.Json;
using Npgsql;

namespace GenWave.Host.Seeding;

/// <summary>
/// <see cref="ISafeLoopSeedMarkerStore"/> backed directly by <c>station.settings</c> on the Station
/// connection — the same table <see cref="GenWave.Host.Configuration.StationSettingsStore"/>
/// writes to, but reached through a separate, narrower seam so the marker key can never be
/// allowlisted by accident (F27.10).
/// </summary>
public sealed class SafeLoopSeedMarkerStore(string connectionString) : ISafeLoopSeedMarkerStore
{
    /// <summary>
    /// The marker key. Lives outside the <c>Station:*</c> config namespace (so it can never collide
    /// with a bound options section) and is absent from
    /// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/> by construction — nothing
    /// on the <c>GET</c>/<c>PUT /api/settings</c> path ever references it.
    /// </summary>
    public const string Key = "Internal:BootSeed:SafeLoopCompletedAt";

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM station.settings WHERE key = @key";
        cmd.Parameters.AddWithValue("key", Key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct);
    }

    /// <inheritdoc/>
    public async Task MarkCompletedAsync(CancellationToken ct)
    {
        // The value carries a UTC timestamp for operator diagnosability (visible only via a direct
        // psql query — never through the settings API); its content is otherwise unused.
        var json = JsonSerializer.Serialize(DateTimeOffset.UtcNow);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO station.settings (key, value, updated_at)
            VALUES (@key, @value::jsonb, now())
            ON CONFLICT (key) DO UPDATE
              SET value      = EXCLUDED.value,
                  updated_at = EXCLUDED.updated_at
            """;
        cmd.Parameters.AddWithValue("key", Key);
        cmd.Parameters.AddWithValue("value", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
