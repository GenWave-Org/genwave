using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IPersonaStore"/> (SPEC F35.1, STORY-118) over
/// <c>station.persona</c>. Connection-per-query against a station_svc-scoped
/// <see cref="NpgsqlDataSource"/> — mirrors <see cref="Catalog.AdminLibraryRepository"/>'s wiring,
/// but against the <c>station</c> schema/role rather than <c>library</c>. Not registered in DI and
/// has no consumer yet — a later Epic T task wires both.
///
/// <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> also keep the F71.1 card columns
/// (<c>slug</c>, <c>definition</c>, <c>enabled</c>) reconciled on every write via
/// <see cref="LegacyPersonaCardMapper"/> (STORY-192) — <see cref="IPersonaStore"/>'s own contract is
/// unchanged; this is a storage-layer detail invisible to every existing consumer.
/// </summary>
sealed class PersonaRepository(NpgsqlDataSource dataSource) : IPersonaStore
{
    // Postgres SQLSTATE for unique_violation — mirrors AdminLibraryRepository's NameConflict mapping.
    const string UniqueViolation = "23505";

    // id is `serial` (int4) at rest per the F35.1 schema — few dozen rows, never near 2^31 — but every
    // other id in this codebase is `long` (bigint), so it is cast on the way out for a consistent,
    // single-width C# id type. Mirrors MediaRow's xmin::text cast for the same "storage width differs
    // from the C# projection" reason.
    const string SelectColumns =
        "select id::bigint as id, name, backstory, style, voice, created_at, updated_at from station.persona";

    public async Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Persona>(new CommandDefinition(
            $"{SelectColumns} order by name",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Persona?> GetByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Persona>(new CommandDefinition(
            $"{SelectColumns} where id = @id",
            new { id },
            cancellationToken: ct));
    }

    /// <summary>
    /// Single-statement insert (F35.1). The insert IS the uniqueness check — a duplicate name raises
    /// a 23505 unique_violation on <c>station.persona</c>'s <c>UNIQUE(name)</c> constraint, caught here
    /// rather than pre-checked with a SELECT (no TOCTOU gap, no wasted round trip on the common path).
    /// Mirrors <see cref="Catalog.AdminLibraryRepository.CreateAsync"/>.
    /// </summary>
    public async Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        var definition = PersonaCardSerializer.Serialize(
            LegacyPersonaCardMapper.BuildCard(draft.Name, draft.Backstory, draft.Style, draft.Voice));

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            var persona = await conn.QuerySingleAsync<Persona>(new CommandDefinition(
                """
                insert into station.persona (name, backstory, style, voice, slug, definition, enabled)
                values (@Name, @Backstory, @Style, @Voice, @Slug, @Definition::jsonb, true)
                returning id::bigint as id, name, backstory, style, voice, created_at, updated_at
                """,
                new { draft.Name, draft.Backstory, draft.Style, draft.Voice, Slug = slug, Definition = definition },
                cancellationToken: ct));
            return new PersonaWriteResult.Created(persona);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new PersonaWriteResult.NameConflict();
        }
    }

    /// <summary>
    /// Single-statement update; <c>updated_at</c> advances in SQL (<c>now()</c>), never in C#, so the
    /// timestamp is always the server's write time. A missing row and a name collision are
    /// distinguished the same way as <see cref="CreateAsync"/>: the UPDATE either returns a row
    /// (found) or nothing (not found), and a unique violation is caught rather than pre-checked.
    /// </summary>
    public async Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        var definition = PersonaCardSerializer.Serialize(
            LegacyPersonaCardMapper.BuildCard(draft.Name, draft.Backstory, draft.Style, draft.Voice));

        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            var persona = await conn.QuerySingleOrDefaultAsync<Persona>(new CommandDefinition(
                """
                update station.persona
                set name = @Name, backstory = @Backstory, style = @Style, voice = @Voice,
                    slug = @Slug, definition = @Definition::jsonb, updated_at = now()
                where id = @Id
                returning id::bigint as id, name, backstory, style, voice, created_at, updated_at
                """,
                new { draft.Name, draft.Backstory, draft.Style, draft.Voice, Slug = slug, Definition = definition, Id = id },
                cancellationToken: ct));
            return persona is null
                ? new PersonaWriteResult.NotFound()
                : new PersonaWriteResult.Updated(persona);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new PersonaWriteResult.NameConflict();
        }
    }

    public async Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.persona where id = @id",
            new { id },
            cancellationToken: ct));
        return affected == 0 ? new PersonaWriteResult.NotFound() : new PersonaWriteResult.Deleted();
    }
}
