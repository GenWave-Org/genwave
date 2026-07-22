using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IPersonaMemory"/> (SPEC F71.4-F71.6, STORY-194) over
/// <c>station.persona_memory</c>. Connection-per-call against a lazily-built station_svc
/// <see cref="NpgsqlDataSource"/> — same "never touch Postgres until a caller actually needs it"
/// discipline <see cref="PersonaRepository"/> documents for its own <see cref="Lazy{T}"/> constructor
/// parameter.
///
/// <b>One clock, the database's:</b> every stored timestamp (<c>created_at</c>'s default,
/// <c>last_aired_at</c> in <see cref="MarkAiredAsync"/>) and every recall-window comparison in
/// <see cref="RecallAsync"/> is computed with Postgres's own <c>now()</c>/<c>make_interval</c>, never
/// this process's <see cref="TimeProvider"/> or <see cref="DateTime.UtcNow"/>. A host container and its
/// database can drift seconds apart; F71.5's mark-before-render guarantee only holds if the timestamp
/// a row is stamped with and the window comparison that later excludes it are measured by the SAME
/// clock, so all of it stays in SQL rather than splitting the math across two clocks.
///
/// Retention eviction (F71.6) runs as a second statement inside <see cref="RecordAsync"/>'s own
/// transaction, in application code — not a <c>plpgsql</c> trigger/function — because
/// ARCHITECTURE.md's Persona foundation section specs it exactly that way ("oldest-accrued evicted in
/// the <c>RecordAsync</c> transaction"); the eviction has no callers beyond this one write path, so a
/// standalone DB function would be one more place to keep in sync for no shared reuse.
/// </summary>
sealed class PersonaMemoryRepository(Lazy<NpgsqlDataSource> dataSource, IOptions<PersonaMemoryOptions> options) : IPersonaMemory
{
    const string SelectColumns =
        """
        select id::bigint as id, persona_id::bigint as persona_id, kind, content, source,
               aired_count, last_aired_at, created_at
        from station.persona_memory
        """;

    /// <summary>
    /// Single insert; when <paramref name="source"/> is <see cref="PersonaMemorySource.Accrued"/>, a
    /// second statement in the SAME transaction deletes every accrued row for this (persona, kind)
    /// beyond <see cref="PersonaMemoryOptions.CapPerKind"/> newest (F71.6): ordering
    /// <c>created_at desc, id desc</c> then skipping the newest <c>CapPerKind</c> rows selects exactly
    /// the oldest excess for deletion — the <c>id desc</c> tiebreak keeps eviction order deterministic
    /// even when two accrued rows land inside the same transaction-frozen <c>now()</c>. An
    /// <see cref="PersonaMemorySource.Authored"/> insert skips this second statement entirely: never
    /// counted toward the cap, never evicted by it.
    /// </summary>
    public async Task<long> RecordAsync(long personaId, string kind, string content, PersonaMemorySource source, CancellationToken ct)
    {
        var sourceText = ToSourceText(source);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = await conn.QuerySingleAsync<long>(new CommandDefinition(
            """
            insert into station.persona_memory (persona_id, kind, content, source)
            values (@PersonaId, @Kind, @Content, @Source)
            returning id::bigint
            """,
            new { PersonaId = personaId, Kind = kind, Content = content, Source = sourceText },
            transaction: tx,
            cancellationToken: ct));

        if (source == PersonaMemorySource.Accrued)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                delete from station.persona_memory
                where id in (
                    select id from station.persona_memory
                    where persona_id = @PersonaId and kind = @Kind and source = 'accrued'
                    order by created_at desc, id desc
                    offset @CapPerKind
                )
                """,
                new { PersonaId = personaId, Kind = kind, options.Value.CapPerKind },
                transaction: tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return id;
    }

    /// <summary>
    /// Stamps <c>last_aired_at = now()</c> and increments <c>aired_count</c> using the database's own
    /// clock (see this class's remarks). An unknown <paramref name="id"/> is a silent no-op: there is
    /// no caller-visible outcome to distinguish from success, only "this id is no longer offered".
    /// </summary>
    public async Task MarkAiredAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "update station.persona_memory set aired_count = aired_count + 1, last_aired_at = now() where id = @id",
            new { id },
            cancellationToken: ct));
    }

    /// <summary>
    /// F71.4's two recall shapes share one query. The first predicate below is the anti-repeat gate:
    /// it is satisfied unconditionally whenever either window is set (the callback shape), and
    /// otherwise (neither window set — the anti-repeat shape) requires the row to have actually
    /// aired — a bit that has never gone out has nothing to "recently done, do differently" about.
    /// The next two predicates are the callback gates: <c>NotAiredWithin</c> passes a row when it has
    /// never aired OR its last airing is older than the window; <c>CreatedWithin</c> passes a row when
    /// unset or the row's age is within the window. Every comparison runs against Postgres's own
    /// <c>now()</c> via <c>make_interval</c>, never a host-computed cutoff (see this class's remarks).
    /// Ordering matches the <c>persona_memory_recall</c> index verbatim (never-aired rows first, then
    /// most-recently-aired), so both shapes are index-satisfied.
    /// </summary>
    public async Task<IReadOnlyList<PersonaMemoryEntry>> RecallAsync(long personaId, RecallSpec spec, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PersonaMemoryEntry>(new CommandDefinition(
            $"""
            {SelectColumns}
            where persona_id = @PersonaId
              and kind = @Kind
              and (@NotAiredWithinSeconds is not null or @CreatedWithinSeconds is not null or last_aired_at is not null)
              and (@NotAiredWithinSeconds is null or last_aired_at is null
                   or last_aired_at <= now() - make_interval(secs => @NotAiredWithinSeconds))
              and (@CreatedWithinSeconds is null or created_at >= now() - make_interval(secs => @CreatedWithinSeconds))
            order by last_aired_at desc nulls first
            limit @Take
            """,
            new
            {
                PersonaId = personaId,
                spec.Kind,
                NotAiredWithinSeconds = spec.NotAiredWithin?.TotalSeconds,
                CreatedWithinSeconds = spec.CreatedWithin?.TotalSeconds,
                spec.Take,
            },
            cancellationToken: ct));

        return rows.ToList();
    }

    /// <summary>
    /// Export's source-filtered read (SPEC F79.1, STORY-208): every row for
    /// <paramref name="personaId"/> in <paramref name="source"/>, every <c>kind</c>, no aired-recency
    /// gate — <see cref="RecallAsync"/>'s windows exist for prompt assembly, not for "every authored
    /// row this persona has". Ordered by id (authoring/insertion order) — a human reading an exported
    /// card's <c>lore[]</c> benefits more from that than from any recall-relevant ordering.
    /// </summary>
    public async Task<IReadOnlyList<PersonaMemoryEntry>> ListAsync(long personaId, PersonaMemorySource source, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PersonaMemoryEntry>(new CommandDefinition(
            $"""
            {SelectColumns}
            where persona_id = @PersonaId and source = @Source
            order by id
            """,
            new { PersonaId = personaId, Source = ToSourceText(source) },
            cancellationToken: ct));

        return rows.ToList();
    }

    static string ToSourceText(PersonaMemorySource source) => source switch
    {
        PersonaMemorySource.Authored => "authored",
        PersonaMemorySource.Accrued => "accrued",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown persona memory source"),
    };
}
