using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IPersonaStore"/> (SPEC F35.1, STORY-118) over
/// <c>station.persona</c>. Connection-per-query against a station_svc-scoped
/// <see cref="NpgsqlDataSource"/> — mirrors <see cref="Catalog.AdminLibraryRepository"/>'s wiring,
/// but against the <c>station</c> schema/role rather than <c>library</c>.
///
/// <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> also keep the F71.1 card columns
/// (<c>slug</c>, <c>definition</c>, <c>enabled</c>) reconciled on every write via
/// <see cref="LegacyPersonaCardMapper"/> (STORY-192) — <see cref="IPersonaStore"/>'s own contract is
/// unchanged; this is a storage-layer detail invisible to every existing consumer.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> (T37, STORY-193 wiring fix): every real
/// TTS render now resolves <see cref="IActivePersonaAccessor"/> through
/// <c>ActivePersonaCorrectionsCache</c>, even on a deployment with no <c>Station</c> Postgres
/// connection configured (<c>ConnectionStrings:Station</c> is <c>""</c> by default — no personas at
/// all is a supported, working configuration). Building an <see cref="NpgsqlDataSource"/> from an
/// empty connection string throws immediately (<c>ArgumentException: Host can't be null</c>), so
/// forcing that build merely by RESOLVING <see cref="IPersonaStore"/> — as a non-lazy
/// constructor parameter would — turned every TTS render into a hard failure on such a deployment.
/// Deferred to first ACTUAL query instead: <see cref="IActivePersonaAccessor"/>'s own
/// <c>activeId &lt;= 0</c> short-circuit (no active persona) means this repository's connection is
/// never touched at all unless a persona genuinely is configured, restoring
/// <c>PersonaServiceCollectionExtensions.AddPersonaStore</c>'s own documented intent ("the failure
/// only surfaces if a request actually resolves IPersonaStore" — resolves AND USES, precisely).
/// </summary>
sealed class PersonaRepository(Lazy<NpgsqlDataSource> dataSource) : IPersonaStore
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
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Persona>(new CommandDefinition(
            $"{SelectColumns} order by name",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Persona?> GetByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
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
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
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
    ///
    /// Edit-wipe guard (review follow-up, T37): <paramref name="draft"/> only ever carries the
    /// legacy Backstory/Style/Voice fields, so <see cref="LegacyPersonaCardMapper.BuildCard"/>
    /// rebuilds <c>definition.soul</c> from THOSE two fields alone on every admin edit — fine for an
    /// ordinary persona, where they are its only source of soul text, but wrong for
    /// <c>PersonaCardMigrator</c>'s <c>"default"</c> bootstrap row: that row's own INSERT never sets
    /// its legacy backstory/style columns (they stay at their empty defaults), while its
    /// <c>definition.soul</c> holds a one-time snapshot the migrator captured from whichever persona
    /// was active at boot. Rebuilding unconditionally from the (empty) legacy columns would silently
    /// overwrite that snapshot with an empty soul the instant an operator edits the default
    /// persona's name or voice — so the <c>case</c> below keeps the EXISTING row's
    /// <c>definition.soul</c> whenever the freshly rebuilt one would be empty AND the existing one
    /// is not; every other field of the rebuilt definition (name, tagline, quirks, voice, ...) still
    /// overwrites normally.
    /// </summary>
    public async Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        var definition = PersonaCardSerializer.Serialize(
            LegacyPersonaCardMapper.BuildCard(draft.Name, draft.Backstory, draft.Style, draft.Voice));

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var persona = await conn.QuerySingleOrDefaultAsync<Persona>(new CommandDefinition(
                """
                update station.persona
                set name = @Name, backstory = @Backstory, style = @Style, voice = @Voice,
                    slug = @Slug,
                    definition = case
                        when coalesce(@Definition::jsonb ->> 'soul', '') = ''
                             and coalesce(definition ->> 'soul', '') <> ''
                        then jsonb_set(@Definition::jsonb, '{soul}', definition -> 'soul')
                        else @Definition::jsonb
                    end,
                    updated_at = now()
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
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.persona where id = @id",
            new { id },
            cancellationToken: ct));
        return affected == 0 ? new PersonaWriteResult.NotFound() : new PersonaWriteResult.Deleted();
    }

    /// <summary>
    /// F71.3/F71.7's card read seam (STORY-193): <c>::text</c> cast mirrors
    /// <c>PersonaCardMigrator</c>/its own spec's own read of this column (Story192) rather than
    /// inventing a second jsonb-read idiom. The <c>'{}'</c> sentinel — <see cref="PersonaCardMigrator"/>'s
    /// own "not yet reconciled" marker — degrades to <see langword="null"/> here rather than being
    /// deserialized: <see cref="PersonaCardSerializer.Deserialize"/> trusts every definition it is
    /// handed to already be a real card, and <c>'{}'</c> would silently produce one with null
    /// reference-typed properties.
    /// </summary>
    public async Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var json = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select definition::text from station.persona where id = @id",
            new { id },
            cancellationToken: ct));

        return string.IsNullOrEmpty(json) || json == "{}" ? null : PersonaCardSerializer.Deserialize(json);
    }

    /// <summary>
    /// F79.1/F79.3's slug-to-id primitive (STORY-208/209): the export/import routes address a
    /// persona by its <c>slug</c>, but every other table a card export/import touches
    /// (<c>persona_memory</c>, <c>persona_taste</c>) keys off the numeric id. A scalar lookup rather
    /// than folding this into <see cref="GetCardByIdAsync"/> — callers that only need the id (to then
    /// query those other tables) never pay for deserializing a definition they will not use.
    /// </summary>
    public async Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "select id::bigint from station.persona where slug = @slug",
            new { slug },
            cancellationToken: ct));
    }
}
