using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// <see cref="ISponsorStore"/>'s one implementation (SPEC F171, F172; STORY-406, STORY-407,
/// STORY-410; PLAN T432) over <c>station.sponsor</c> — connection-per-call, mirrors
/// <see cref="AdSpotRepository"/>'s own <see cref="Lazy{T}"/> data-source discipline one table over.
/// Single-row reads/writes bind straight to <see cref="Sponsor"/> (the <see cref="AdBriefRepository"/>
/// precedent — no raw-text/enum split on this table, so the Core record is Dapper's own projection
/// target); <see cref="ListAsync"/> alone needs <see cref="SponsorRow"/>'s extra join columns.
/// </summary>
sealed class SponsorRepository(Lazy<NpgsqlDataSource> dataSource) : ISponsorStore
{
    /// <summary>Every column <see cref="Sponsor"/> projects — one shared literal so every read/write
    /// method can never drift apart on column order.</summary>
    const string Columns =
        "id, name, pack_slug, tagline, about, phone, address, website, tone, paused, paused_at, " +
        "created_at, updated_at, xmin::text as version";

    /// <summary>Same projection as <see cref="Columns"/>, table-qualified to the <c>s</c> alias —
    /// Postgres refuses an unqualified system-column reference (<c>xmin</c>) once the query contains
    /// a JOIN, even a LEFT JOIN against a derived table (verified live: "column xmin does not exist
    /// ... HINT: To reference that column, you must use a table-qualified name"). Only
    /// <see cref="ListAsync"/>'s own query joins; every other <see cref="Columns"/> call site is a
    /// single-table statement where the bare names already resolve. DERIVED from
    /// <see cref="Columns"/> (round-3 finding R4) — every column name in <see cref="Columns"/> is
    /// comma-separated with no other comma-space anywhere in the literal, so prefixing each one with
    /// <c>s.</c> is a single string replace; the two literals can never drift apart on column
    /// order.</summary>
    static readonly string ListColumns = "s." + Columns.Replace(", ", ", s.");

    const string UniqueViolation = "23505";
    const string CheckViolation = "23514";

    /// <summary>db/46's own <c>UNIQUE NULLS NOT DISTINCT (pack_slug, name_key)</c> constraint —
    /// caught as SQLSTATE 23505 and mapped to <see cref="SponsorWriteResult.NameTaken"/>.</summary>
    const string NameTakenConstraint = "sponsor_pack_slug_name_key";

    /// <summary>Defensive ceiling on <see cref="ListAsync"/>'s otherwise-unpaged read — the SAME
    /// <c>AdBriefRepository.MaxUnpagedRows</c>/<c>AdSpotRepository.MaxUnpagedRows</c> value, one table
    /// over: the sponsor universe is operator-curated, never expected to approach this, but the read
    /// stays bounded rather than genuinely unbounded.</summary>
    const int MaxUnpagedRows = 1000;

    /// <summary><see cref="ISponsorStore.ListAsync"/> — TWO round trips, not N+1 (SPEC F172): the
    /// first reads every matching sponsor plus its brief/show counts via left-joined per-table
    /// subqueries; the second reads every matching sponsor's own ad-spot counts grouped by state, in
    /// one query keyed on the ids the first query already returned. <paramref name="q"/>, when
    /// non-null, is LIKE-escaped (<c>%</c>/<c>_</c>/<c>\</c>) before being folded and wrapped in
    /// wildcards — a literal <c>%</c> or <c>_</c> typed by an operator searches for that literal
    /// character, never a wildcard.</summary>
    public async Task<IReadOnlyList<SponsorListRow>> ListAsync(string? q, CancellationToken ct)
    {
        var escapedQ = q is null ? null : EscapeLikeMetacharacters(q);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var sponsorRows = (await conn.QueryAsync<SponsorRow>(new CommandDefinition(
            $"""
            select {ListColumns},
                   coalesce(b.briefs, 0)::int as briefs,
                   coalesce(sh.shows, 0)::int as shows
            from station.sponsor s
            left join (
                select sponsor_id, count(*) as briefs from station.ad_brief group by sponsor_id
            ) b on b.sponsor_id = s.id
            left join (
                select sponsor_id, count(*) as shows from station.show group by sponsor_id
            ) sh on sh.sponsor_id = s.id
            where @q::text is null
               or s.name_key like '%' || station.sponsor_fold(@q) || '%' escape '\'
            order by s.name
            limit @limit
            """,
            new { q = escapedQ, limit = MaxUnpagedRows },
            cancellationToken: ct))).AsList();

        if (sponsorRows.Count == 0) return Array.Empty<SponsorListRow>();

        var ids = sponsorRows.Select(r => r.Id).ToArray();
        var stateCounts = (await conn.QueryAsync<SponsorSpotStateCountRow>(new CommandDefinition(
            """
            select sponsor_id, state::text as state, count(*)::int as count
            from station.ad_spot
            where sponsor_id = any(@ids)
            group by sponsor_id, state
            """,
            new { ids }, cancellationToken: ct))).AsList();

        var spotsBySponsor = stateCounts
            .GroupBy(r => r.SponsorId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<AdState, int>)g.ToDictionary(r => ParseState(r.State), r => r.Count));

