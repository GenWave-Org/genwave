using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The Postgres-backed store for <c>station.booth_log</c> (SPEC F72.1-F72.3, STORY-195). Connection-per-call
/// against a lazily-built station_svc <see cref="NpgsqlDataSource"/> — same discipline
/// <see cref="PersonaMemoryRepository"/> documents for its own <see cref="Lazy{T}"/> constructor
/// parameter (an empty/dev-mode <c>ConnectionStrings:Station</c> must never block boot; the failure
/// only surfaces if a caller actually appends or reads).
///
/// <b>Retention runs inside the same transaction as the insert</b> (SPEC F72.3, see
/// <see cref="InsertAndEvictAsync"/>, which <see cref="AppendAsync"/> calls), in
/// application code rather than a separate job or <c>plpgsql</c> trigger: at hobby-station event
/// rates (one row per track start/patter/mode change, never a hot inner loop) a DELETE on every
/// insert is cheap, and it guarantees the table never grows unbounded without needing a second
/// scheduled process to keep in sync. Mirrors <see cref="PersonaMemoryRepository.RecordAsync"/>'s own
/// "eviction as a second statement in the same transaction" shape.
///
/// Registered concretely once and exposed under BOTH seams it implements
/// (<see cref="IBoothLogAppender"/>/<see cref="IBoothLogReader"/>) — the same "one instance, every
/// interface" idiom <c>NormalizingTtsSynthesizer</c>/<c>LlmCopyWriter</c> use, so the drain loop that
/// writes and the admin endpoint that reads are never two drifted instances.
/// </summary>
sealed class BoothLogRepository(Lazy<NpgsqlDataSource> dataSource, IOptions<BoothLogOptions> options)
    : IBoothLogAppender, IBoothLogReader
{
    // Postgres SQLSTATE code for foreign-key violation — mirrors MediaRatingRepository/MediaRepository.
    const string ForeignKeyViolation = "23503";

    // persona_id is `integer` at rest (mirrors station.persona.id's own `serial` width) but every id
    // in this project's C# projection is `long` — cast on the way out, same reason PersonaRepository's
    // own SelectColumns casts id::bigint (and PersonaTasteRepository casts persona_id::bigint):
    // Dapper's record-constructor mapping matches column CLR type to parameter type exactly, so an
    // uncast int4 column fails to bind a `long?` constructor parameter.
    const string SelectColumns =
        """
        select id::bigint as id, occurred_at, kind, summary, persona_id::bigint as persona_id
        from station.booth_log
        """;

    /// <summary>
    /// <paramref name="personaId"/> (SPEC F84.6, STORY-215) was captured SYNCHRONOUSLY by
    /// <see cref="BoothLogWriter.Publish"/> at air time, well before this append ever runs — a new
    /// edge that drain-time resolution never had: the persona can be DELETED in the gap between air
    /// and this call, leaving <paramref name="personaId"/> a dangling reference. That insert fails
    /// the <c>persona_id</c> FK (23503) even though <c>booth_log.persona_id</c>'s own <c>ON DELETE SET
    /// NULL</c> already protects every row persisted BEFORE the delete — SET NULL cannot help a row
    /// that has not been inserted yet. Caught here specifically and retried unstamped: the booth-log
    /// row itself must never be dropped over a stamp that went stale mid-flight. <paramref name="artist"/>
    /// (SPEC F84.1, STORY-215, PLAN T70) is plain text — no FK, no degrade path of its own.
    /// </summary>
    public async Task AppendAsync(string kind, string summary, long? personaId, string? artist, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        try
        {
            await InsertAndEvictAsync(conn, kind, summary, personaId, artist, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation && personaId is not null)
        {
            // The failed attempt's `await using var tx` already rolled back (disposal runs as the
            // exception unwinds InsertAndEvictAsync, before it reaches this catch) — the connection
            // is clean, so retrying a fresh transaction on the SAME conn is safe.
            await InsertAndEvictAsync(conn, kind, summary, personaId: null, artist, ct);
        }
    }

    async Task InsertAndEvictAsync(NpgsqlConnection conn, string kind, string summary, long? personaId, string? artist, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "insert into station.booth_log (kind, summary, persona_id, artist) values (@Kind, @Summary, @PersonaId, @Artist)",
            new { Kind = kind, Summary = summary, PersonaId = personaId, Artist = artist },
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.booth_log where occurred_at < now() - make_interval(days => @RetentionDays)",
            new { options.Value.RetentionDays },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<BoothLogPage> ReadAsync(BoothLogCursor? before, int take, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        // Fetch one extra row beyond `take` to detect whether a next page exists, without a
        // separate COUNT query. Row-wise comparison `(occurred_at, id) < (@BeforeOccurredAt,
        // @BeforeId)` is the exact keyset-continuation predicate for this ORDER BY (occurred_at
        // DESC, id DESC) — no OFFSET, so a concurrently-inserted row can never shift an
        // already-served page.
        //
        // Branched into two statements rather than one `@BeforeOccurredAt is null or (...) < (...)`
        // predicate: with `before = null` every parameter in that row-value comparison is untyped
        // (Dapper sends a plain null for both DateTime? and long?), and Postgres's parser cannot
        // infer a type for `$1`/`$2` from a ROW() comparison the same way it can from a plain
        // `col is null or col < $1` shape — it fails 42P08 ("could not determine data type of
        // parameter") before the query ever runs, no null-cursor row need exist. The cursor branch
        // below has no null parameters, so its types resolve from the columns being compared.
        var command = before is null
            ? new CommandDefinition(
                $"""
                {SelectColumns}
                order by occurred_at desc, id desc
                limit @Limit
                """,
                new { Limit = take + 1 },
                cancellationToken: ct)
            : new CommandDefinition(
                $"""
                {SelectColumns}
                where (occurred_at, id) < (@BeforeOccurredAt, @BeforeId)
                order by occurred_at desc, id desc
                limit @Limit
                """,
                new { BeforeOccurredAt = before.OccurredAt, BeforeId = before.Id, Limit = take + 1 },
                cancellationToken: ct);

        var rows = (await conn.QueryAsync<BoothLogEntry>(command)).ToList();

        var hasMore = rows.Count > take;
        var entries = rows.Take(take).ToList();
        var nextBefore = hasMore ? new BoothLogCursor(entries[^1].OccurredAt, entries[^1].Id) : null;

        return new BoothLogPage(entries, nextBefore);
    }
}
