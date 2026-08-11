using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IShowStore"/> (SPEC F115.1, STORY-305, PLAN T239) over
/// <c>station.show</c>. Connection-per-query against a station_svc-scoped <see cref="NpgsqlDataSource"/>
/// — mirrors <see cref="PersonaRepository"/>'s own wiring and slug-conflict-via-unique-violation
/// posture exactly (the closest existing station repository: name + house-Slugify'd slug + provenance
/// pair). Never selects/binds <c>persona_id</c>/<c>envelope</c> — SPEC F115.2's "unread this epic" law
/// — so <see cref="Show"/> has no way to carry either even by accident.
///
/// <paramref name="dataSource"/> is a <see cref="Lazy{T}"/>, the same "resolving must never be enough
/// to trigger a connection attempt" reason every other station-schema store in this file's directory
/// carries one (see <see cref="PersonaRepository"/>'s own remarks in full).
/// </summary>
sealed class ShowRepository(Lazy<NpgsqlDataSource> dataSource) : IShowStore
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

    // id is `serial` (int4) at rest — mirrors PersonaRepository's own SelectColumns comment: every id
    // in this codebase is `long` (bigint) in C#, so it is cast on the way out for a consistent,
    // single-width C# id type.
    const string SelectColumns =
        "select id::bigint as id, name, slug, tagline, flavor, imported_from, imported_at, " +
        "created_at, updated_at from station.show";

    public async Task<IReadOnlyList<Show>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<Show>(new CommandDefinition(
            $"{SelectColumns} order by name",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Show?> GetByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Show>(new CommandDefinition(
            $"{SelectColumns} where id = @id",
            new { id },
            cancellationToken: ct));
    }

    public async Task<Show?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Show>(new CommandDefinition(
            $"{SelectColumns} where slug = @slug",
            new { slug },
            cancellationToken: ct));
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
    /// "null when the show carries none" contract instead of a stray <c>''</c>.
    /// </summary>
    public async Task<ShowWriteResult> CreateAsync(ShowDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        if (ValidateName(draft, slug) is { } invalidName) return invalidName;
        if (ValidateBudgets(draft) is { } violation) return violation;

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var show = await conn.QuerySingleAsync<Show>(new CommandDefinition(
                $"""
                insert into station.show (name, slug, tagline, flavor)
                values (@Name, @Slug, @Tagline, @Flavor)
                returning id::bigint as id, name, slug, tagline, flavor, imported_from, imported_at,
                    created_at, updated_at
                """,
                new { draft.Name, Slug = slug, Tagline = NullIfBlank(draft.Tagline), Flavor = NullIfBlank(draft.Flavor) },
                cancellationToken: ct));
            return new ShowWriteResult.Created(show);
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
    /// </summary>
    public async Task<ShowWriteResult> UpdateAsync(long id, ShowDraft draft, CancellationToken ct)
    {
        var slug = LegacyPersonaCardMapper.Slugify(draft.Name);
        if (ValidateName(draft, slug) is { } invalidName) return invalidName;
        if (ValidateBudgets(draft) is { } violation) return violation;

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var show = await conn.QuerySingleOrDefaultAsync<Show>(new CommandDefinition(
                $"""
                update station.show
                set name = @Name, slug = @Slug, tagline = @Tagline, flavor = @Flavor, updated_at = now()
                where id = @Id
                returning id::bigint as id, name, slug, tagline, flavor, imported_from, imported_at,
                    created_at, updated_at
                """,
                new { draft.Name, Slug = slug, Tagline = NullIfBlank(draft.Tagline), Flavor = NullIfBlank(draft.Flavor), Id = id },
                cancellationToken: ct));
            return show is null ? new ShowWriteResult.NotFound() : new ShowWriteResult.Updated(show);
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
    /// </summary>
    public async Task<Show?> ImportAsync(string slug, string name, string? tagline, string? flavor, string importedFrom, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Show>(new CommandDefinition(
            """
            insert into station.show (name, slug, tagline, flavor, imported_from, imported_at)
            values (@Name, @Slug, @Tagline, @Flavor, @ImportedFrom, now())
            on conflict (slug) do update
              set name = @Name, tagline = @Tagline, flavor = @Flavor,
                  imported_from = @ImportedFrom, imported_at = now(), updated_at = now()
              where station.show.imported_from is not null
            returning id::bigint as id, name, slug, tagline, flavor, imported_from, imported_at,
                created_at, updated_at
            """,
            new { Name = name, Slug = slug, Tagline = NullIfBlank(tagline), Flavor = NullIfBlank(flavor), ImportedFrom = importedFrom },
            cancellationToken: ct));
    }

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
