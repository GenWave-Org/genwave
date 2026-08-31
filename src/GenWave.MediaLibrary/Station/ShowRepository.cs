using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IShowStore"/> (SPEC F115.1, STORY-305, PLAN T239) over
/// <c>station.show</c>. Connection-per-query against a station_svc-scoped <see cref="NpgsqlDataSource"/>
/// — mirrors <see cref="PersonaRepository"/>'s own wiring and slug-conflict-via-unique-violation
/// posture exactly (the closest existing station repository: name + house-Slugify'd slug + provenance
/// pair). Never selects/binds <c>persona_id</c>, or any <c>envelope</c> key beyond <c>rotation</c>
/// (SPEC F115.2's "unread this epic" law, narrowed by exactly one field at SPEC F152.3/PLAN T360 — see
/// <see cref="SetRotationAsync"/> and <see cref="RotationEnvelopeCodec"/>).
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/>, the same "resolving must never be enough
/// to trigger a connection attempt" reason every other station-schema store in this file's directory
/// carries one (see <see cref="PersonaRepository"/>'s own remarks in full).
/// </summary>
sealed class ShowRepository(Lazy<NpgsqlDataSource> dataSource, ILogger<ShowRepository> logger) : IShowStore
{
    // Postgres SQLSTATE for unique_violation — mirrors PersonaRepository's NameConflict mapping; here
    // the only UNIQUE constraint on station.show is show_slug_key (db/06, db/35), so any 23505 this
    // repository can trigger is a slug collision.
    const string UniqueViolation = "23505";

    // Postgres SQLSTATE for foreign_key_violation — station.segment_schedule.show_id's ON DELETE
    // RESTRICT (db/06, SPEC F114) fires here when DeleteAsync targets a show a schedule row still
    // names. Mirrors PersonaRepository's own ForeignKeyViolation mapping (PLAN T120 review F4: this
    // mapping lives in the store, never a controller, so no controller ever imports Npgsql). Unlike
    // PersonaRepository.DeleteAsync's query-then-delete idiom, this store does not pre-query the
    // schedule for slot detail — PLAN T240's endpoint-layer guard (SPEC F115.4) names the offending
    // blocks; ShowWriteResult.Referenced stays a bare singleton here.
    const string ForeignKeyViolation = "23503";

    /// <inheritdoc/>
    public event Action? ShowChanged;

    /// <summary>
    /// Ephemeral Dapper projection of one <c>station.show</c> row — settable properties, not a
    /// positional record, mirrors this file's own house idiom for a shape one further mapping step
    /// (<see cref="ToShow"/>) still needs to touch before it becomes the domain type. <see cref="RotationJson"/>
    /// is the raw text <c>envelope -&gt;&gt; 'rotation'</c> extracted (SPEC F152.3, PLAN T360) — Dapper
    /// has no built-in jsonb-to-<see cref="RotationPredicate"/> mapping, so <see cref="RotationEnvelopeCodec"/>
    /// parses it, never this row type itself.
    /// </summary>
    sealed record ShowRow
    {
        public long Id { get; init; }
        public string Name { get; init; } = "";
        public string Slug { get; init; } = "";
        public string? Tagline { get; init; }
        public string? Flavor { get; init; }
        public string? ImportedFrom { get; init; }
        public DateTime? ImportedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string? RotationJson { get; init; }
    }

    // id is `serial` (int4) at rest — mirrors PersonaRepository's own SelectColumns comment: every id
    // in this codebase is `long` (bigint) in C#, so it is cast on the way out for a consistent,
    // single-width C# id type. envelope's own rotation_json column is PLAN T360's own addition (SPEC
    // F152.3) — the ONLY envelope key this repository ever selects.
    const string SelectColumns =
        "select id::bigint as id, name, slug, tagline, flavor, imported_from, imported_at, " +
        "created_at, updated_at, envelope ->> 'rotation' as rotation_json from station.show";

    // Every write below RETURNs this identical column set (SelectColumns' own list, minus the FROM
    // clause) so ToShow has one shape to map from regardless of which statement produced the row.
    const string ReturningColumns =
        "returning id::bigint as id, name, slug, tagline, flavor, imported_from, imported_at, " +
        "created_at, updated_at, envelope ->> 'rotation' as rotation_json";

