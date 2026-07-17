using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Queries <c>library.library</c> for display names and media counts.
/// Uses the same <see cref="NpgsqlDataSource"/> as <see cref="MediaRepository"/> (library_svc role).
/// </summary>
sealed class LibraryRepository(NpgsqlDataSource dataSource) : ILibraryRepository
{
    public async Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long Id, string Name)>(new CommandDefinition(
            "select id, name from library.library where id = any(@ids)",
            new { ids = ids.ToArray() },
            cancellationToken: ct));

        return rows.Select(r => new LibraryInfo(r.Id, r.Name)).ToList();
    }

    public async Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long Id, string Name, int MediaCount)>(new CommandDefinition(
            """
            select l.id, l.name, coalesce(cast(count(m.id) as int), 0) as media_count
            from library.library l
            left join library.media m on m.library_id = l.id
            group by l.id, l.name
            order by l.id
            """,
            cancellationToken: ct));

        return rows.Select(r => new LibraryAdminInfo(r.Id, r.Name, r.MediaCount)).ToList();
    }
}
