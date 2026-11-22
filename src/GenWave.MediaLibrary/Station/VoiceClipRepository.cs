using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process <see cref="IVoiceClipStore"/> over <c>station.voice_clip</c> (SPEC F219.12, PLAN T756). Connection-per-query,
/// the <see cref="VoicePackRepository"/> shape. <paramref name="dataSource"/> is lazy so resolving the store never connects.
/// Columns are aliased to the record's property names, so no underscore mapping is relied on.
/// </summary>
sealed class VoiceClipRepository(Lazy<NpgsqlDataSource> dataSource) : IVoiceClipStore
{
    // Postgres SQLSTATE for unique_violation.
    const string UniqueViolation = "23505";

    const string Columns = """
        id::bigint as "Id", slug as "Slug", label as "Label", file_name as "FileName", duration_ms as "DurationMs",
        source as "Source", credit as "Credit", created_at as "CreatedAt"
        """;

    public async Task<IReadOnlyList<VoiceClip>> ListAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<VoiceClip>(new CommandDefinition(
            $"select {Columns} from station.voice_clip order by created_at, slug", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<VoiceClip?> InsertAsync(NewVoiceClip clip, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        try
        {
            return await conn.QuerySingleAsync<VoiceClip>(new CommandDefinition(
                $"""
                insert into station.voice_clip (slug, label, file_name, duration_ms, source, credit)
                values (@Slug, @Label, @FileName, @DurationMs, @Source, @Credit)
                returning {Columns}
                """,
                clip, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.voice_clip where slug = @slug", new { slug }, cancellationToken: ct));
        return affected > 0;
    }
}
