using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IStationImageStore"/> (SPEC F131, STORY-339, PLAN T290,
/// gh-#15) over the single-row <c>station.station_image</c>. Connection-per-query, mirrors
/// <see cref="ThemeRepository"/>'s own station-schema wiring; every method operates on the one row at
/// <c>id = 1</c> (the table's own <c>CHECK (id = 1)</c> makes a second row structurally impossible), so
/// no method here ever takes an id parameter.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> — mirrors every other station-schema store
/// in this file's directory: merely resolving <see cref="IStationImageStore"/> from DI must never be
/// enough to trigger a connection attempt against an empty/dev-mode <c>ConnectionStrings:Station</c>.
/// </summary>
sealed class StationImageRepository(Lazy<NpgsqlDataSource> dataSource) : IStationImageStore
{
    public async Task<StationImage?> GetAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<StationImage>(new CommandDefinition(
            "select bytes, byte_size, sha256, token, updated_at from station.station_image where id = 1",
            cancellationToken: ct));
    }

    /// <summary>
    /// Single-statement upsert against the fixed <c>id = 1</c> row: no row yet inserts one, an
    /// existing row replaces every column — including <paramref name="token"/>, which the CALLER has
    /// already rotated before this method ever runs (mirrors
    /// <see cref="PersonaAvatarRepository.UpsertAsync"/>'s own "the store is dumb about rotation
    /// policy" discipline). <c>byte_size</c> is derived from <paramref name="bytes"/>.<c>Length</c> —
    /// this table has no dedicated input record to carry that invariant the way
    /// <see cref="AvatarPackItemInput"/> does, so the derivation happens here instead.
    /// <c>updated_at</c> is always the write's own <c>now()</c>.
    /// </summary>
    public async Task UpsertAsync(byte[] bytes, string sha256, string token, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.station_image (id, bytes, byte_size, sha256, token, updated_at)
            values (1, @Bytes, @ByteSize, @Sha256, @Token, now())
            on conflict (id) do update
              set bytes = @Bytes,
                  byte_size = @ByteSize,
                  sha256 = @Sha256,
                  token = @Token,
                  updated_at = now()
            """,
            new { Bytes = bytes, ByteSize = bytes.Length, Sha256 = sha256, Token = token },
            cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.station_image where id = 1",
            cancellationToken: ct));
        return affected > 0;
    }
}
