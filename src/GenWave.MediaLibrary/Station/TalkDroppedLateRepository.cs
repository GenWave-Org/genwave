using Dapper;
using GenWave.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Postgres-backed <see cref="ITalkDroppedLateReader"/> over <c>station.booth_log</c> (SPEC F219.26,
/// STORY-560). No new schema: the table is small and bounded by booth retention (default 14 days), which is
/// longer than the 7-day window it is asked for, so a sequential scan is as good as an index here.
/// </summary>
sealed class TalkDroppedLateRepository(Lazy<NpgsqlDataSource> dataSource, ILogger<TalkDroppedLateRepository> logger)
    : ITalkDroppedLateReader
{
    public async Task<int?> CountSinceAsync(DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(*)::integer from station.booth_log where kind = 'talk-dropped-late' and occurred_at >= @Since",
                new { Since = since },
                cancellationToken: ct));
        }
        // ArgumentException: no station connection string is configured, so the lazy data source cannot be built.
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException)
        {
            logger.LogWarning(ex, "Late-talk count unavailable: the station database could not be reached");
            return null;
        }
    }
}
