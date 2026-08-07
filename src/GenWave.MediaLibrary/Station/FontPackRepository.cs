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

    /// <summary>
    /// The ONE guard predicate — "does some <c>station.theme</c> row's raw text quote this face's own
    /// <c>/fonts/&lt;file&gt;</c> src" — shared verbatim by <see cref="DeleteAsync"/>'s own atomic guard
    /// clause AND its follow-up naming query (review finding N4): one string, not two hand-copied SQL
    /// fragments a future edit to either could silently leave the other behind on. <c>fpf</c>/<c>th</c>
    /// are the fixed aliases both call sites bind to <c>station.font_pack_face</c>/<c>station.theme</c>
    /// respectively — this predicate is only ever spliced into a query that already carries both.
    /// </summary>
    const string ReferencesFacePredicate = "strpos(th.definition::text, '\"/fonts/' || fpf.file || '\"') > 0";

    /// <summary>
    /// SPEC F104.14, STORY-288, PLAN T208 — see <see cref="IFontPackStore.DeleteAsync"/>'s own remarks
    /// for the "delete IS the guard" contract this fulfils. The DELETE below is ONE statement: its own
    /// <c>not exists (...)</c> clause is evaluated as part of the SAME statement that removes the row —
    /// no separate ROUND TRIP between "is it referenced" and "remove it" for a concurrent
    /// <c>station.theme</c> write to land inside the way two independent app-level calls would invite
    /// (the FK-guard precedent's own real constraint applied where there is no literal FK to lean on: a
    /// reference lives inside <c>station.theme.definition</c>'s opaque jsonb, not a foreign key column).
    /// <b>This is NOT full serializable isolation</b> — see <see cref="IFontPackStore.DeleteAsync"/>'s
    /// own remarks for the honest READ COMMITTED boundary (a theme referencing this pack CAN still
    /// commit in the narrow window between this statement's own snapshot and its commit) and the
    /// fail-soft outcome when it does.
    ///
    /// <para>
    /// <b>A TEXT substring search, deliberately, not a structural jsonb path query.</b>
    /// <c>GenWave.MediaLibrary</c> (this project) has no dependency on
    /// <c>GenWave.Host.Theming.ThemeManifest</c> and must never gain one just to answer "does this JSON
    /// blob mention this filename" — the same "opaque jsonb, a caller deserializes at its own edge"
    /// discipline <see cref="IThemeStore"/>'s own remarks already establish for a WRITE, extended here to
    /// a QUERY. Every font asset src this app ever writes is the fixed shape
    /// <c>ThemeManifestParser.FontSrcPattern</c> pins at the OTHER end of this seam
    /// (<c>/fonts/&lt;file&gt;.woff2</c>, nothing that needs escaping inside a JSON string), and
    /// <c>ThemeManifestSerializer</c> writes unindented JSON (STORY-269 AC5) — so a quoted
    /// <c>"/fonts/&lt;file&gt;"</c> literal always appears verbatim in <c>definition::text</c> wherever a
    /// theme actually references that face, regardless of whatever OTHER keys/whitespace surround it.
    /// <c>strpos</c>, not <c>like</c>: a plain substring search needs no wildcard-character escaping of
    /// a face's own file name (<c>like</c>'s <c>%</c>/<c>_</c> would otherwise need escaping) — this
    /// sidesteps that whole bug class rather than getting the escaping right by convention.
    /// </para>
    ///
    /// <para>
    /// <b>False POSITIVES are possible too (review finding N3) — fail-closed, and acceptable.</b> This
    /// is a substring search over the WHOLE serialized document, not a scoped read of the font-asset
    /// fields alone: a theme whose <c>name</c>/<c>author</c> (or any other string field) happens to
    /// literally contain the quoted text <c>"/fonts/&lt;file&gt;"</c> — e.g. an unusual theme name that
    /// quotes a filename in prose — would be (wrongly) counted as a referencing theme, blocking an
    /// uninstall that a structural read would have allowed. This never loses data or corrupts state: the
    /// worst case is an operator editing an unrelated theme's text field to clear a spurious block, and
    /// the 409 still names the (wrongly) blocking theme by slug, so it is discoverable rather than a
    /// silent, unexplained refusal. Fail-closed over fail-open — a false positive merely delays an
    /// uninstall; a false negative would silently break a theme's own face mid-air.
    /// </para>
    /// </summary>
    public async Task<FontPackDeleteResult> DeleteAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var deletedId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            $"""
            delete from station.font_pack fp
            where fp.slug = @slug
              and not exists (
                select 1
                from station.font_pack_face fpf
                join station.theme th
                  on {ReferencesFacePredicate}
                where fpf.pack_id = fp.id
              )
            returning fp.id
            """,
            new { slug },
            cancellationToken: ct));

        if (deletedId is not null)
            return new FontPackDeleteResult.Deleted();

        // Either no such pack exists, or it does but the DELETE above refused it (referenced) — the
        // affected-row-count alone cannot distinguish the two, so both are re-queried here.
        var packId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "select id from station.font_pack where slug = @slug",
            new { slug },
            cancellationToken: ct));

        if (packId is null)
            return new FontPackDeleteResult.NotFound();

        // Re-run AFTER the fact, purely to NAME the offenders for the 409 body — the DELETE's own
        // refusal above is already correct regardless of what this finds (mirrors
        // PersonaWriteResult.ScheduledElsewhere's own race-backstop "may be empty" remarks: every
        // referencing theme could, in a narrow window, have been edited/removed between the refusal and
        // this query). `collate "C"` (review finding N5) makes the byte-ordinal order
        // FontPackDeleteResult.Referenced's own remarks promise ACTUAL, not merely assumed — Postgres's
        // default collation is whatever the cluster/database was initialised with (locale-dependent,
        // not necessarily byte-ordinal), so without this the order could drift by deployment.
        // The ORDER BY expression must appear verbatim in the SELECT DISTINCT list (Postgres 42P10), so
        // `collate "C"` is applied to the projected column itself (aliased back to `slug`), not bolted
        // onto a separate ORDER BY clause referencing the unqualified column.
        var referencingSlugs = await conn.QueryAsync<string>(new CommandDefinition(
            $"""
            select distinct th.slug collate "C" as slug
            from station.theme th
            join station.font_pack_face fpf
              on {ReferencesFacePredicate}
            where fpf.pack_id = @packId
            order by slug
            """,
            new { packId },
            cancellationToken: ct));

        return new FontPackDeleteResult.Referenced(referencingSlugs.ToList());
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
