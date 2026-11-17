using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

// The operator's half (SPEC F218.19/.20/.24): list and edit rows, each statement scoped by persona AND row id.
sealed partial class PersonaMemoryRepository : IPersonaMemoryAdmin
{
    // The asked-count window is CallbackStore.OfferRetentionDays (F218.48); an int constant, so interpolating it is injection-safe.
    static readonly string AdminColumns =
        $"""
        id::bigint as id, kind, source, content, aired_count as AiredCount, last_aired_at as LastAiredAt,
        created_at as CreatedAt, approved_at as ApprovedAt, forgotten_at as ForgottenAt,
        (select count(*)::integer from station.memory_offer o
          where o.memory_id = station.persona_memory.id and o.offered_at >= now() - interval '{CallbackStore.OfferRetentionDays} days') as AskedCount
        """;

    public async Task<IReadOnlyList<PersonaMemoryAdminRow>> ListForAdminAsync(long personaId, string? kind, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PersonaMemoryAdminRow>(new CommandDefinition(
            $"""
            select {AdminColumns}
            from station.persona_memory
            where persona_id = @personaId
              and kind = any(@kinds)
            order by created_at desc, id desc
            """,
            new { personaId, kinds = kind is null ? MemoryKinds.OperatorShaped : [kind] },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<PersonaMemoryAdminRow> AddAuthoredLoreAsync(long personaId, string text, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<PersonaMemoryAdminRow>(new CommandDefinition(
            $"""
            insert into station.persona_memory (persona_id, kind, content, source)
            values (@personaId, 'lore', @text, 'authored')
            returning {AdminColumns}
            """,
            new { personaId, text },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PersonaMemoryAdminRow>> AddDraftedLoreAsync(
        long personaId, IReadOnlyList<string> texts, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var stored = new List<PersonaMemoryAdminRow>(texts.Count);
        foreach (var text in texts)
            stored.Add(await conn.QuerySingleAsync<PersonaMemoryAdminRow>(new CommandDefinition(
                $"""
                insert into station.persona_memory (persona_id, kind, content, source)
                values (@personaId, 'lore', @text, 'drafted')
                returning {AdminColumns}
                """,
                new { personaId, text },
                tx, cancellationToken: ct)));
        await tx.CommitAsync(ct);
        return stored;
    }

    public async Task<PersonaMemoryUpdateResult> UpdateAsync(
        long personaId, long rowId, PersonaMemoryChange change, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var source = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "select source from station.persona_memory where persona_id = @personaId and id = @rowId and kind = any(@kinds) for update",
            new { personaId, rowId, kinds = MemoryKinds.OperatorShaped },
            tx, cancellationToken: ct));
        if (source is null)
            return new PersonaMemoryUpdateResult(PersonaMemoryUpdateStatus.NotFound, null);
        if (change.Approve && source != MemorySources.Drafted)
            return new PersonaMemoryUpdateResult(PersonaMemoryUpdateStatus.NotDrafted, null);

        var row = await conn.QuerySingleAsync<PersonaMemoryAdminRow>(new CommandDefinition(
            $"""
            update station.persona_memory set
              content = coalesce(@text::text, content),
              approved_at = case when @approve then coalesce(approved_at, now()) else approved_at end,
              forgotten_at = case when @forgotten::boolean is null then forgotten_at
                                  when @forgotten::boolean then coalesce(forgotten_at, now())
                                  else null end
            where persona_id = @personaId and id = @rowId
            returning {AdminColumns}
            """,
            new { personaId, rowId, text = change.Text, approve = change.Approve, forgotten = change.Forgotten },
            tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new PersonaMemoryUpdateResult(PersonaMemoryUpdateStatus.Updated, row);
    }

    public async Task<bool> DeleteAsync(long personaId, long rowId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.persona_memory where persona_id = @personaId and id = @rowId and kind = any(@kinds)",
            new { personaId, rowId, kinds = MemoryKinds.OperatorShaped },
            cancellationToken: ct)) > 0;
    }
}
