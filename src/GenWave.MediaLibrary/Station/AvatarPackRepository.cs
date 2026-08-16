using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IAvatarPackStore"/> (SPEC F128, STORY-332, PLAN T290)
/// over <c>station.avatar_pack</c>(+<c>_item</c>). Unlike <see cref="ThemeRepository"/>'s
/// connection-per-query single-table upsert, <see cref="UpsertAsync"/> spans two tables and needs the
/// "replace every item on re-install" guarantee, so it opens ONE connection and runs the ENTIRE write
/// (pack upsert-by-slug, then a delete-then-reinsert-all of that pack's items) inside ONE
/// <see cref="NpgsqlTransaction"/> — the same shape <see cref="FontPackRepository.UpsertAsync"/> uses
/// for its own multi-table install. Every other method stays connection-per-query, mirroring
/// <see cref="FontPackRepository"/> exactly. Unlike that class, <c>(pack_id, name)</c> is UNIQUE only
/// WITHIN a pack (never globally, the way <c>font_pack_face.file</c> is), so no post-failure
/// collision-resolution path is needed here.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> — mirrors every other station-schema store
/// in this file's directory: merely resolving <see cref="IAvatarPackStore"/> from DI must never be
/// enough to trigger a connection attempt against an empty/dev-mode <c>ConnectionStrings:Station</c>.
/// </summary>
sealed class AvatarPackRepository(Lazy<NpgsqlDataSource> dataSource) : IAvatarPackStore
{
    /// <summary>
    /// Single-transaction upsert (mirrors <see cref="FontPackRepository.UpsertAsync"/>'s own shape):
    /// the pack row's real <c>UNIQUE(slug)</c> constraint is the ON CONFLICT target, not a pre-check.
    /// <c>imported_at</c> is stamped <c>now()</c> unconditionally on both the insert and the update
    /// branch. Every existing item for the resolved pack id is deleted, then every
    /// <paramref name="items"/> entry is inserted fresh — a re-install's item LIST becomes the pack's
    /// entire item set, never a merge with what was there before.
    /// </summary>
    public async Task UpsertAsync(
        string slug, string definition, string importedFrom,
        IReadOnlyList<AvatarPackItemInput> items, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var packId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            insert into station.avatar_pack (slug, definition, imported_from, imported_at)
            values (@Slug, @Definition::jsonb, @ImportedFrom, now())
            on conflict (slug) do update
              set definition = @Definition::jsonb,
                  imported_from = @ImportedFrom,
                  imported_at = now()
            returning id
            """,
            new { Slug = slug, Definition = definition, ImportedFrom = importedFrom },
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.avatar_pack_item where pack_id = @PackId",
            new { PackId = packId },
            transaction: tx,
            cancellationToken: ct));

        foreach (var item in items)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into station.avatar_pack_item (pack_id, name, suggested_persona, bytes, byte_size, sha256)
                values (@PackId, @Name, @SuggestedPersona, @Bytes, @ByteSize, @Sha256)
                """,
                new
                {
                    PackId = packId, item.Name, item.SuggestedPersona, item.Bytes, item.ByteSize, item.Sha256,
                },
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>Pack + every one of its items, WITH bytes (see <see cref="AvatarPack.Items"/>'s own
    /// remarks) — two queries rather than a join, mirroring
    /// <see cref="FontPackRepository.GetAllAsync"/>'s own reasoning: a pack typically owns a handful of
    /// items, so an in-memory join stays simpler than a join-and-split-on shape for no real cost at
    /// this scale.</summary>
    public async Task<AvatarPack?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var pack = await conn.QuerySingleOrDefaultAsync<PackRow>(new CommandDefinition(
            """
            select id, slug, definition::text as definition, imported_from, imported_at
            from station.avatar_pack
            where slug = @slug
            """,
            new { slug },
            cancellationToken: ct));

        if (pack is null) return null;

        var items = await conn.QueryAsync<AvatarPackItem>(new CommandDefinition(
            "select name, suggested_persona, bytes, byte_size, sha256 from station.avatar_pack_item where pack_id = @packId",
            new { packId = pack.Id },
            cancellationToken: ct));

        return new AvatarPack(pack.Slug, pack.Definition, pack.ImportedFrom, pack.ImportedAt, items.ToList());
    }

    /// <summary>Every installed pack, each with an EMPTY <see cref="AvatarPack.Items"/> list — see that
    /// property's own remarks for why this listing read deliberately excludes per-item bytes (a single
    /// query against <c>station.avatar_pack</c> alone, never joining <c>_item</c>).</summary>
    public async Task<IReadOnlyList<AvatarPack>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var packs = await conn.QueryAsync<PackRow>(new CommandDefinition(
            "select id, slug, definition::text as definition, imported_from, imported_at from station.avatar_pack",
            cancellationToken: ct));

        return packs
            .Select(pack => new AvatarPack(pack.Slug, pack.Definition, pack.ImportedFrom, pack.ImportedAt, []))
            .ToList();
    }

    /// <summary>Removes the pack and, by <c>ON DELETE CASCADE</c>, every one of its items — no
    /// referenced-by guard (see <see cref="IAvatarPackStore"/>'s own remarks: a worn face is a copy,
    /// never a live reference into this table).</summary>
    public async Task<bool> DeleteAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.avatar_pack where slug = @slug",
            new { slug },
            cancellationToken: ct));
        return affected > 0;
    }

    /// <summary>Dapper deserialization shape for a <c>station.avatar_pack</c> row — carries <c>id</c>
    /// only to key <see cref="GetBySlugAsync"/>'s own item lookup; <see cref="AvatarPack"/> itself
    /// never exposes the surrogate key, mirroring <see cref="OwnerTheme"/>/<see cref="FontPack"/>'s own
    /// slug-is-identity convention.</summary>
    sealed record PackRow(int Id, string Slug, string Definition, string ImportedFrom, DateTime ImportedAt);
}
