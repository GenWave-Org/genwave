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

    // Postgres SQLSTATE for foreign_key_violation — the FK RESTRICT on
    // station.segment_schedule.persona_id (db/27, SPEC F91.9) fires here when DeleteAsync targets a
    // persona still named by a schedule row (PLAN T120 review F4 — moved down from PersonaController,
    // which used to catch the raw PostgresException itself).
    const string ForeignKeyViolation = "23503";

    // id is `serial` (int4) at rest per the F35.1 schema — few dozen rows, never near 2^31 — but every
    // other id in this codebase is `long` (bigint), so it is cast on the way out for a consistent,
    // single-width C# id type. Mirrors MediaRow's xmin::text cast for the same "storage width differs
    // from the C# projection" reason.
    const string SelectColumns =
        "select id::bigint as id, name, backstory, style, voice, created_at, updated_at, " +
        "imported_from, imported_at, slug from station.persona";

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
    /// Mirrors <see cref="Catalog.AdminLibraryRepository.CreateAsync"/>. Deliberately does not set
    /// <c>imported_from</c>/<c>imported_at</c> (SPEC F90.7) — an authored-in-place persona keeps both
    /// NULL by never naming them in the INSERT's column list; only
    /// <c>PersonaImportRepository.ImportAsync</c> ever writes those two columns.
    /// </summary>
    public async Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        var card = LegacyPersonaCardMapper.BuildCard(draft.Name, draft.Backstory, draft.Style, draft.Voice);
        if (draft.Soul is not null)
            card = card with { Soul = draft.Soul };
        var definition = PersonaCardSerializer.Serialize(card);

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var persona = await conn.QuerySingleAsync<Persona>(new CommandDefinition(
                """
                insert into station.persona (name, backstory, style, voice, slug, definition, enabled)
                values (@Name, @Backstory, @Style, @Voice, @Slug, @Definition::jsonb, true)
                returning id::bigint as id, name, backstory, style, voice, created_at, updated_at,
                    imported_from, imported_at, slug
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
    /// Read-merge-write update inside one transaction; <c>updated_at</c> advances in SQL
    /// (<c>now()</c>), never in C#, so the timestamp is always the server's write time. A missing row
    /// and a name collision are distinguished the same way as <see cref="CreateAsync"/>: the UPDATE
    /// either returns a row (found) or nothing (not found), and a unique violation is caught rather
    /// than pre-checked.
    ///
    /// Edit-wipe guard (gh-#256, superseding the T37 soul-only SQL <c>case</c>): the legacy draft
    /// carries only Name/Backstory/Style/Voice — rebuilding the ENTIRE definition from it (the old
    /// behavior) silently reset every card field the admin editor doesn't render: a catalog-hired
    /// persona's quirks, lore, tagline, corrections, energy disposition, and its VoiceSpec
    /// engine/pace/language all went to empty/defaults on ANY admin edit (even a voice change). The
    /// merge below reads the row's EXISTING definition first (<c>for update</c>, same transaction)
    /// and only overwrites what the draft actually carries: <c>name</c>, the VoiceSpec's
    /// <c>voiceId</c>, and the soul — <paramref name="draft"/>.Soul verbatim when provided (the
    /// editor's direct card-soul edit), else the legacy Backstory/Style rebuild, else (both empty)
    /// the existing soul survives exactly as the old <c>case</c> guaranteed for the migrator's
    /// bootstrap row. A row with no reconciled definition yet falls back to a full
    /// <see cref="LegacyPersonaCardMapper.BuildCard"/>, same as <see cref="CreateAsync"/>.
    ///
    /// Like <see cref="CreateAsync"/>, this UPDATE never names <c>imported_from</c>/<c>imported_at</c>
    /// (SPEC F90.7) — an admin edit to an imported persona leaves its provenance stamp exactly as the
    /// last import left it, never clearing or refreshing it.
    /// </summary>
    public async Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var existingJson = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "select definition::text from station.persona where id = @Id for update",
                new { Id = id },
                transaction: tx,
                cancellationToken: ct));
            if (existingJson is null)
                return new PersonaWriteResult.NotFound();

            var existing = string.IsNullOrEmpty(existingJson) || existingJson == "{}"
                ? null
                : PersonaCardSerializer.Deserialize(existingJson);
            var definition = PersonaCardSerializer.Serialize(MergeCard(existing, draft));

            var persona = await conn.QuerySingleOrDefaultAsync<Persona>(new CommandDefinition(
                """
                update station.persona
                set name = @Name, backstory = @Backstory, style = @Style, voice = @Voice,
                    slug = @Slug, definition = @Definition::jsonb, updated_at = now()
                where id = @Id
                returning id::bigint as id, name, backstory, style, voice, created_at, updated_at,
                    imported_from, imported_at, slug
                """,
                new { draft.Name, draft.Backstory, draft.Style, draft.Voice, Slug = slug, Definition = definition, Id = id },
                transaction: tx,
                cancellationToken: ct));
            if (persona is null)
                return new PersonaWriteResult.NotFound();

            await tx.CommitAsync(ct);
            return new PersonaWriteResult.Updated(persona);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new PersonaWriteResult.NameConflict();
        }
    }

    /// <summary>The gh-#256 merge itself — see <see cref="UpdateAsync"/>'s remarks. Pure so the
    /// integration specs can pin each field's survival rule without a database in the arrange.</summary>
    internal static PersonaCard MergeCard(PersonaCard? existing, PersonaDraft draft)
    {
        if (existing is null)
        {
            var built = LegacyPersonaCardMapper.BuildCard(draft.Name, draft.Backstory, draft.Style, draft.Voice);
            return draft.Soul is null ? built : built with { Soul = draft.Soul };
        }

        var rebuiltSoul = LegacyPersonaCardMapper.BuildSoul(draft.Backstory, draft.Style);
        var soul = draft.Soul ?? (rebuiltSoul.Length > 0 ? rebuiltSoul : existing.Soul);

        return existing with
        {
            Name = draft.Name,
            Soul = soul,
            Voice = existing.Voice with { VoiceId = draft.Voice },
        };
    }

    /// <summary>
    /// Query-then-delete (SPEC F91.9, PLAN T121). Reads every <c>station.segment_schedule</c> row
    /// still naming this persona BEFORE attempting the DELETE: a non-empty result short-circuits
    /// straight to <see cref="PersonaWriteResult.ScheduledElsewhere"/> carrying those slots — an
    /// honest, informative rejection instead of a round trip to the DELETE just to have the FK bounce
    /// it (and the FK violation alone carries no slot detail to report). A benched persona (zero
    /// schedule rows) falls through to the plain SQL DELETE (SPEC F35.4).
    ///
    /// <para>
    /// RACE BACKSTOP: the table's own <c>ON DELETE RESTRICT</c> (db/27) still fires as a
    /// <c>foreign_key_violation</c> if a slot is painted between the query above and the DELETE below
    /// — caught here exactly like <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> already catch a
    /// name collision (PLAN T120 review F4: this mapping lives in the store, never
    /// <c>PersonaController</c>, so the controller never imports Npgsql at all). That path re-queries
    /// the schedule rather than trusting the pre-DELETE read, which is now stale by definition; the
    /// smallest honest result is whatever THAT re-query finds — including empty, if the race closed
    /// the other way (the painted slot was itself removed again before the re-query runs). An empty
    /// <see cref="PersonaWriteResult.ScheduledElsewhere.Slots"/> here still means "rejected, try
    /// again" — it is never treated as "actually fine to delete", since this branch is only ever
    /// reached when the database itself just refused the DELETE.
    /// </para>
    /// </summary>
    public async Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var slots = await QueryScheduledSlotsAsync(conn, id, ct);
        if (slots.Count > 0)
            return new PersonaWriteResult.ScheduledElsewhere(slots);

        try
        {
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                "delete from station.persona where id = @id",
                new { id },
                cancellationToken: ct));
            return affected == 0 ? new PersonaWriteResult.NotFound() : new PersonaWriteResult.Deleted();
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            var raceSlots = await QueryScheduledSlotsAsync(conn, id, ct);
            return new PersonaWriteResult.ScheduledElsewhere(raceSlots);
        }
    }

    /// <summary>
    /// Ephemeral Dapper projection for <see cref="QueryScheduledSlotsAsync"/> — settable properties,
    /// not a positional record, mirrors <see cref="ScheduleRepository"/>'s own <c>ScheduleRow</c>: kept
    /// as a plain <see cref="int"/> here and cast to <see cref="DayOfWeek"/> only when building the
    /// public <see cref="ScheduledSlot"/>, rather than trusting Dapper's constructor-based binding to
    /// coerce an integer column straight into an enum-typed positional-record parameter.
    /// </summary>
    sealed record ScheduledSlotRow
    {
        public int DayOfWeek { get; init; }
        public int StartMinute { get; init; }
        public int EndMinute { get; init; }
    }

    /// <summary>
    /// Every <c>station.segment_schedule</c> row naming <paramref name="personaId"/>, ordered the
    /// same way <see cref="ScheduleRepository.LoadWeekAsync"/> orders the whole grid (day, then start
    /// minute) — so a 409 body listing multiple slots reads in a natural, predictable order.
    /// </summary>
    static async Task<IReadOnlyList<ScheduledSlot>> QueryScheduledSlotsAsync(
        NpgsqlConnection conn, long personaId, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<ScheduledSlotRow>(new CommandDefinition(
            """
            select day_of_week, start_minute, end_minute
            from station.segment_schedule
            where persona_id = @personaId
            order by day_of_week, start_minute
            """,
            new { personaId },
            cancellationToken: ct));

        return rows.Select(row => new ScheduledSlot((DayOfWeek)row.DayOfWeek, row.StartMinute, row.EndMinute)).ToList();
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
    /// Batch card read (gh-#256) — one query for every row's <c>definition</c>, keyed by id, feeding
    /// <c>GET /api/personas</c>'s soul/quirks/lore projection. Same <c>::text</c> idiom and the same
    /// <c>'{}'</c>-sentinel-degrades-to-absent posture as <see cref="GetCardByIdAsync"/>, batched.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, PersonaCard>> GetCardsAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long Id, string Definition)>(new CommandDefinition(
            "select id::bigint as id, definition::text as definition from station.persona",
            cancellationToken: ct));

        var cards = new Dictionary<long, PersonaCard>();
        foreach (var (id, json) in rows)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") continue;
            if (PersonaCardSerializer.Deserialize(json) is { } card) cards[id] = card;
        }

        return cards;
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
