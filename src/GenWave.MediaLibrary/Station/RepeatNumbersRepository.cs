using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Postgres-backed <see cref="IRepeatNumbersReader"/> over <c>station.v_repeat_catches_7d</c> and
/// <c>station.v_catchphrase_per_shift</c> (SPEC F218.29, STORY-542; db/51).
/// </summary>
sealed class RepeatNumbersRepository(Lazy<NpgsqlDataSource> dataSource, ILogger<RepeatNumbersRepository> logger)
    : IRepeatNumbersReader
{
    /// <summary>Most over-used phrases returned: the panel is a glance, not a report.</summary>
    const int MaxOverused = 20;

    public async Task<RepeatNumbers?> ReadAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

            var djs = await conn.QueryAsync<DjRow>(new CommandDefinition(
                """
                select persona_id::bigint as persona_id, persona_name, catches, trims, fallbacks
                from station.v_repeat_catches_7d
                where catches > 0 or fallbacks > 0
                order by catches desc, fallbacks desc, persona_name
                """,
                cancellationToken: ct));

            var overused = await conn.QueryAsync<OveruseRow>(new CommandDefinition(
                """
                select persona_id::bigint as persona_id, persona_name, shift_key, phrase, max_per_shift, occurrences
                from station.v_catchphrase_per_shift
                where occurrences > max_per_shift and last_written_at >= now() - interval '7 days'
                order by last_written_at desc, persona_name, phrase
                limit @Take
                """,
                new { Take = MaxOverused },
                cancellationToken: ct));

            return new RepeatNumbers(
                djs.Select(r => new DjRepeatNumbers(r.PersonaId, r.PersonaName, r.Catches, r.Trims, r.Fallbacks)).ToList(),
                overused.Select(r => new CatchphraseOveruse(
                    r.PersonaId, r.PersonaName, r.ShiftKey, r.Phrase, r.MaxPerShift, r.Occurrences)).ToList());
        }
        // ArgumentException: no station connection string is configured, so the lazy data source cannot be built.
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException)
        {
            logger.LogWarning(ex, "Repeat numbers unavailable: the station database could not be reached");
            return null;
        }
    }

    sealed record DjRow(long PersonaId, string PersonaName, int Catches, int Trims, int Fallbacks);

    sealed record OveruseRow(long PersonaId, string PersonaName, string ShiftKey, string Phrase, int MaxPerShift, int Occurrences);
}
