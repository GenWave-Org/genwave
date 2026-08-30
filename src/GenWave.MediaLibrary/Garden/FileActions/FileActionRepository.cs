using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The Library Gardener's file-action audit + the ONE xmin-guarded row update a confirmed action
/// writes (SPEC F154.6, F154.7; STORY-379; PLAN T380, gh-#529) — every SQL statement behind
/// <c>FileActionExecutor</c> lives here (L2: only <c>*Repository</c> types inside
/// <c>GenWave.MediaLibrary.Garden</c> may touch Npgsql/Dapper), connection-per-call against the
/// library's own <see cref="NpgsqlDataSource"/>, the same shape <c>RotFindingRepository</c>/
/// <c>MediaThumbRepository</c> already establish one seam over.
///
/// Also implements <see cref="IFileActionSubjectReader"/> (PLAN T381) — the ONE read the dry-run
/// endpoint needs, exposed to <c>GenWave.Host</c> through that public port rather than this
/// (internal) type itself, the same <c>IAdminMediaLookup</c>/<c>MediaRepository</c> shape.
/// </summary>
sealed class FileActionRepository(NpgsqlDataSource dataSource, ILogger<FileActionRepository> logger)
    : IFileActionSubjectReader
{
    /// <summary>
    /// Re-reads <paramref name="mediaId"/>'s current <c>(xmin, path)</c> — the executor's own TOCTOU
    /// re-check (SPEC F154.5, STORY-379 AC7's executor half) against
    /// <c>Core.Domain.PlanBinding.Matches</c>. <see langword="null"/> when the row no longer exists —
    /// a binding can never match a row that is gone, so the caller treats this exactly like a
    /// mismatch.
    /// </summary>
    public async Task<FileActionBindingRow?> ReadBindingAsync(long mediaId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<FileActionBindingRow>(new CommandDefinition(
            "select xmin::text as xmin, path from library.media where id = @mediaId",
            new { mediaId }, cancellationToken: ct));
    }

    /// <summary>
    /// Reads <paramref name="mediaId"/>'s current catalog snapshot — the dry-run endpoint's own
    /// <see cref="FileActionSubject"/> source (SPEC F154.1, F154.3; STORY-379; PLAN T381, gh-#529).
    /// This repository performs no file I/O of its own (T381 review N4 moved the file's own tag read
    /// into <see cref="IFileActionPlanner"/> itself, via <see cref="IFileTagReader"/>, AFTER the
    /// subject's own destination gate — never here). <see langword="null"/> when no row exists with
    /// this id — the caller's own 404 decision, never echoed here.
    /// </summary>
    public async Task<FileActionSubject?> ReadSubjectAsync(long mediaId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<FileActionSubjectRow>(new CommandDefinition(
            """
            select xmin::text as xmin, path, library_id, artist, title, album, year, genre
            from library.media
            where id = @mediaId
            """,
            new { mediaId }, cancellationToken: ct));

        return row is null
            ? null
            : new FileActionSubject(
                mediaId, row.Xmin, row.Path, row.LibraryId, row.Artist, row.Title, row.Album, row.Year, row.Genre);
    }

    /// <summary>
    /// The one transaction a successful file action writes (SPEC F154.6, F154.7): an xmin-guarded
    /// <c>update</c> of <c>path</c>/<c>size_bytes</c>/<c>mtime</c> — <c>state</c> untouched on
    /// purpose, unlike <c>Catalog.MediaRepository.MarkDiscoveredAsync</c>'s own changed-file write,
    /// which flips <c>state = 'discovered'</c> and would re-enrich a row this action never actually
    /// changed the AUDIO of — followed by the audit insert, both inside ONE
    /// <see cref="NpgsqlTransaction"/>. Returns the update's own affected-row count: 0 means the xmin
    /// guard closed the TOCTOU gap between the executor's own binding re-read and now (someone wrote
    /// the row in between) — the caller treats this exactly like any other DB failure and reverts the
    /// filesystem op. A genuine database exception (e.g. STORY-379 AC13's trigger-raised revert
    /// fixture) is caught here, logged with the media id only, and ALSO reported as 0 — the caller
    /// never needs to know Npgsql exists (L2's own boundary: only a <c>*Repository</c> type may).
    /// </summary>
    public async Task<int> RelocateAsync(
        long mediaId, string xmin, string path, long sizeBytes, DateTime mtime,
        FileActionAuditEntry audit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                """
                update library.media
                set path = @path, size_bytes = @sizeBytes, mtime = @mtime
                where id = @mediaId and xmin::text = @xmin
                """,
                new { mediaId, xmin, path, sizeBytes, mtime },
                transaction: tx, cancellationToken: ct));

            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                return 0;
            }

            await conn.ExecuteAsync(BuildAuditCommand(audit, tx, ct));
            await tx.CommitAsync(ct);
            return affected;
        }
        catch (NpgsqlException)
        {
            // Never the exception message here (STORY-379's own "no path in any log/exception
            // message" law) — a trigger's RAISE EXCEPTION text is caller-controlled in the AC13
            // fixture and, more generally, an untrusted string this class must not assume is safe to
            // render. await using var tx above rolls back on disposal since it was never committed.
            logger.LogWarning("RelocateAsync failed for media {MediaId}", mediaId);
            return 0;
        }
    }

    /// <summary>
    /// Writes one <c>library.file_action</c> row on its own connection/transaction (SPEC F154.7) —
    /// every attempt that does NOT go through <see cref="RelocateAsync"/>'s own combined write
    /// (busy/conflict/refused/failed outcomes, and a revert's own <c>reverted</c> row, which must
    /// land in a NEW transaction since the one <see cref="RelocateAsync"/> just rolled back).
    /// Best-effort (T380 review N8's own audit-insert-failure fact): a genuine database exception is
    /// caught here, logged with the media id only, and swallowed — the audit trail is observability
    /// layered on top of the file action's own correctness, never load-bearing for it. This is what
    /// keeps a REVERT that itself cannot be audited (the same failure that forced the revert may
    /// still be blocking every insert into this table) from turning an already-correctly-reverted
    /// filesystem state into a reported failure — <c>FileActionExecutor</c> never needs to know
    /// Npgsql exists (L2's own boundary) or branch on whether the audit row actually landed.
    ///
    /// <para>
    /// <b>T380 review R2-1:</b> <paramref name="ct"/> also gets caught here, separately from
    /// <see cref="NpgsqlException"/> — <c>NpgsqlDataSource.OpenConnectionAsync</c> throws
    /// <see cref="OperationCanceledException"/> (a <see cref="TaskCanceledException"/>, specifically),
    /// NOT an <see cref="NpgsqlException"/>, when the token handed to it is already cancelled. The
    /// caller's own post-commit token is meant to be independent of the ORIGINAL caller's
    /// cancellation by construction; this catch is defence in depth for the case where the
    /// post-commit budget itself simply runs out mid-sequence — an audit write must never let that
    /// escape as an unhandled exception either.
    /// </para>
    /// </summary>
    public async Task AuditAsync(FileActionAuditEntry audit, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await conn.ExecuteAsync(BuildAuditCommand(audit, transaction: null, ct));
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("AuditAsync failed for media {MediaId}", audit.MediaId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("AuditAsync cancelled for media {MediaId}", audit.MediaId);
        }
    }

    /// <summary>Every <c>library.file_action</c> row for <paramref name="mediaId"/>, newest first,
    /// bounded to <paramref name="limit"/> — test-support only (STORY-379's own facts), not a
    /// production read path today; T381's own confirm-endpoint response, or a future admin history
    /// surface, may reuse this exact seam rather than inventing a second one.</summary>
    public async Task<IReadOnlyList<FileActionAuditRecord>> ListAuditAsync(long mediaId, int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<FileActionAuditQueryRow>(new CommandDefinition(
            """
            select id, media_id, verb::text as verb, from_path, to_path, plan_token,
                   performed_at, outcome, detail::text as detail
            from library.file_action
            where media_id = @mediaId
            order by performed_at desc
            limit @limit
            """,
            new { mediaId, limit }, cancellationToken: ct));

        return rows.Select(ToRecord).ToList();
    }

    static CommandDefinition BuildAuditCommand(FileActionAuditEntry audit, NpgsqlTransaction? transaction, CancellationToken ct) =>
        new(
            """
            insert into library.file_action (media_id, verb, from_path, to_path, plan_token, outcome, detail)
            values (@MediaId, @Verb::library.file_verb, @FromPath, @ToPath, @PlanToken, @Outcome, @DetailJson::jsonb)
            """,
            new
            {
                audit.MediaId,
                Verb = FileActionVerbTokens.ToToken(audit.Verb),
                audit.FromPath,
                audit.ToPath,
                audit.PlanToken,
                audit.Outcome,
                audit.DetailJson,
            },
            transaction: transaction,
            cancellationToken: ct);

    static FileActionAuditRecord ToRecord(FileActionAuditQueryRow row) => new(
        row.Id, row.MediaId, ParseVerb(row.Verb), row.FromPath, row.ToPath, row.PlanToken,
        row.PerformedAt, row.Outcome, row.Detail);

    static FileActionVerb ParseVerb(string verb) =>
        FileActionVerbTokens.TryParse(verb, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unrecognised library.file_verb value '{verb}'.");
}
