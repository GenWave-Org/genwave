using Dapper;
using GenWave.Core.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="ISchemaJournal"/> (SPEC F211.4, STORY-484, PLAN T592)
/// over <c>station.schema_migration</c> (SPEC F211.3). Connection-per-query, mirrors
/// <see cref="ThemeRepository"/>'s own station-schema wiring one single-table reader over.
/// </summary>
sealed class SchemaJournalRepository(Lazy<NpgsqlDataSource> dataSource) : ISchemaJournal
{
    // Well-known Postgres SQLSTATE; no Npgsql.PostgresErrorCodes dependency (house convention —
    // see AdminLibraryRepository's own remarks).
    const string UndefinedTable = "42P01";

    /// <summary>
    /// Parse rule (the one place it lives on the SQL side — the architecture fact
    /// <c>FeatureSchemaExpectedPin</c> applies the identical "leading run of ASCII digits" rule to
    /// <c>db/NN-*-migration.sh</c> file names in C#): the <c>where</c> clause excludes any
    /// <c>script</c> that doesn't start with a digit at all (an unparseable row is ignored, never a
    /// query error), and <c>substring(... from '^[0-9]+')</c> extracts only the LEADING run of digits
    /// — <c>"48z-..."</c> reads as 48, never as a parse failure. <c>max()</c> compares the extracted
    /// runs as <c>int</c>, not text, so 100 outranks 99 (a lexical <c>max(script)</c> would not).
    /// <c>max()</c> over zero qualifying rows returns SQL <c>NULL</c>, which Dapper's
    /// <see cref="SqlMapper.ExecuteScalarAsync{T}(System.Data.IDbConnection, CommandDefinition)"/> maps
    /// straight to <see langword="null"/> for <c>int?</c> — an empty journal needs no special case here.
    /// </summary>
    const string HighestAppliedSql =
        """
        select max(substring(script from '^[0-9]+')::int)
        from station.schema_migration
        where script ~ '^[0-9]+'
        """;

    public async Task<int?> GetAppliedAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(HighestAppliedSql, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedTable)
        {
            // A pre-F211 box that never ran the new migrate.sh has no station.schema_migration table at
            // all (a raw `docker compose up` gets it from db/06's fresh-init mirror and reads as an empty
            // journal instead) — reads as "nothing applied" rather than throwing, the same as empty.
            return null;
        }
    }
}
