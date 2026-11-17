using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Postgres-backed <see cref="ICallbackRateReader"/> over <c>station.callback_rate</c> (SPEC F218.47,
/// STORY-551; db/52): sums every DJ's row so the station-wide rate is used over hours, not a mean of rates.
/// </summary>
sealed class CallbackRateRepository(Lazy<NpgsqlDataSource> dataSource, ILogger<CallbackRateRepository> logger)
    : ICallbackRateReader
{
    public async Task<CallbackRate?> ReadAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var row = await conn.QuerySingleAsync<TotalsRow>(new CommandDefinition(
                """
                select coalesce(sum(asks), 0)::integer as asks,
                       coalesce(sum(used), 0)::integer as used,
                       coalesce(sum(hours), 0)::integer as hours
                from station.callback_rate
                """,
                cancellationToken: ct));
            return new CallbackRate(row.Asks, row.Used, row.Hours);
        }
        // ArgumentException: no station connection string is configured, so the lazy data source cannot be built.
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException)
        {
            logger.LogWarning(ex, "Callback rate unavailable: the station database could not be reached");
            return null;
        }
    }

    sealed record TotalsRow(int Asks, int Used, int Hours);
}
