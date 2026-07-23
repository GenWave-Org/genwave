using Dapper;
using GenWave.Core.Abstractions;
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
}
