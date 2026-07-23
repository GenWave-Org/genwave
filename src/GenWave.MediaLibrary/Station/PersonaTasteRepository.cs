using System.Text.Json;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IPersonaTasteStore"/> (SPEC F82.1, F84.1-F84.3;
/// STORY-213) over <c>station.persona_taste</c>. Connection-per-call against a lazily-built
/// station_svc <see cref="NpgsqlDataSource"/> — the same "never touch Postgres until a caller
/// actually needs it" discipline <see cref="PersonaRepository"/> and <see cref="PersonaMemoryRepository"/>
/// document for their own <see cref="Lazy{T}"/> constructor parameter.
///
/// <c>predicate</c>/<c>context</c> round-trip through <see cref="PersonaCardSerializer.Options"/> — the
/// same camelCase JSON convention a <see cref="TasteRule"/> already uses inside a
/// <see cref="PersonaCard"/>'s <c>taste[]</c> (T56) — rather than a second, differently-configured
/// <see cref="JsonSerializerOptions"/>. <see cref="TasteRule"/>'s own constructor re-validates
/// <c>weight</c> on every read, which can never throw here: the column's own
/// <c>CHECK (weight BETWEEN -1 AND 1)</c> guarantees every stored value is already in range.
/// </summary>
sealed class PersonaTasteRepository(Lazy<NpgsqlDataSource> dataSource) : IPersonaTasteStore
{
    const string SelectColumns =
        """
        select id::bigint as id, persona_id::bigint as persona_id,
               predicate::text as predicate, context::text as context,
               weight, source, created_at, updated_at
        from station.persona_taste
        """;

    public async Task<long> InsertAsync(long personaId, TasteRule rule, PersonaTasteSource source, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<long>(new CommandDefinition(
            """
            insert into station.persona_taste (persona_id, predicate, context, weight, source)
            values (@PersonaId, @Predicate::jsonb, @Context::jsonb, @Weight, @Source)
            returning id::bigint
            """,
            ToWriteParameters(personaId, rule, source),
            cancellationToken: ct));
    }

    /// <summary>
    /// Two-statement upsert-by-identity rather than a single <c>INSERT ... ON CONFLICT</c>: jsonb has
    /// no default btree opclass usable as a unique index's conflict target, so there is no constraint
    /// to name in an <c>ON CONFLICT</c> clause. The UPDATE's own WHERE clause IS the identity check —
    /// (<paramref name="personaId"/>, <paramref name="source"/>, and jsonb equality on predicate and
    /// context; jsonb <c>=</c> compares parsed structure, not literal text, so this holds regardless of
    /// key order/whitespace in the serialized form) — and a row is inserted only when that UPDATE
    /// affects nothing.
    /// </summary>
    public async Task<long> ReplaceAsync(long personaId, TasteRule rule, PersonaTasteSource source, CancellationToken ct)
    {
        var parameters = ToWriteParameters(personaId, rule, source);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var updatedId = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            """
            update station.persona_taste
            set weight = @Weight, updated_at = now()
            where persona_id = @PersonaId and source = @Source
              and predicate = @Predicate::jsonb and context = @Context::jsonb
            returning id::bigint
            """,
            parameters,
            cancellationToken: ct));

        if (updatedId is long id)
            return id;

        return await conn.QuerySingleAsync<long>(new CommandDefinition(
            """
            insert into station.persona_taste (persona_id, predicate, context, weight, source)
            values (@PersonaId, @Predicate::jsonb, @Context::jsonb, @Weight, @Source)
            returning id::bigint
            """,
            parameters,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PersonaTasteRow>(new CommandDefinition(
            $"""
            {SelectColumns}
            where persona_id = @PersonaId
              and (@Source is null or source = @Source)
            order by id
            """,
            new { PersonaId = personaId, Source = source is null ? null : ToSourceText(source.Value) },
            cancellationToken: ct));

        return rows.Select(ToEntry).ToList();
    }

    public async Task<int> DeleteAsync(long personaId, PersonaTasteSource source, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.persona_taste where persona_id = @PersonaId and source = @Source",
            new { PersonaId = personaId, Source = ToSourceText(source) },
            cancellationToken: ct));
    }

    /// <summary>
    /// Weight is narrowed to <see langword="float"/> here — matching the column's <c>real</c> storage
    /// width exactly — rather than left as <see cref="TasteRule.Weight"/>'s <see langword="double"/>,
    /// so Npgsql's inferred parameter type needs no assignment cast at all.
    /// </summary>
    static object ToWriteParameters(long personaId, TasteRule rule, PersonaTasteSource source) => new
    {
        PersonaId = personaId,
        Predicate = JsonSerializer.Serialize(rule.Predicate, PersonaCardSerializer.Options),
        Context = JsonSerializer.Serialize(rule.Context, PersonaCardSerializer.Options),
        Weight = (float)rule.Weight,
        Source = ToSourceText(source),
    };

    static PersonaTasteEntry ToEntry(PersonaTasteRow row) => new(
        row.Id,
        row.PersonaId,
        new TasteRule(
            JsonSerializer.Deserialize<TastePredicate>(row.Predicate, PersonaCardSerializer.Options)
                ?? throw new InvalidOperationException("station.persona_taste.predicate deserialized to null"),
            Normalize(JsonSerializer.Deserialize<TasteContext>(row.Context, PersonaCardSerializer.Options)
                ?? throw new InvalidOperationException("station.persona_taste.context deserialized to null")),
            row.Weight),
        ToSource(row.Source),
        row.CreatedAt,
        row.UpdatedAt);

    /// <summary>
    /// gh-#87 — a stored context of <c>{}</c> (persona card import, a hand-edited row) deserializes
    /// <see cref="TasteContext.DaysOfWeek"/> to null despite the record's non-nullable annotation
    /// (STJ fills constructor parameters by reflection). Coalescing here, at the one read seam every
    /// consumer goes through, makes the domain type's non-null contract actually hold downstream —
    /// null and <c>[]</c> both mean "no day gate" (SPEC F82.1).
    /// </summary>
    static TasteContext Normalize(TasteContext context) =>
        context.DaysOfWeek is null ? context with { DaysOfWeek = [] } : context;

    static string ToSourceText(PersonaTasteSource source) => source switch
    {
        PersonaTasteSource.Authored => "authored",
        PersonaTasteSource.Operator => "operator",
        PersonaTasteSource.Accrued => "accrued",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown persona taste source"),
    };

    static PersonaTasteSource ToSource(string source) => source switch
    {
        "authored" => PersonaTasteSource.Authored,
        "operator" => PersonaTasteSource.Operator,
        "accrued" => PersonaTasteSource.Accrued,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown persona taste source"),
    };
}
