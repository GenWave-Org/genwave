using System.Text.Json;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The <c>station.settings</c> key-value row I/O (gh-#406 slice 3): <c>WriteAsync</c>/
/// <c>ReadAllAsync</c> moved here byte-identical from
/// <c>GenWave.Host.Configuration.StationSettingsStore</c>, which now delegates to this class instead
/// of opening <see cref="NpgsqlConnection"/>s itself (STORY-042's original write side).
///
/// <paramref name="connectionString"/> arrives as a PLAIN string, not the
/// <see cref="Lazy{T}"/>&lt;<see cref="NpgsqlDataSource"/>&gt; every sibling repository in this
/// namespace (<see cref="PersonaRepository"/>, <see cref="RequestRepository"/>, <see cref="ShowRepository"/>,
/// ...) is built from — a deliberate deviation, not an oversight. gh-#406's remaining slices (4:
/// <c>SafeLoopSeedMarkerStore</c>, 5: <c>StationSettingsConfigurationProvider</c>) both need this
/// class constructible OUTSIDE the DI container, on the pre-DI boot path — most pointedly,
/// <c>StationSettingsConfigurationProvider.Load()</c> runs as part of building
/// <see cref="Microsoft.Extensions.Configuration.IConfigurationBuilder"/> itself, before
/// <c>WebApplicationBuilder.Build()</c> ever creates a container capable of constructing (let alone
/// injecting) a built <see cref="NpgsqlDataSource"/>. A plain connection string needs no container
/// and no builder step to exist, so slices 4/5 can construct this repository directly from the raw
/// <c>ConnectionStrings:Station</c> value the same way <c>StationSettingsStore</c> already does today.
///
/// Connection-per-call against a short-lived <see cref="NpgsqlConnection"/>, exactly as the code
/// this class was extracted from — no data-source pooling, matching the original's "thread-safe,
/// Npgsql connections are created per-operation" contract verbatim.
///
/// <see cref="ReadAllAsync"/> returns EVERY row, unfiltered: the settings allowlist
/// (<c>GenWave.Host.Configuration.StationSettingsAllowlist</c>) is a Host-only concern this project
/// has no reference to and must not gain one (L2/L1 confinement) — filtering by allowlist stays the
/// caller's job, same as it always has been for the write-side allowlist check.
/// </summary>
public sealed class StationSettingsRepository(string connectionString)
{
    /// <summary>
    /// Upserts <paramref name="value"/> (JSON-serialized) under <paramref name="key"/>. No allowlist
    /// check here — that guard lives at the caller (<c>StationSettingsStore.WriteAsync</c>), the same
    /// separation every other MediaLibrary repository keeps from its own callers' business rules.
    /// </summary>
    public async Task WriteAsync(string key, object value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO station.settings (key, value, updated_at)
            VALUES (@key, @value::jsonb, now())
            ON CONFLICT (key) DO UPDATE
              SET value      = EXCLUDED.value,
                  version    = station.settings.version + 1,
                  updated_at = EXCLUDED.updated_at
            """;
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Optimistic-concurrency write (gh-#486): persists <paramref name="value"/> only if the row's
    /// current <c>version</c> still matches <paramref name="expectedVersion"/> — the guard against a
    /// whole-array/document write silently clobbering a concurrent editor's save (probed at T144:
    /// DELETE || PUT both 2xx, one edit vanished). No allowlist check here, same split
    /// <see cref="WriteAsync"/> already keeps from its caller.
    ///
    /// <paramref name="expectedVersion"/> of <c>0</c> means "no row existed at the caller's read" —
    /// a plain conditional INSERT (<c>ON CONFLICT DO NOTHING</c>), never a version comparison, since
    /// there is no row yet to compare against; a real row's <c>version</c> starts at 1 and only ever
    /// grows, so it can never legitimately equal 0 and collide with this sentinel. Any other value
    /// runs a conditional UPDATE gated on <c>version = @expectedVersion</c>.
    ///
    /// Returns the row's new version on success, or <see langword="null"/> when the guard failed —
    /// either no row existed and one raced into existence first (the 0 branch), or an existing row's
    /// version had already moved (the UPDATE branch matched zero rows). The caller decides how a
    /// <see langword="null"/> surfaces (a 409, in every Host caller today).
    /// </summary>
    public async Task<long?> WriteIfVersionMatchesAsync(
        string key, object value, long expectedVersion, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        if (expectedVersion == 0)
        {
            cmd.CommandText =
                """
                INSERT INTO station.settings (key, value, version, updated_at)
                VALUES (@key, @value::jsonb, 1, now())
                ON CONFLICT (key) DO NOTHING
                RETURNING version
                """;
        }
        else
        {
            cmd.CommandText =
                """
                UPDATE station.settings
                   SET value      = @value::jsonb,
                       version    = version + 1,
                       updated_at = now()
                 WHERE key = @key
                   AND version = @expectedVersion
                RETURNING version
                """;
            cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        }
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", json);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long newVersion ? newVersion : null;
    }

    /// <summary>
    /// Every row's current <c>version</c> (gh-#486), keyed by <c>key</c> — the read half of the
    /// optimistic-concurrency guard <see cref="WriteIfVersionMatchesAsync"/> writes. A key absent
    /// from the result has no row yet; the caller's own "no row" sentinel (0, matching
    /// <see cref="WriteIfVersionMatchesAsync"/>'s own expectedVersion=0 branch) applies, not
    /// anything this repository decides. Any failure (including a <see cref="Npgsql.NpgsqlException"/>)
    /// propagates to the caller — same posture as <see cref="ReadAllAsync"/>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, long>> ReadVersionsAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, version FROM station.settings";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetInt64(1);

        return result;
    }

    /// <summary>
    /// Every row in <c>station.settings</c>, keyed by <c>key</c>, value verbatim as the stored JSONB
    /// text. Lets any failure (including a <see cref="Npgsql.NpgsqlException"/>) propagate to the
    /// caller — the degrade-to-empty-on-DB-down posture is a caller policy
    /// (<c>StationSettingsStore.ReadAllAsync</c>), not something this repository decides on its own.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM station.settings";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetString(1);

        return result;
    }

    /// <summary>
    /// True if a row for <paramref name="key"/> exists in <c>station.settings</c> — added for gh-#406
    /// slice 4: <c>GenWave.Host.Seeding.SafeLoopSeedMarkerStore</c>'s one-shot boot-seed marker check
    /// (F27.10) needs a single-key existence probe, not the full unfiltered <see cref="ReadAllAsync"/>
    /// scan. Any failure (including a <see cref="Npgsql.NpgsqlException"/>) propagates to the caller —
    /// same posture as <see cref="ReadAllAsync"/>, degrade policy is a caller concern, not this
    /// repository's.
    /// </summary>
    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM station.settings WHERE key = @key";
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct);
    }

    /// <summary>
    /// SYNCHRONOUS read of every row in <c>station.settings</c> — the one deliberate sync exception
    /// in this otherwise async-only repository, added for gh-#406 slice 5.
    /// <c>GenWave.Host.Configuration.StationSettingsConfigurationProvider.Load()</c> implements
    /// <see cref="Microsoft.Extensions.Configuration.IConfigurationProvider.Load"/>, a synchronous
    /// contract member the configuration system calls while
    /// <see cref="Microsoft.Extensions.Configuration.IConfigurationBuilder"/> itself is still being
    /// built — the same pre-DI boot path this class's plain-connection-string ctor exists for — and
    /// has no async entry point available there to await <see cref="ReadAllAsync"/> from. SQL is
    /// byte-identical to <see cref="ReadAllAsync"/>'s; only the sync/async shape differs. Any failure
    /// (including a <see cref="Npgsql.NpgsqlException"/>) propagates to the caller — same posture as
    /// <see cref="ReadAllAsync"/>/<see cref="ExistsAsync"/>, degrade policy is a caller concern, not
    /// this repository's.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReadAllForBoot()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM station.settings";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);

        return result;
    }
}
