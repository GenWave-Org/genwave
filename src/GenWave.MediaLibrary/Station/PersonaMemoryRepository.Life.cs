using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

// The life half (SPEC F218.18, STORY-537): extracted <c>life</c> rows, deduped on their normalised text and capped.
sealed partial class PersonaMemoryRepository
{
    public async Task<bool> AddAsync(NewLifeClaim claim, CancellationToken ct) =>
        await WriteLifeAsync(null, [claim], ct) > 0;

    public async Task AddOutingAsync(NewMemoryEvent outing, IReadOnlyList<NewLifeClaim> claims, CancellationToken ct) =>
        await WriteLifeAsync(outing, claims, ct);

    /// <summary>
    /// One transaction: drop the claims the persona already has (compared on <see cref="LifeMemoryRules.Normalise"/>
    /// in code, so there is one definition of "same claim", forgotten rows included), write the shared event when
    /// any claim is new, insert the rows, then evict each persona past <see cref="LifeMemoryRules.Cap"/>. Returns
    /// the number of rows written. Concurrency is one writer (the extractor), so the check-then-insert needs no lock.
    /// </summary>
    async Task<int> WriteLifeAsync(NewMemoryEvent? outing, IReadOnlyList<NewLifeClaim> claims, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var fresh = new List<NewLifeClaim>();
        foreach (var claim in claims)
            if (!await HasClaimAsync(conn, tx, claim, fresh, ct))
                fresh.Add(claim);

        if (fresh.Count == 0)
            return 0;

        long? eventId = outing is null
            ? null
            : await conn.QuerySingleAsync<long>(new CommandDefinition(
                """
                insert into station.memory_event (occurred_at, kind, summary)
                values (@OccurredAt, @Kind, @Summary)
                returning id::bigint
                """,
                new { OccurredAt = outing.OccurredAt.ToUniversalTime(), outing.Kind, outing.Summary },
                transaction: tx,
                cancellationToken: ct));

        var written = new List<long>();
        foreach (var claim in fresh)
            written.Add(await conn.QuerySingleAsync<long>(new CommandDefinition(
                """
                insert into station.persona_memory (persona_id, kind, content, source, event_id)
                values (@PersonaId, 'life', @Content, 'extracted', @EventId)
                returning id::bigint
                """,
                new { claim.PersonaId, claim.Content, EventId = eventId },
                transaction: tx,
                cancellationToken: ct)));

        foreach (var personaId in fresh.Select(c => c.PersonaId).Distinct())
            await EvictLifeAsync(conn, tx, personaId, written, ct);

        await tx.CommitAsync(ct);
        return fresh.Count;
    }

    static async Task<bool> HasClaimAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, NewLifeClaim claim, List<NewLifeClaim> pending, CancellationToken ct)
    {
        var wanted = LifeMemoryRules.Normalise(claim.Content);
        if (pending.Any(p => p.PersonaId == claim.PersonaId && LifeMemoryRules.Normalise(p.Content) == wanted))
            return true;

        var existing = await conn.QueryAsync<string>(new CommandDefinition(
            "select content from station.persona_memory where persona_id = @PersonaId and kind = 'life'",
            new { claim.PersonaId },
            transaction: tx,
            cancellationToken: ct));
        return existing.Any(content => LifeMemoryRules.Normalise(content) == wanted);
    }

    /// <summary>
    /// Keeps the persona's extracted life rows at <see cref="LifeMemoryRules.Cap"/>: the excess goes, never-aired
    /// first (oldest <c>created_at</c>, then lowest id), then, when every row has aired, the least recently
    /// aired. <paramref name="keepIds"/>, the rows just written, are never evicted. Authored life rows are not counted,
    /// and neither are forgotten rows: a forgotten row is a tombstone that keeps its claim from being learned again
    /// (see <c>HasClaimAsync</c>), so it neither counts toward the cap nor is evicted.
    /// </summary>
    static async Task EvictLifeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long personaId, IReadOnlyList<long> keepIds, CancellationToken ct) =>
        await conn.ExecuteAsync(new CommandDefinition(
            """
            delete from station.persona_memory
            where id in (
                select id from station.persona_memory
                where persona_id = @PersonaId and kind = 'life' and source = 'extracted' and forgotten_at is null
                  and id::bigint <> all(@KeepIds)
                order by (last_aired_at is not null), coalesce(last_aired_at, created_at), id
                limit greatest(0, (select count(*) from station.persona_memory
                                   where persona_id = @PersonaId and kind = 'life' and source = 'extracted'
                                     and forgotten_at is null) - @Cap)
            )
            """,
            new { PersonaId = personaId, KeepIds = keepIds.ToArray(), Cap = LifeMemoryRules.Cap },
            transaction: tx,
            cancellationToken: ct));
}
