using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Npgsql implementation of <see cref="ICallbackStore"/> (SPEC F218.43-F218.46). Every window runs on
/// Postgres's <c>now()</c>, the same clock that stamps <c>offered_at</c> and <c>last_aired_at</c>.
/// Connection-per-call against the lazily-built station data source, like <see cref="PersonaMemoryRepository"/>.
/// </summary>
sealed class CallbackStore(Lazy<NpgsqlDataSource> dataSource) : ICallbackStore
{
    internal static readonly TimeSpan LoreRest = TimeSpan.FromHours(6);
    internal static readonly TimeSpan OtherRest = TimeSpan.FromHours(48);
    internal static readonly TimeSpan AskRest = TimeSpan.FromHours(2);
    internal const int OfferRetentionDays = 28;

    public async Task<IReadOnlyList<RecalledMemory>> EligibleAsync(long personaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        // Draft/forgotten/lore-source predicates are the same ones the retired row recall used (T737). last_aired_at is
        // only ever set by a used ask (F218.46), so it is the "used" clock.
        var rows = await conn.QueryAsync<RecalledMemory>(new CommandDefinition(
            """
            select m.id::bigint as id, m.kind, m.content, m.occurred_at
            from station.persona_memory m
            where m.persona_id = @PersonaId
              and m.kind in ('lore', 'episode', 'life')
              and m.forgotten_at is null
              and (m.source <> 'drafted' or m.approved_at is not null)
              and (m.kind <> 'lore' or m.source in ('authored', 'drafted'))
              and (m.last_aired_at is null
                   or m.last_aired_at <= now() - make_interval(secs => case when m.kind = 'lore' then @LoreRestSeconds else @OtherRestSeconds end))
              and not exists (
                  select 1 from station.memory_offer o
                  where o.memory_id = m.id and o.offered_at > now() - make_interval(secs => @AskRestSeconds))
            order by (select max(o.offered_at) from station.memory_offer o where o.memory_id = m.id) asc nulls first,
                     coalesce(m.occurred_at, m.created_at) desc, m.id desc
            """,
            new
            {
                PersonaId = personaId,
                LoreRestSeconds = LoreRest.TotalSeconds,
                OtherRestSeconds = OtherRest.TotalSeconds,
                AskRestSeconds = AskRest.TotalSeconds,
            },
            cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<CallbackOfferHistory> LastOfferAsync(long personaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var (usedSeconds, askSeconds) = await conn.QuerySingleAsync<(double?, double?)>(new CommandDefinition(
            """
            select extract(epoch from now() - max(offered_at) filter (where used))::float8,
                   extract(epoch from now() - max(offered_at))::float8
            from station.memory_offer
            where persona_id = @PersonaId
            """,
            new { PersonaId = personaId },
            cancellationToken: ct));

        return new CallbackOfferHistory(ToSpan(usedSeconds), ToSpan(askSeconds));
    }

    static TimeSpan? ToSpan(double? seconds) => seconds is { } s ? TimeSpan.FromSeconds(s) : null;

    public async Task<long> OfferAsync(long memoryId, long personaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        // A data-modifying CTE always runs to completion, so eviction and insert are one statement.
        return await conn.QuerySingleAsync<long>(new CommandDefinition(
            """
            with evicted as (
                delete from station.memory_offer
                where offered_at < now() - make_interval(days => @RetentionDays)
            )
            insert into station.memory_offer (memory_id, persona_id)
            values (@MemoryId, @PersonaId)
            returning id
            """,
            new { MemoryId = memoryId, PersonaId = personaId, RetentionDays = OfferRetentionDays },
            cancellationToken: ct));
    }

    public async Task SettleAsync(long offerId, long? utteranceId, bool used, IReadOnlyList<string> matchedWords, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // "and not used" makes a retried settle of a used offer a no-op, so the counters cannot double-count.
        var memoryId = await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            update station.memory_offer
            set utterance_id = @UtteranceId, used = @Used, matched_words = @MatchedWords
            where id = @OfferId and not used
            returning memory_id
            """,
            new { OfferId = offerId, UtteranceId = utteranceId, Used = used, MatchedWords = matchedWords.ToArray() },
            transaction: tx,
            cancellationToken: ct));

        if (used && memoryId is { } id)
            await conn.ExecuteAsync(new CommandDefinition(
                "update station.persona_memory set aired_count = aired_count + 1, last_aired_at = now() where id = @id",
                new { id },
                transaction: tx,
                cancellationToken: ct));

        await tx.CommitAsync(ct);
    }
}
