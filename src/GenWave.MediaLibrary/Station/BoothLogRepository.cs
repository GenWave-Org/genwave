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
        select id::bigint as id, occurred_at, kind, summary, persona_id::bigint as persona_id,
               pick::text as pick, media_id
        from station.booth_log
        """;

    /// <summary>
    /// <paramref name="request"/>'s <see cref="BoothLogAppendRequest.PersonaId"/> (SPEC F84.6,
    /// STORY-215) was captured SYNCHRONOUSLY by <see cref="BoothLogWriter.Publish"/> at air time, well
    /// before this append ever runs — a new edge that drain-time resolution never had: the persona can
    /// be DELETED in the gap between air and this call, leaving it a dangling reference. That insert
    /// fails the <c>persona_id</c> FK (23503) even though <c>booth_log.persona_id</c>'s own
    /// <c>ON DELETE SET NULL</c> already protects every row persisted BEFORE the delete — SET NULL
    /// cannot help a row that has not been inserted yet. Caught here specifically and retried
    /// unstamped: the booth-log row itself must never be dropped over a stamp that went stale
    /// mid-flight. <see cref="BoothLogAppendRequest.Artist"/> (SPEC F84.1, STORY-215, PLAN T70) is
    /// plain text — no FK, no degrade path of its own. <see cref="BoothLogAppendRequest.Pick"/> (SPEC
    /// F86.1, STORY-217, PLAN T73) is likewise plain text (pre-serialized jsonb, no FK) — the
    /// persona-id retry below never touches it either way. <see cref="BoothLogAppendRequest.SegmentKind"/>
    /// (SPEC F113.1, STORY-304, PLAN T220) is likewise plain text (the SegmentKind enum's token name,
    /// or null) — no FK, no degrade path. <see cref="BoothLogAppendRequest.ShowId"/> (SPEC F121.1,
    /// STORY-310, PLAN T242) is likewise no-FK-by-design (history outlives the entity) — the
    /// persona-id retry never touches any of these three either.
    /// </summary>
    public async Task AppendAsync(BoothLogAppendRequest request, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        try
        {
            await InsertAndEvictAsync(conn, request, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation && request.PersonaId is not null)
        {
            // The failed attempt's `await using var tx` already rolled back (disposal runs as the
            // exception unwinds InsertAndEvictAsync, before it reaches this catch) — the connection
            // is clean, so retrying a fresh transaction on the SAME conn is safe.
            await InsertAndEvictAsync(conn, request with { PersonaId = null }, ct);
        }
    }

    async Task InsertAndEvictAsync(NpgsqlConnection conn, BoothLogAppendRequest request, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.booth_log (kind, summary, persona_id, artist, pick, media_id, segment_kind, show_id)
            values (@Kind, @Summary, @PersonaId, @Artist, @Pick::jsonb, @MediaId, @SegmentKind, @ShowId)
            """,
            new
            {
                request.Kind, request.Summary, request.PersonaId, request.Artist, request.Pick,
                request.MediaId, request.SegmentKind, request.ShowId,
            },
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

    /// <inheritdoc/>
    public async Task<long?> GetMediaIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "select media_id from station.booth_log where id = @Id",
            new { Id = id },
            cancellationToken: ct));
    }

    /// <inheritdoc/>
    public async Task<BoothLogAiring?> GetTrackAiringAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<TrackAiringRow>(new CommandDefinition(
            "select kind, media_id, occurred_at from station.booth_log where id = @Id",
            new { Id = id },
            cancellationToken: ct));

        return row is null
            ? null
            : new BoothLogAiring(row.Kind, row.MediaId, new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)));
    }

    /// <summary>Ephemeral Dapper projection for <see cref="GetTrackAiringAsync"/> — <c>occurred_at</c>
    /// reads back as <see cref="DateTime"/> (Dapper's own reader-inferred type for <c>timestamptz</c>,
    /// the same <see cref="BoothLogEntry.OccurredAt"/> convention every other booth-log read in this
    /// class already follows — a <see cref="DateTimeOffset"/>-typed constructor parameter here fails
    /// Dapper's constructor match outright, proven at T367 review). Converted to
    /// <see cref="DateTimeOffset"/> only at the boundary <see cref="BoothLogAiring"/> itself promises.</summary>
    sealed record TrackAiringRow(string Kind, long? MediaId, DateTime OccurredAt);

    /// <inheritdoc/>
    /// <summary>
    /// See <see cref="IBoothLogReader.GetLastAiringAsync"/> for the "contiguous run" definition this
    /// query implements — <c>marked</c>/<c>runs</c> assign a monotonically non-decreasing
    /// <c>run_id</c> that increments the instant either the <c>show_id</c> changes (<c>IS DISTINCT
    /// FROM</c>, so the very first row — whose <c>lag</c> is <c>NULL</c> — starts its own run rather
    /// than comparing against nothing) or the gap to the previous row exceeds three hours.
    ///
    /// <para>
    /// <b>Two SEPARATE window-function CTE levels (T362 review HIGH-1), never one nested inside the
    /// other.</b> The original draft called <c>lag(...) over w</c> INSIDE the argument of
    /// <c>sum(...) over w</c> in the same <c>select</c> item — Postgres rejects that outright
    /// ("window function calls cannot be nested", every real station 500s on this route). <c>marked</c>
    /// computes the boundary flag (one window function, <c>lag</c>) as its own plain column;
    /// <c>runs</c> then sums THAT column (a second, independent window function, <c>sum</c>) — legal
    /// because by the time <c>runs</c> runs, <c>boundary</c> is an ordinary materialized column, not a
    /// window function call.
    /// </para>
    ///
    /// <para>
    /// <b>Bounded, not a 14-day full-table scan (T362 review MED-4).</b> <c>target_anchor</c> finds
    /// <paramref name="showId"/>'s own most recent <c>"track-started"</c> timestamp FIRST — a single
    /// indexed lookup (<c>booth_log_show_track_started</c>, <c>(show_id, occurred_at) where kind =
    /// 'track-started'</c>) — then <c>track_rows</c> narrows to a 48-hour window ending at that anchor,
    /// ACROSS EVERY SHOW (never scoped to <paramref name="showId"/> alone): a run genuinely ends the
    /// instant a DIFFERENT show's row lands between two of this show's own airings, even when every
    /// timestamp involved sits well inside the three-hour gap threshold — narrowing the base rows to
    /// <paramref name="showId"/> ALONE before computing boundaries would silently lose that
    /// cross-show interruption (verified: two of this show's own rows either side of another show's,
    /// all within three hours of each other, must count as TWO runs of the CLICKED show, picks=2, not
    /// one merged run of 4 — see this method's own MediaLibrary.Tests fact). 48 hours is a generous,
    /// fixed bound — no real contiguous run (three-hour-gap-free) plausibly spans that long — chosen
    /// over the full <c>BoothLog:RetentionDays</c> window (14 days by default) specifically so this
    /// query's own cost stops scaling with retention.
    /// </para>
    ///
    /// The final <c>select</c> finds <paramref name="showId"/>'s own highest <c>run_id</c> within the
    /// bounded window (its most recent run, since <c>run_id</c> only ever increases with
    /// <c>occurred_at</c>) and counts every row that run_id carries — which, by construction, all
    /// share <paramref name="showId"/>, since a run boundary fires on any <c>show_id</c> change.
    /// <c>count(*)</c>/<c>count(*) filter(...)</c> over no matching rows (an empty <c>runs</c>, a
    /// <paramref name="showId"/> that has never aired at all — <c>target_anchor.anchor</c> null, so
    /// <c>track_rows</c> is empty by construction — or one that never appears in the bounded window)
    /// both return zero, never <see langword="null"/> — an aggregate with no GROUP BY always answers
    /// exactly one row — so <see cref="ShowLastAiring.Picks"/> of zero is this method's own
    /// "never aired" signal, mapped to a <see langword="null"/> return just below.
    /// </summary>
    public async Task<ShowLastAiring?> GetLastAiringAsync(long showId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleAsync<LastAiringRow>(new CommandDefinition(
            """
            with target_anchor as (
                select max(occurred_at) as anchor
                from station.booth_log
                where kind = 'track-started' and show_id = @ShowId
            ),
            track_rows as (
                select b.show_id, b.occurred_at, (b.pick ->> 'rotationRelax')::int as rotation_relax
                from station.booth_log b, target_anchor a
                where b.kind = 'track-started'
                  and a.anchor is not null
                  and b.occurred_at between a.anchor - interval '48 hours' and a.anchor
            ),
            marked as (
                select
                    show_id,
                    rotation_relax,
                    occurred_at,
                    case
                        when lag(show_id) over w is distinct from show_id
                          or occurred_at - lag(occurred_at) over w > interval '3 hours'
                        then 1 else 0
                    end as boundary
                from track_rows
                window w as (order by occurred_at)
            ),
            runs as (
                select
                    show_id,
                    rotation_relax,
                    sum(boundary) over (order by occurred_at) as run_id
                from marked
            )
            select
                count(*)::int as picks,
                count(*) filter (where coalesce(rotation_relax, 0) > 0)::int as relaxed
            from runs
            where run_id = (select run_id from runs where show_id = @ShowId order by run_id desc limit 1)
            """,
            new { ShowId = showId },
            cancellationToken: ct));

        return row.Picks == 0 ? null : new ShowLastAiring(row.Picks, row.Relaxed);
    }

    /// <summary>Ephemeral Dapper projection for <see cref="GetLastAiringAsync"/>'s own aggregate
    /// read — never a public shape, mapped straight to <see cref="ShowLastAiring"/>.</summary>
    sealed record LastAiringRow
    {
        public int Picks { get; init; }
        public int Relaxed { get; init; }
    }
}
