using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IRequestStore"/> (SPEC F87, STORY-224, PLAN T86)
/// over <c>station.request</c>. Connection-per-call against a lazily-built station_svc
/// <see cref="NpgsqlDataSource"/> — the same "never touch Postgres until a caller actually needs
/// it" discipline <see cref="PersonaTasteRepository"/>/<see cref="BoothLogRepository"/> document
/// for their own <see cref="Lazy{T}"/> constructor parameter.
///
/// <paramref name="wishRetentionHours"/> (bound from <c>Requests:WishRetentionHours</c>, default
/// 24) arrives as a plain constructor value rather than a bound options type: the owning options
/// class lives in <c>GenWave.Host</c> (env/compose-only, registered the same way
/// <c>ArtworkOptions</c> is), and this project has no reference to that assembly — the Host's own
/// DI wiring extracts the value once and passes it through, mirroring
/// <see cref="BoothLogRepository"/>'s <c>BoothLogOptions.RetentionDays</c> shape in spirit if not
/// in exact mechanism.
/// </summary>
sealed class RequestRepository(Lazy<NpgsqlDataSource> dataSource, int wishRetentionHours) : IRequestStore
{
    /// <summary>
    /// Retention runs inside the SAME transaction as the insert (SPEC F87.8) — the
    /// <see cref="BoothLogRepository.AppendAsync"/> idiom applied to this table: at hobby-station
    /// request rates (never a hot inner loop) a conditional UPDATE on every insert is cheap, and it
    /// guarantees no separate scheduled process is needed to keep the sweep honest. Only rows whose
    /// <c>wish</c> is still non-null are touched — a rescan of already-swept rows on every future
    /// insert would be pure waste.
    /// </summary>
    public async Task<long> InsertAsync(string wish, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = await conn.QuerySingleAsync<long>(new CommandDefinition(
            """
            insert into station.request (wish, expires_at)
            values (@Wish, @ExpiresAt)
            returning id::bigint
            """,
            new { Wish = wish, ExpiresAt = expiresAt },
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.request
            set wish = null
            where wish is not null
              and received_at < now() - make_interval(hours => @RetentionHours)
            """,
            new { RetentionHours = wishRetentionHours },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<int> CountPendingAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*)::int from station.request where status = 'pending'",
            cancellationToken: ct));
    }

    /// <summary>
    /// Deletes (not merely marks) the oldest pending row — an evicted request never fulfilled and
    /// leaves no outcome to inspect, unlike a natural TTL expiry (SPEC F87.6, <c>status =
    /// 'expired'</c>, a later task's concern). A no-op when no pending row exists: the subquery
    /// then selects nothing, so the DELETE matches zero rows.
    /// </summary>
    public async Task EvictOldestPendingAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            delete from station.request
            where id = (
              select id from station.request
              where status = 'pending'
              order by received_at asc, id asc
              limit 1
            )
            """,
            cancellationToken: ct));
    }

    /// <summary>
    /// The "unparsed" discriminator (see <see cref="IRequestStore"/>'s own remarks) applied as a
    /// single-row WHERE clause: <c>wish is not null</c> guards against the (practically unreachable —
    /// the 24h retention window is far longer than any realistic <c>WindowMinutes</c> fulfillment
    /// window) case of a row whose text was already swept while still somehow pending and unparsed —
    /// nothing for a parser to read in that case either way.
    /// </summary>
    public async Task<UnparsedRequest?> GetForParseAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UnparsedRequest?>(new CommandDefinition(
            // Columns aliased to the record's own PascalCase parameter names: Dapper's
            // constructor-matching path for a positional record (unlike its property-setter
            // fallback) matches column name to parameter name directly and does NOT apply
            // DefaultTypeMap.MatchNamesWithUnderscores' snake_case->PascalCase translation.
            """
            select id::bigint as "Id", wish as "Wish", expires_at as "ExpiresAt"
            from station.request
            where id = @Id
              and status = 'pending'
              and wish is not null
              and artist is null
              and title is null
              and moods is null
            """,
            new { Id = id },
            cancellationToken: ct));
    }

    /// <summary>Startup recovery sweep source (see <see cref="IRequestStore"/>'s own remarks) —
    /// oldest first, so a long backlog re-parses in received order.</summary>
    public async Task<IReadOnlyList<long>> ListUnparsedPendingIdsAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            """
            select id::bigint
            from station.request
            where status = 'pending'
              and wish is not null
              and artist is null
              and title is null
              and moods is null
            order by received_at asc, id asc
            """,
            cancellationToken: ct));
        return ids.AsList();
    }

    public async Task MarkParsedAsync(
        long id, string? artist, string? title, IReadOnlyList<string> moods, bool unmatched, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.request
            set artist = @Artist,
                title = @Title,
                moods = @Moods,
                status = case when @Unmatched then 'unmatched' else status end
            where id = @Id
            """,
            new { Id = id, Artist = artist, Title = title, Moods = moods.ToArray(), Unmatched = unmatched },
            cancellationToken: ct));
    }

    /// <summary>Stamps the match, leaves <c>status</c> untouched (see <see cref="IRequestStore"/>'s
    /// own remarks — still <c>pending</c>, T90's fulfillment rung decides what happens next).</summary>
    public async Task MarkMatchedAsync(long id, long mediaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update station.request set matched_media_id = @MediaId where id = @Id",
            new { Id = id, MediaId = mediaId },
            cancellationToken: ct));
    }

    public async Task MarkUnmatchedAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update station.request set status = 'unmatched' where id = @Id",
            new { Id = id },
            cancellationToken: ct));
    }

    /// <summary>
    /// SPEC F87.6, STORY-227, PLAN T90 — oldest-first (see <see cref="IRequestStore"/>'s own remarks
    /// for the tie-break convention), admitting only a row with a T89 match OR a non-empty mood
    /// predicate: <c>cardinality(moods)</c> returns <see langword="null"/> for a <see langword="null"/>
    /// array and <c>0</c> for an empty one, so <c>&gt; 0</c> alone (no separate "moods is not null"
    /// guard) correctly excludes both.
    /// </summary>
    public async Task<FulfillableRequest?> GetOldestLiveAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<FulfillableRequestRow?>(new CommandDefinition(
            """
            select id::bigint as "Id", matched_media_id as "MatchedMediaId", moods as "Moods"
            from station.request
            where status = 'pending'
              and expires_at >= @Now
              and (matched_media_id is not null or cardinality(moods) > 0)
            order by received_at asc, id asc
            limit 1
            """,
            new { Now = now },
            cancellationToken: ct));
        return row?.ToFulfillableRequest();
    }

    public async Task<int> ExpireStaleAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "update station.request set status = 'expired' where status = 'pending' and expires_at < @Now",
            new { Now = now },
            cancellationToken: ct));
    }

    /// <summary>The one-shot CAS (SPEC F87.6): the WHERE clause's own <c>status = 'pending'</c> guard
    /// IS the compare; <c>ExecuteAsync</c>'s affected-row count IS the swap result.</summary>
    public async Task<bool> TryMarkFulfilledAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "update station.request set status = 'fulfilled', fulfilled_at = now() where id = @Id and status = 'pending'",
            new { Id = id },
            cancellationToken: ct));
        return affected == 1;
    }
}
