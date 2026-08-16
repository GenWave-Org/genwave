using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IIconPackStore"/> (SPEC F130, STORY-337, PLAN T290)
/// over <c>station.icon_pack</c>. Connection-per-query, mirrors <see cref="ThemeRepository"/>'s own
/// station-schema wiring almost exactly — a single jsonb-backed table, no child rows, no cross-table
/// transaction.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> — mirrors every other station-schema store
/// in this file's directory: merely resolving <see cref="IIconPackStore"/> from DI must never be
/// enough to trigger a connection attempt against an empty/dev-mode <c>ConnectionStrings:Station</c>.
/// </summary>
sealed class IconPackRepository(Lazy<NpgsqlDataSource> dataSource) : IIconPackStore
{
    const string SelectColumns =
        "select slug, definition::text as definition, imported_from, imported_at from station.icon_pack";

    /// <summary>
    /// Single-statement upsert: the real <c>UNIQUE(slug)</c> constraint is the ON CONFLICT target, not
    /// a pre-check — mirrors <see cref="ThemeRepository.UpsertAsync"/>'s own insert-or-update-in-one-
    /// round-trip shape. <c>imported_at</c> is stamped <c>now()</c> unconditionally on both branches —
    /// unlike <see cref="ThemeRepository.UpsertAsync"/>'s own null-<c>importedFrom</c> branch, an icon
    /// pack has no authored-in-place path (mirrors <see cref="FontPackRepository.UpsertAsync"/>'s own
    /// unconditional stamp).
    /// </summary>
    public async Task UpsertAsync(string slug, string definition, string importedFrom, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.icon_pack (slug, definition, imported_from, imported_at)
            values (@Slug, @Definition::jsonb, @ImportedFrom, now())
            on conflict (slug) do update
              set definition = @Definition::jsonb,
                  imported_from = @ImportedFrom,
                  imported_at = now()
            """,
            new { Slug = slug, Definition = definition, ImportedFrom = importedFrom },
            cancellationToken: ct));
    }

    public async Task<IconPack?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<IconPack>(new CommandDefinition(
            $"{SelectColumns} where slug = @slug",
            new { slug },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<IconPack>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<IconPack>(new CommandDefinition(SelectColumns, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// The settings-page hot path's own lighter-weight projection (SPEC F130.4, PLAN T303 review
    /// finding F2) — selects <c>slug</c> alone, never <c>definition::text</c>, so
    /// <c>Station:IconPack</c>'s live choices stop paying for every installed pack's full (up to 256
    /// KiB) definition on every settings <c>GET</c>/<c>PUT</c>.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAllSlugsAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "select slug from station.icon_pack", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> DeleteAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.icon_pack where slug = @slug",
            new { slug },
            cancellationToken: ct));
        return affected > 0;
    }
}
