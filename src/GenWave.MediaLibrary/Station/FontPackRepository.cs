using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IFontPackStore"/> (SPEC F104, STORY-282, PLAN T198)
/// over <c>station.font_pack</c>(+<c>_face</c>). Unlike <see cref="ThemeRepository"/>'s
/// connection-per-query single-table upsert, <see cref="UpsertAsync"/> spans two tables and needs
/// F104's "replace every face on re-install" guarantee, so it opens ONE connection and runs the
/// ENTIRE write (pack upsert-by-slug, then a delete-then-reinsert-all of that pack's faces) inside
/// ONE <see cref="NpgsqlTransaction"/> — the same shape <see cref="PersonaImportRepository"/> uses
/// for its own multi-table import. Every other method stays connection-per-query, mirroring
/// <see cref="ThemeRepository"/> exactly.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> — mirrors every other station-schema
/// store in this file's directory: merely resolving <see cref="IFontPackStore"/> from DI must never
/// be enough to trigger a connection attempt against an empty/dev-mode
/// <c>ConnectionStrings:Station</c>.
/// </summary>
sealed class FontPackRepository(Lazy<NpgsqlDataSource> dataSource) : IFontPackStore
{
    /// <summary>
    /// Single-transaction upsert (SPEC F104 "Data model"): the pack row's real
    /// <c>UNIQUE(slug)</c> constraint is the ON CONFLICT target, not a pre-check — mirrors
    /// <c>ThemeRepository.UpsertAsync</c>'s own insert-or-update-in-one-round-trip shape.
    /// <c>imported_at</c> is stamped <c>now()</c> unconditionally on both the insert and the update
    /// branch. Every existing face for the resolved pack id is deleted, then every
    /// <paramref name="faces"/> entry is inserted fresh — a re-install's face LIST becomes the
    /// pack's entire face set, never a merge with what was there before.
    /// </summary>
    public async Task UpsertAsync(
        string slug, string family, string definition, string importedFrom,
        IReadOnlyList<FontPackFaceInput> faces, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var packId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            insert into station.font_pack (slug, family, definition, imported_from, imported_at)
            values (@Slug, @Family, @Definition::jsonb, @ImportedFrom, now())
            on conflict (slug) do update
              set family = @Family,
                  definition = @Definition::jsonb,
                  imported_from = @ImportedFrom,
                  imported_at = now()
            returning id
            """,
            new { Slug = slug, Family = family, Definition = definition, ImportedFrom = importedFrom },
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.font_pack_face where pack_id = @PackId",
            new { PackId = packId },
            transaction: tx,
            cancellationToken: ct));

        foreach (var face in faces)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into station.font_pack_face (pack_id, file, style, bytes, byte_size, sha256)
                values (@PackId, @File, @Style, @Bytes, @ByteSize, @Sha256)
                """,
                new
                {
                    PackId = packId, face.File, face.Style, face.Bytes, face.ByteSize, face.Sha256,
                },
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Two queries (packs, then every face) rather than a SQL join — a pack typically owns 1-2 faces
    /// (SPEC F104), so grouping in memory via <see cref="Enumerable.ToLookup{TSource,TKey}"/> stays
    /// simpler than a join-and-split-on shape for no real cost at this scale.
    /// </summary>
    public async Task<IReadOnlyList<FontPack>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var packs = await conn.QueryAsync<PackRow>(new CommandDefinition(
            """
            select id, slug, family, definition::text as definition, imported_from, imported_at, created_at
            from station.font_pack
            """,
            cancellationToken: ct));

        var faces = await conn.QueryAsync<FaceRow>(new CommandDefinition(
            "select pack_id, file, style, byte_size, sha256 from station.font_pack_face",
            cancellationToken: ct));

        var facesByPack = faces.ToLookup(face => face.PackId);

        return packs
            .Select(pack => new FontPack(
                pack.Slug, pack.Family, pack.Definition, pack.ImportedFrom, pack.ImportedAt, pack.CreatedAt,
                facesByPack[pack.Id].Select(face => new FontPackFace(face.File, face.Style, face.ByteSize, face.Sha256)).ToList()))
            .ToList();
    }

    public async Task<FontPackFaceContent?> GetFaceByFileAsync(string file, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ContentRow>(new CommandDefinition(
            "select bytes, sha256 from station.font_pack_face where file = @file",
            new { file },
            cancellationToken: ct));

        return row is null ? null : new FontPackFaceContent(row.Bytes, row.Sha256);
    }

    /// <summary>Dapper deserialization shape for a <c>station.font_pack</c> row — carries <c>id</c>
    /// only to key <see cref="GetAllAsync"/>'s own face grouping; <see cref="FontPack"/> itself never
    /// exposes the surrogate key, mirroring <see cref="OwnerTheme"/>'s own slug-is-identity
    /// convention.</summary>
    sealed record PackRow(int Id, string Slug, string Family, string Definition, string ImportedFrom, DateTime ImportedAt, DateTime CreatedAt);

    /// <summary>Dapper deserialization shape for a <c>station.font_pack_face</c> row, keyed by
    /// <see cref="PackId"/> for <see cref="GetAllAsync"/>'s own grouping.</summary>
    sealed record FaceRow(int PackId, string File, string Style, int ByteSize, string Sha256);

    /// <summary>Dapper deserialization shape for <see cref="GetFaceByFileAsync"/>'s own narrow
    /// projection.</summary>
    sealed record ContentRow(byte[] Bytes, string Sha256);
}