        return sponsorRows
            .Select(r => new SponsorListRow(
                ToSponsor(r),
                r.Briefs,
                spotsBySponsor.TryGetValue(r.Id, out var spots) ? spots : EmptySpotCounts,
                r.Shows))
            .ToList();
    }

    static readonly IReadOnlyDictionary<AdState, int> EmptySpotCounts =
        new Dictionary<AdState, int>();

    /// <summary><see cref="ISponsorStore.GetAsync"/> — single-row lookup, <see langword="null"/> for
    /// an unknown id.</summary>
    public async Task<Sponsor?> GetAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Sponsor?>(new CommandDefinition(
            $"select {Columns} from station.sponsor where id = @id", new { id }, cancellationToken: ct));
    }

    /// <summary><see cref="ISponsorStore.CreateOwnerAsync"/> — <c>pack_slug</c> hardcoded
    /// <see langword="null"/> in the INSERT. Catches SQLSTATE 23505 on
    /// <see cref="NameTakenConstraint"/> as <see cref="SponsorWriteResult.NameTaken"/>, and SQLSTATE
    /// 23514 (a db/46 <c>CHECK</c>) as <see cref="SponsorWriteResult.InvalidField"/>.</summary>
    public async Task<SponsorWriteResult> CreateOwnerAsync(NewSponsor sponsor, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var row = await conn.QuerySingleAsync<Sponsor>(new CommandDefinition(
                $"""
                insert into station.sponsor (name, pack_slug, tagline, about, phone, address, website, tone)
                values (@name, null, @tagline, @about, @phone, @address, @website, @tone)
                returning {Columns}
                """,
                new
                {
                    sponsor.Name,
                    Tagline = NullIfBlank(sponsor.Tagline),
                    About = NullIfBlank(sponsor.About),
                    Phone = NullIfBlank(sponsor.Phone),
                    Address = NullIfBlank(sponsor.Address),
                    Website = NullIfBlank(sponsor.Website),
                    Tone = NullIfBlank(sponsor.Tone),
                },
                cancellationToken: ct));
            return new SponsorWriteResult.Ok(row);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == NameTakenConstraint)
        {
            return new SponsorWriteResult.NameTaken();
        }
        catch (PostgresException ex) when (ex.SqlState == CheckViolation)
        {
            return new SponsorWriteResult.InvalidField(FieldNameFromCheckConstraint(ex));
        }
    }

    /// <summary><see cref="ISponsorStore.UpsertPackSponsorsAsync"/> (SPEC F171.1/F2/F3; round-3 finding
    /// R1) — ONE round trip computes every input name's fold via <c>unnest</c>; two DIFFERENT names
    /// sharing a fold refuse the WHOLE batch as <see cref="SponsorPackWriteResult.NamesCollide"/>
    /// before any row is written. Otherwise every non-colliding (<paramref name="packSlug"/>, folded
    /// name) pair is upserted in a SECOND round trip: a single <c>INSERT ... SELECT FROM unnest(...)
    /// ON CONFLICT DO UPDATE</c> statement, atomic on its own since it is one statement. Because the
    /// conflict target is <see cref="NameTakenConstraint"/> (<c>pack_slug</c>, <c>name_key</c>), a
    /// repeat call carrying the SAME name is a no-op re-write of the existing row; a fold-PRESERVING
    /// spelling change (casing, whitespace) re-labels that same row; a genuine RENAME — a different
    /// fold — inserts a SECOND row instead, since nothing in the manifest ties the old row to the new
    /// name.</summary>
    public async Task<SponsorPackWriteResult> UpsertPackSponsorsAsync(
        string packSlug, IReadOnlyList<string> names, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var folds = (await conn.QueryAsync<SponsorNameFoldRow>(new CommandDefinition(
            "select n as name, station.sponsor_fold(n) as folded from unnest(@names::text[]) as n",
            new { names = names.ToArray() }, cancellationToken: ct))).AsList();

        var firstNameByFold = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in folds)
        {
            if (firstNameByFold.TryGetValue(row.Folded, out var earlier)
                && !string.Equals(earlier, row.Name, StringComparison.Ordinal))
            {
                return new SponsorPackWriteResult.NamesCollide(First: earlier, Second: row.Name);
            }
            firstNameByFold[row.Folded] = row.Name;
        }

        try
        {
            var upserted = (await conn.QueryAsync<Sponsor>(new CommandDefinition(
                $"""
                insert into station.sponsor (name, pack_slug)
                select n, @packSlug from unnest(@distinctNames::text[]) as n
                on conflict on constraint {NameTakenConstraint} do update
                set name = excluded.name, updated_at = now()
                returning {Columns}
                """,
                new { distinctNames = firstNameByFold.Values.ToArray(), packSlug },
                cancellationToken: ct))).AsList();

            var byName = upserted.ToDictionary(s => s.Name, StringComparer.Ordinal);
            return new SponsorPackWriteResult.Ok(names.Select(n => byName[n]).ToList());
        }
        catch (PostgresException ex) when (ex.SqlState == CheckViolation)
        {
            return new SponsorPackWriteResult.InvalidField(FieldNameFromCheckConstraint(ex));
        }
    }

    /// <summary><see cref="ISponsorStore.FindOrCreateOwnerAsync"/> — exact lookup by folded name in
    /// the owner namespace (<c>pack_slug is null</c>) first; a miss inserts, and a concurrent insert
    /// race on <see cref="NameTakenConstraint"/> (two callers resolving the same brand-new name at
    /// once) is resolved by re-selecting the winner rather than surfacing the race as an
    /// error.</summary>
    public async Task<SponsorWriteResult> FindOrCreateOwnerAsync(string name, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var existing = await conn.QuerySingleOrDefaultAsync<Sponsor?>(new CommandDefinition(
            $"select {Columns} from station.sponsor where pack_slug is null and name_key = station.sponsor_fold(@name)",
            new { name }, cancellationToken: ct));
        if (existing is not null) return new SponsorWriteResult.Ok(existing);

        try
        {
            var inserted = await conn.QuerySingleAsync<Sponsor>(new CommandDefinition(
                $"""
                insert into station.sponsor (name, pack_slug)
                values (@name, null)
                returning {Columns}
                """,
                new { name }, cancellationToken: ct));
            return new SponsorWriteResult.Ok(inserted);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == NameTakenConstraint)
        {
            var winner = await conn.QuerySingleAsync<Sponsor>(new CommandDefinition(
                $"select {Columns} from station.sponsor where pack_slug is null and name_key = station.sponsor_fold(@name)",
                new { name }, cancellationToken: ct));
            return new SponsorWriteResult.Ok(winner);
        }
        catch (PostgresException ex) when (ex.SqlState == CheckViolation)
        {
            return new SponsorWriteResult.InvalidField(FieldNameFromCheckConstraint(ex));
        }
    }

    /// <summary><see cref="ISponsorStore.UpdateAsync"/> — a transaction wrapping a row-locking read
    /// (existence + <see cref="Sponsor.PackSlug"/>, for the <see cref="SponsorWriteResult.NamePackOwned"/>
    /// guard) followed by an xmin-guarded <c>UPDATE ... RETURNING</c> (the
    /// <c>AdSpotRepository.UpdateAsync</c> sparse-SET-clause shape one table over). Every early return
    /// below relies on <c>await using var tx</c> to roll back on dispose — Npgsql only ever commits a
    /// transaction on an explicit <see cref="Npgsql.NpgsqlTransaction.CommitAsync"/>, so leaving the
    /// scope any other way already rolls back.</summary>
    public async Task<SponsorWriteResult> UpdateAsync(
        long id, SponsorEdit edit, string expectedVersion, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = await conn.QuerySingleOrDefaultAsync<Sponsor?>(new CommandDefinition(
            $"select {Columns} from station.sponsor where id = @id for update",
            new { id }, transaction: tx, cancellationToken: ct));

        if (existing is null)
        {
            return new SponsorWriteResult.NotFound();
        }

        if (edit.Name is not null && existing.PackSlug is not null)
        {
            return new SponsorWriteResult.NamePackOwned();
        }

        var setClauses = new List<string> { "updated_at = now()" };
        if (edit.Name is not null) setClauses.Add("name = @name");
        if (edit.Tagline is not null) setClauses.Add("tagline = @tagline");
        if (edit.About is not null) setClauses.Add("about = @about");
        if (edit.Phone is not null) setClauses.Add("phone = @phone");
        if (edit.Address is not null) setClauses.Add("address = @address");
        if (edit.Website is not null) setClauses.Add("website = @website");
        if (edit.Tone is not null) setClauses.Add("tone = @tone");

        try
        {
            var row = await conn.QuerySingleOrDefaultAsync<Sponsor?>(new CommandDefinition(
                $"""
                update station.sponsor
                set {string.Join(", ", setClauses)}
                where id = @id and xmin = @expectedVersion::xid
                returning {Columns}
                """,
                new
                {
                    id,
                    expectedVersion,
                    name = edit.Name,
                    tagline = NullIfBlank(edit.Tagline),
                    about = NullIfBlank(edit.About),
                    phone = NullIfBlank(edit.Phone),
                    address = NullIfBlank(edit.Address),
                    website = NullIfBlank(edit.Website),
                    tone = NullIfBlank(edit.Tone),
                },
                transaction: tx, cancellationToken: ct));

            if (row is null)
            {
                return new SponsorWriteResult.VersionConflict();
            }

            await tx.CommitAsync(ct);
            return new SponsorWriteResult.Ok(row);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == NameTakenConstraint)
        {
            return new SponsorWriteResult.NameTaken();
        }
        catch (PostgresException ex) when (ex.SqlState == CheckViolation)
        {
            return new SponsorWriteResult.InvalidField(FieldNameFromCheckConstraint(ex));
        }
    }

    /// <summary><see cref="ISponsorStore.SetPausedAsync"/> — idempotent: <c>coalesce(paused_at,
    /// now())</c> means a repeat pause leaves the ORIGINAL <c>paused_at</c> untouched, never restamping
    /// it; a resume always clears it back to <see langword="null"/> regardless of its prior
    /// value.</summary>
    public async Task<Sponsor?> SetPausedAsync(long id, bool paused, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<Sponsor?>(new CommandDefinition(
            $"""
            update station.sponsor
            set paused = @paused,
                paused_at = case when @paused then coalesce(paused_at, now()) else null end,
                updated_at = now()
            where id = @id
            returning {Columns}
            """,
            new { id, paused }, cancellationToken: ct));
    }

    /// <summary><see cref="ISponsorStore.DeleteIfUnreferencedAsync"/> — reads the exact reference
    /// counts and up to ten referencing titles BEFORE attempting the delete (the
    /// <c>PersonaWriteResult.ScheduledElsewhere</c> pre-query precedent, applied across three tables
    /// instead of two): a positive count on any of the three refuses the delete outright as
    /// <see cref="SponsorDeleteResult.InUse"/>, without ever reaching the DELETE statement. The DELETE
    /// itself still carries a backstop for the race where a referencing row lands between the read and
    /// the delete — every FK is <c>ON DELETE RESTRICT</c> (db/46), so that race surfaces as SQLSTATE
    /// 23503, caught here and re-reported as a fresh <see cref="SponsorDeleteResult.InUse"/> read.
    /// </summary>
    public async Task<SponsorDeleteResult> DeleteIfUnreferencedAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from station.sponsor where id = @id)",
            new { id }, cancellationToken: ct));
        if (!exists) return new SponsorDeleteResult.NotFound();

        var refs = await ReadReferencesAsync(conn, id, ct);
        if (refs.Counts.Briefs > 0 || refs.Counts.Spots > 0 || refs.Counts.Shows > 0)
            return new SponsorDeleteResult.InUse(refs.Counts.Briefs, refs.Counts.Spots, refs.Counts.Shows, refs.Titles);

        try
        {
            var affected = await conn.ExecuteAsync(new CommandDefinition(
                "delete from station.sponsor where id = @id", new { id }, cancellationToken: ct));
            return affected == 0
                ? new SponsorDeleteResult.NotFound()
                : new SponsorDeleteResult.Deleted();
        }
        catch (PostgresException ex) when (ex.SqlState == "23503")
        {
            var raced = await ReadReferencesAsync(conn, id, ct);
            return new SponsorDeleteResult.InUse(
                raced.Counts.Briefs, raced.Counts.Spots, raced.Counts.Shows, raced.Titles);
        }
    }

    static async Task<(SponsorReferenceCountsRow Counts, IReadOnlyList<string> Titles)> ReadReferencesAsync(
        NpgsqlConnection conn, long id, CancellationToken ct)
    {
        await using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            select
              (select count(*)::int from station.ad_brief where sponsor_id = @id) as briefs,
              (select count(*)::int from station.ad_spot where sponsor_id = @id) as spots,
              (select count(*)::int from station.show where sponsor_id = @id) as shows;

            select title from (
                select premise as title, created_at, 0 as source_order
                from station.ad_brief where sponsor_id = @id
                union all
                select title, created_at, 1 from station.ad_spot where sponsor_id = @id
                union all
                select name, created_at, 2 from station.show where sponsor_id = @id
            ) refs
            where title is not null
            order by created_at, source_order
            limit 10;
            """,
            new { id }, cancellationToken: ct));

        var counts = await multi.ReadSingleAsync<SponsorReferenceCountsRow>();
        var titles = (await multi.ReadAsync<string>()).AsList();
        return (counts, titles);
    }

    /// <summary>Escapes LIKE metacharacters (<c>\</c> first, then <c>%</c>/<c>_</c>) so a literal
    /// search term never behaves as a wildcard pattern once wrapped and matched with
    /// <c>escape '\'</c>.</summary>
    static string EscapeLikeMetacharacters(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Parses a db/46 <c>CHECK</c> violation's own constraint name
    /// (<c>sponsor_&lt;field&gt;_check</c> — Postgres's own default naming for an unnamed column-level
    /// CHECK) back into the field it guards. A constraint name that doesn't fit this shape means db/46
    /// grew a CHECK this parser doesn't know about — that is a real bug to surface, not a caller
    /// error, so it rethrows <paramref name="ex"/> wrapped with the constraint name rather than
    /// silently reporting an "unknown" field (round-3 finding R4).</summary>
    static string FieldNameFromCheckConstraint(PostgresException ex)
    {
        const string prefix = "sponsor_";
        const string suffix = "_check";
        var constraintName = ex.ConstraintName;
        if (constraintName is not null
            && constraintName.StartsWith(prefix, StringComparison.Ordinal)
            && constraintName.EndsWith(suffix, StringComparison.Ordinal)
            && constraintName.Length > prefix.Length + suffix.Length)
        {
            return constraintName[prefix.Length..^suffix.Length];
        }
        throw new InvalidOperationException(
            $"Unrecognised station.sponsor CHECK constraint '{constraintName}' — expected the shape " +
            $"'{prefix}<field>{suffix}'.", ex);
    }

    static Sponsor ToSponsor(SponsorRow row) => new(
        row.Id, row.Name, row.PackSlug, row.Tagline, row.About, row.Phone, row.Address, row.Website,
        row.Tone, row.Paused, row.PausedAt, row.CreatedAt, row.UpdatedAt, row.Version);

    /// <summary>Same DB-invariant assertion as <c>AdSpotRepository.ParseState</c>, over
    /// <see cref="AdStateTokens"/> — a row read back from <c>station.ad_state</c> whose text does not
    /// round-trip is a data-integrity bug, not a caller error.</summary>
    static AdState ParseState(string state) =>
        AdStateTokens.TryParse(state, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unrecognised station.ad_state value '{state}'.");
}