    public async Task<IReadOnlyList<Show>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ShowRow>(new CommandDefinition(
            $"{SelectColumns} order by name",
            cancellationToken: ct));
        return rows.Select(ToShow).ToList();
    }

    public async Task<Show?> GetByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ShowRow>(new CommandDefinition(
            $"{SelectColumns} where id = @id",
            new { id },
            cancellationToken: ct));
        return row is null ? null : ToShow(row);
    }

    public async Task<Show?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ShowRow>(new CommandDefinition(
            $"{SelectColumns} where slug = @slug",
            new { slug },
            cancellationToken: ct));
        return row is null ? null : ToShow(row);
    }

    /// <summary>
    /// Single-statement insert (SPEC F115.1). The insert IS the uniqueness check — a colliding slug
    /// raises a 23505 unique_violation on <c>station.show</c>'s <c>UNIQUE(slug)</c> constraint, caught
    /// here rather than pre-checked with a SELECT (no TOCTOU gap, no wasted round trip on the common
    /// path) — mirrors <see cref="PersonaRepository.CreateAsync"/>. Deliberately does not name
    /// <c>imported_from</c>/<c>imported_at</c> in the column list (SPEC F90.7's pattern, applied here
    /// per F115.1): an authored show keeps both NULL by construction; only a future import write path
    /// (PLAN T254) ever sets them. <c>tagline</c>/<c>flavor</c> bind through <see cref="NullIfBlank"/>
    /// — an empty/whitespace-only value persists as <c>NULL</c>, matching <see cref="Show"/>'s own
    /// "null when the show carries none" contract instead of a stray <c>''</c>. <c>envelope</c> is
    /// left untouched (stays whatever it already was — NULL for a brand-new row), the same "this
    /// statement never overwrites the whole document" discipline <see cref="SetRotationAsync"/> keeps.
    /// </summary>
    public async Task<ShowWriteResult> CreateAsync(ShowDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        if (ValidateName(draft, slug) is { } invalidName) return invalidName;
        if (ValidateBudgets(draft) is { } violation) return violation;

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var row = await conn.QuerySingleAsync<ShowRow>(new CommandDefinition(
                $"""
                insert into station.show (name, slug, tagline, flavor)
                values (@Name, @Slug, @Tagline, @Flavor)
                {ReturningColumns}
                """,
                new { draft.Name, Slug = slug, Tagline = NullIfBlank(draft.Tagline), Flavor = NullIfBlank(draft.Flavor) },
                cancellationToken: ct));
            return new ShowWriteResult.Created(ToShow(row));
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new ShowWriteResult.SlugConflict();
        }
    }

    /// <summary>
    /// Plain UPDATE (SPEC F115.1) — re-derives <c>slug</c> from the draft's <c>Name</c> the same way
    /// <see cref="CreateAsync"/> does, mirroring <see cref="PersonaRepository.UpdateAsync"/>'s own
    /// "re-derive on every authored edit" rule for <c>Persona.Slug</c>. <c>updated_at</c> advances in
    /// SQL (<c>now()</c>), never in C#. Like <see cref="CreateAsync"/>, never names
    /// <c>imported_from</c>/<c>imported_at</c> — an authored edit leaves an imported show's provenance
    /// stamp exactly as the last import left it (the endpoint-layer gate that refuses an authored edit
    /// to an imported show entirely is PLAN T240's SPEC F115.5, not this seam's) — and, like
    /// <see cref="CreateAsync"/>, binds <c>tagline</c>/<c>flavor</c> through <see cref="NullIfBlank"/>
    /// so clearing either field to <c>""</c> in an edit persists <c>NULL</c>, not an empty string.
    /// Never touches <c>envelope</c> either — an authored name/tagline/flavor edit leaves a show's own
    /// rotation rule (if any) exactly as <see cref="SetRotationAsync"/> last left it.
    /// </summary>
    public async Task<ShowWriteResult> UpdateAsync(long id, ShowDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        if (ValidateName(draft, slug) is { } invalidName) return invalidName;
        if (ValidateBudgets(draft) is { } violation) return violation;

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var row = await conn.QuerySingleOrDefaultAsync<ShowRow>(new CommandDefinition(
                $"""
                update station.show
                set name = @Name, slug = @Slug, tagline = @Tagline, flavor = @Flavor, updated_at = now()
                where id = @Id
                {ReturningColumns}
                """,
                new { draft.Name, Slug = slug, Tagline = NullIfBlank(draft.Tagline), Flavor = NullIfBlank(draft.Flavor), Id = id },
                cancellationToken: ct));
            if (row is null) return new ShowWriteResult.NotFound();

            // PLAN T360 review HIGH-1: this same "cached ShowSummary goes stale" bug already existed
            // for name/tagline/flavor edits — every one of those fields rides ScheduleRepository/
            // SpecialsRepository's own LEFT JOIN into the SAME cached ShowSummary Rotation does. It
            // predates T360 (live since PLAN T241 first joined station.show into the resolver
            // snapshot); T360 is the first task to add a ShowChanged event at all, so this write gains
            // the same fix SetRotationAsync gets, closing the gap for every field this store can edit.
            ShowChanged?.Invoke();
            return new ShowWriteResult.Updated(ToShow(row));
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new ShowWriteResult.SlugConflict();
        }
    }

    /// <summary>
    /// Plain DELETE (SPEC F115.1). <c>station.segment_schedule.show_id</c>'s <c>ON DELETE RESTRICT</c>
    /// (db/06, SPEC F114) already gives the database its own teeth against deleting a still-referenced
    /// show — surfaced here as SQLSTATE 23503, caught and mapped to
    /// <see cref="ShowWriteResult.Referenced"/> (PLAN T120 review F4: the mapping lives in the store,
    /// not a controller). Unlike <see cref="PersonaRepository.DeleteAsync"/>, this method does not
    /// pre-query the schedule for slot detail — naming which blocks reference it for a 409 body is
    /// PLAN T240's endpoint-layer guard (SPEC F115.4); this seam's own case only says "referenced".
    /// </summary>
    public async Task<ShowWriteResult> DeleteAsync(long id, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                "delete from station.show where id = @id",
                new { id },
                cancellationToken: ct));
            return affected == 0 ? new ShowWriteResult.NotFound() : new ShowWriteResult.Deleted();
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            return new ShowWriteResult.Referenced();
        }
    }

    /// <summary>
    /// The import write path (SPEC F118.2, F115.5, STORY-315, PLAN T254) — a single, ATOMIC
    /// CONDITIONAL upsert-by-slug (the gh-#394 conditional-write form): <c>ON CONFLICT (slug) DO
    /// UPDATE ... WHERE imported_from IS NOT NULL</c>, not <c>ThemeRepository.UpsertAsync</c>'s own
    /// unconditional <c>ON CONFLICT DO UPDATE</c> (station.theme carries no authored-vs-imported
    /// distinction to guard — every owner theme IS an import). The WHERE clause is what makes SPEC
    /// F115.5's collision gate part of THIS statement rather than a read-then-write pair around it: on
    /// a fresh slug the INSERT branch always applies (the WHERE only ever gates the CONFLICT branch);
    /// on a conflict with an existing AUTHORED row (<c>imported_from IS NULL</c>) the WHERE evaluates
    /// false, so Postgres performs NEITHER the update NOR emits a RETURNING row for it — the statement
    /// touches nothing, and the <c>QuerySingleOrDefaultAsync</c> call below reads back
    /// <see langword="null"/>, the caller's own signal to refuse. On a conflict with an existing
    /// IMPORTED row (<c>imported_from IS NOT NULL</c>) the WHERE evaluates true and the update applies
    /// normally: every authored field (name/tagline/flavor) replaced, provenance re-stamped
    /// unconditionally — a re-import always refreshes <c>imported_at</c>, mirroring
    /// <c>IPersonaImportStore.ImportAsync</c>'s own "a re-import refreshes the stamp" rule.
    /// <c>updated_at</c> advances the same way <see cref="UpdateAsync"/>'s own explicit <c>now()</c>
    /// does. Beyond this ONE gate, this method performs no other validation (mirrors
    /// <c>ThemeRepository.UpsertAsync</c>'s "pure persistence" posture otherwise) — route-slug
    /// shape/reservation and the 2× import budget ceiling already ran in <c>ShowsController.Import</c>
    /// before this is ever called.
    ///
    /// <para>
    /// <b><paramref name="rotation"/> (SPEC F152.6, PLAN T363) — "no opinion," not
    /// <see cref="SetRotationAsync"/>'s own "clear."</b> <see langword="null"/> never touches
    /// <c>envelope</c> at all — the INSERT branch leaves it at its column default (<see langword="null"/>
    /// for a brand-new row, mirroring <see cref="CreateAsync"/>'s own "stays whatever it already was"
    /// remark), and the CONFLICT branch's own <c>envelope</c> assignment is a self-reference
    /// (<c>envelope = station.show.envelope</c>) — a byte-identical no-op write, so an existing show's
    /// rotation rule (or any other dormant <c>envelope</c> key) survives a re-import that carries no
    /// rotation opinion untouched. A non-null <paramref name="rotation"/> writes it the identical
    /// <see cref="RotationEnvelopeCodec"/>/merge-via-jsonb-<c>||</c> way <see cref="SetRotationAsync"/>
    /// does, on EITHER branch — a single <c>case</c> expression keyed off whether the codec's own JSON
    /// text is <see langword="null"/> covers both "no opinion" and "write a rule" without two SQL
    /// statements or a read-then-write pair (the identical atomicity <see cref="IShowStore.ImportAsync"/>'s
    /// own remarks already promise for the rest of this statement).
    /// </para>
    /// </summary>
    public async Task<Show?> ImportAsync(
        string slug, string name, string? tagline, string? flavor, string importedFrom,
        RotationPredicate? rotation, CancellationToken ct)
    {
        var rotationJson = RotationEnvelopeCodec.ToJson(rotation);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ShowRow>(new CommandDefinition(
            $"""
            insert into station.show (name, slug, tagline, flavor, imported_from, imported_at, envelope)
            values (
              @Name, @Slug, @Tagline, @Flavor, @ImportedFrom, now(),
              case when @RotationJson::text is null then null
                   else jsonb_build_object('rotation', @RotationJson::jsonb)
              end)
            on conflict (slug) do update
              set name = @Name, tagline = @Tagline, flavor = @Flavor,
                  imported_from = @ImportedFrom, imported_at = now(), updated_at = now(),
                  envelope = case when @RotationJson::text is null then station.show.envelope
                     else coalesce(station.show.envelope, jsonb_build_object())
                       || jsonb_build_object('rotation', @RotationJson::jsonb)
                  end
              where station.show.imported_from is not null
            {ReturningColumns}
            """,
            new
            {
                Name = name, Slug = slug, Tagline = NullIfBlank(tagline), Flavor = NullIfBlank(flavor),
                ImportedFrom = importedFrom, RotationJson = rotationJson,
            },
            cancellationToken: ct));

        if (row is null) return null;

        // SPEC F152.6, PLAN T363 (the T360 review HIGH-1 fix, extended to the import path — see
        // IShowStore.ShowChanged's own remarks): raised unconditionally on every successful upsert, a
        // fresh insert as much as a re-import, since either can leave a name/tagline/flavor/rotation
        // edit an already-cached ScheduleWeekSnapshot needs to know about. Never raised on the declined
        // (null) authored-collision case above.
        ShowChanged?.Invoke();
        return ToShow(row);
    }

    /// <summary>
    /// See <see cref="IShowStore.SetRotationAsync"/>. The jsonb write itself never overwrites the
    /// whole <c>envelope</c> document (postgres-dba house rule, and SPEC F115.2's own "every other
    /// key/column stays dormant" pin depends on it): <paramref name="rotation"/> non-null MERGES a
    /// <c>{"rotation": {...}}</c> fragment in via jsonb <c>||</c>; <see langword="null"/> REMOVES just
    /// the <c>rotation</c> key via jsonb <c>-</c>. <c>coalesce(envelope, jsonb_build_object())</c>
    /// handles a still-NULL <c>envelope</c> on a show that has never carried one (every show shipped
    /// before this task) — <c>jsonb_build_object()</c> with no arguments is Postgres's own <c>{}</c>,
    /// spelled without a literal brace so it survives this method's own raw interpolated SQL string
    /// unescaped — both operators need a genuine jsonb value on their left, never a SQL NULL, to behave.
    /// </summary>
    public async Task<ShowWriteResult> SetRotationAsync(long id, RotationPredicate? rotation, CancellationToken ct)
    {
        var rotationJson = RotationEnvelopeCodec.ToJson(rotation);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ShowRow>(new CommandDefinition(
            $"""
            update station.show
            set envelope = case
                  when @RotationJson::text is null then coalesce(envelope, jsonb_build_object()) - 'rotation'
                  else coalesce(envelope, jsonb_build_object()) || jsonb_build_object('rotation', @RotationJson::jsonb)
                end,
                updated_at = now()
            where id = @Id
            {ReturningColumns}
            """,
            new { Id = id, RotationJson = rotationJson },
            cancellationToken: ct));

        if (row is null) return new ShowWriteResult.NotFound();

        // PLAN T360 review HIGH-1: CachingScheduleResolver's cached ScheduleWeekSnapshot embeds a
        // ShowSummary (with THIS Rotation) at LOAD time — that cache has no TTL and, before this
        // event, only ever dirtied on IScheduleStore.WeekChanged/SpecialsChanged. Without this line an
        // operator's rotation edit would sit invisible until an unrelated schedule/specials write, or
        // a process restart, happened to reload it — raised exactly once per successful write, never
        // on NotFound, mirroring ScheduleRepository.ReplaceWeekAsync/AssignShowAsync's own
        // WeekChanged?.Invoke() placement.
        ShowChanged?.Invoke();
        return new ShowWriteResult.Updated(ToShow(row));
    }

    /// <summary>Maps one <see cref="ShowRow"/> into the domain <see cref="Show"/>, parsing
    /// <see cref="ShowRow.RotationJson"/> via <see cref="RotationEnvelopeCodec.Parse"/> — the one
    /// mapping step every read/write method above shares, so a malformed row WARNs identically
    /// regardless of which statement produced it.</summary>
    Show ToShow(ShowRow row) => new(
        row.Id, row.Name, row.Slug, row.Tagline, row.Flavor, row.ImportedFrom, row.ImportedAt,
        row.CreatedAt, row.UpdatedAt, RotationEnvelopeCodec.Parse(row.RotationJson, row.Name, logger));

    /// <summary>
    /// SPEC F115.1's name-shape guard — pure C#, evaluated before either write method ever opens a
    /// connection. Rejects a blank/whitespace-only <c>Name</c> outright (station.show's own <c>check
    /// (length(btrim(name)) > 0)</c>, db/33, would otherwise surface as an unhandled 23514
    /// check_violation) and a <paramref name="slug"/> equal to
    /// <see cref="LegacyPersonaCardMapper.FallbackSlug"/> — REJECTED regardless of how it got there:
    /// an emoji-only name that hits Slugify's own empty-slug rescue AND a name that slugifies to
    /// <c>"persona"</c> the ordinary way (the literal name <c>"Persona"</c>, for instance — see
    /// <see cref="LegacyPersonaCardMapper.FallbackSlug"/>'s own remarks, PLAN T240 review A1) both
    /// land here. See <see cref="ShowWriteResult.InvalidName"/>'s own remarks for the
    /// REJECT-not-autocorrect rationale. <paramref name="slug"/> is passed in rather than re-derived
    /// so the two call sites (<see cref="CreateAsync"/>/<see cref="UpdateAsync"/>) compute
    /// <c>LegacyPersonaCardMapper.Slugify</c> exactly once each.
    /// </summary>
    static ShowWriteResult.InvalidName? ValidateName(ShowDraft draft, string slug) =>
        string.IsNullOrWhiteSpace(draft.Name) || slug == LegacyPersonaCardMapper.FallbackSlug
            ? new ShowWriteResult.InvalidName()
            : null;

    /// <summary>
    /// SPEC F115.1's 1× budget check — delegates to <see cref="ShowBudgets.FirstViolation"/> (the rule
    /// now lives beside its own constants in Core so T240/T244/T254 can reuse the identical check
    /// order without re-deriving it).
    /// </summary>
    static ShowWriteResult.BudgetExceeded? ValidateBudgets(ShowDraft draft) =>
        ShowBudgets.FirstViolation(draft) is { } field ? new ShowWriteResult.BudgetExceeded(field) : null;

    /// <summary>
    /// Empty/whitespace-only optional text collapses to <c>null</c> at this write seam (SPEC F115.1;
    /// <see cref="Show"/>/<see cref="ShowDraft"/>'s own docs promise <c>null</c> "when the show
    /// carries none"). An editor that clears a tagline/flavor field sends <c>""</c>, not <c>null</c> —
    /// binding that verbatim would persist an empty string forever rather than the documented absent
    /// state.
    /// </summary>
    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
