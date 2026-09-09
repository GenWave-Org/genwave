using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// <see cref="IAdBriefStore"/>'s one implementation (SPEC F159.1, F162.1, F162.2, F171.6; STORY-389,
/// STORY-392, STORY-406; PLAN T398, T403b, T432) over <c>station.ad_brief</c> —
/// connection-per-call, mirrors <see cref="AdSpotRepository"/>'s own <see cref="Lazy{T}"/>
/// data-source discipline one table over. No column here needs a raw-text/enum split the way
/// <see cref="AdSpotRow"/> does, so <see cref="AdBrief"/> (the Core-level record itself) is
/// Dapper's own projection target — no separate internal row type.
/// </summary>
sealed class AdBriefRepository(Lazy<NpgsqlDataSource> dataSource) : IAdBriefStore
{
    /// <summary>Every column <see cref="AdBrief"/> projects — one shared literal so
    /// <see cref="ListAllAsync"/>/<see cref="CreateOwnerAsync"/>/<see cref="SetEnabledAsync"/> can
    /// never drift from <see cref="UpsertAsync"/>'s own column list.</summary>
    const string Columns = "id, pack_slug, sponsor_id, premise, tone, structure, enabled, created_at";

    /// <summary>Defensive ceiling on <see cref="ListAllAsync"/>'s otherwise-unpaged read — the SAME
    /// <c>AdSpotRepository.MaxUnpagedRows</c> value, one table over: the Briefs tab is an
    /// operator-curated catalog, never expected to approach this, but the read stays bounded rather
    /// than genuinely unbounded (T403b's own YAGNI call on real paging, not a YAGNI call on a
    /// ceiling).</summary>
    const int MaxUnpagedRows = 1000;

    /// <summary>
    /// The <c>on conflict</c> update clause <see cref="UpsertAsync"/>'s own <c>ON CONFLICT (sponsor_id,
    /// premise_key)</c> upsert uses — deliberately omits <c>enabled</c> (T405 review RULING, corrects
    /// the T398-shipped shape): <c>enabled</c> is set ONLY by the INSERT half's own values list (a
    /// brand-new row), never touched again by an UPDATE — see <see cref="IAdBriefStore.UpsertAsync"/>'s
    /// own remarks for the full PRESERVE-on-conflict contract this enforces. <see cref="UpsertAllAsync"/>
    /// carries the SAME never-touch-<c>enabled</c>-on-update rule but can't share this literal —
    /// premise/tone/structure there are updated by plain <c>@parameter</c>, not Postgres' own
    /// <c>excluded.</c> pseudo-table, since that method's own existing-row match (see its remarks) is
    /// never an <c>ON CONFLICT</c> in the first place.
    /// </summary>
    const string ConflictUpdateSet = "premise = excluded.premise, tone = excluded.tone, structure = excluded.structure";

    /// <summary>
    /// <see cref="IAdBriefStore.UpsertAsync"/> — one round trip IS the check (the
    /// <c>Catalog.ArtworkTokenRepository</c>/<c>AnnouncementRepository.InsertAsync</c> lazy-upsert
    /// precedent): <c>on conflict (sponsor_id, premise_key)</c> infers <c>station.ad_brief</c>'s own
    /// <c>ad_brief_sponsor_id_premise_key</c> constraint (db/46 step 9; PLAN T432 retargets this from
    /// the pre-sponsors <c>(pack_slug, brand)</c> key) — <c>premise_key</c> is the STORED fold of
    /// <c>premise</c> (db/06), so two calls whose premise folds identical for the SAME
    /// <paramref name="sponsorId"/> collapse to one row. That constraint is a PLAIN <c>UNIQUE</c>, not
    /// <c>NULLS NOT DISTINCT</c> (db/06's own remarks on <c>ad_brief_sponsor_id_premise_key</c>): a
    /// <see langword="null"/>/blank <paramref name="premise"/> folds to a <see langword="null"/>
    /// <c>premise_key</c>, and Postgres never treats two <see langword="null"/>s as conflicting, so a
    /// SECOND angle-less call always inserts a NEW row rather than updating — SPEC F171.6's own "a
    /// NULL premise is no angle" carve-out, enforced at the constraint, not by this method.
    /// <c>created_at</c> is never in the <c>SET</c> list, so the update half leaves it untouched —
    /// and, as of the T405 review ruling, neither is <c>enabled</c> (see
    /// <see cref="ConflictUpdateSet"/>'s own remarks): <paramref name="enabled"/> only ever lands on
    /// the INSERT half's own values list, so a second call's <paramref name="enabled"/> argument is
    /// silently irrelevant to an EXISTING row — the interface's own remarks name why.
    /// </summary>
    public async Task<AdBrief> UpsertAsync(
        string? packSlug, long sponsorId, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<AdBrief>(new CommandDefinition(
            $"""
            insert into station.ad_brief (pack_slug, sponsor_id, premise, tone, structure, enabled)
            values (@packSlug, @sponsorId, @premise, @tone, @structure, @enabled)
            on conflict (sponsor_id, premise_key) do update
            set {ConflictUpdateSet}
            returning {Columns}
            """,
            new { packSlug, sponsorId, premise, tone, structure, enabled },
            cancellationToken: ct));
    }

    /// <summary>
    /// <see cref="IAdBriefStore.UpsertAllAsync"/> — ONE connection, ONE <see cref="NpgsqlTransaction"/>
    /// wrapping one <c>ON CONFLICT DO UPDATE</c> round trip per declared brief (round-3 finding R1 —
    /// replaces this method's own former read-then-write <c>existing</c> CTE, now that db/46 step 9b
    /// carries a real <c>(pack_slug, sponsor_id) WHERE pack_slug IS NOT NULL</c> partial unique index
    /// for the conflict target to name). Applied per-row rather than delete-then-reinsert — a brief's
    /// own <c>enabled</c> flag is exactly the per-row state a blanket delete-then-reinsert would
    /// destroy, the reason this method upserts each brief individually inside the shared transaction
    /// instead (the <see cref="AvatarPackRepository.UpsertAsync"/>/<see cref="FontPackRepository.UpsertAsync"/>
    /// "single-transaction multi-write install" precedent). A failure on ANY brief (the connection
    /// never reaches <see cref="NpgsqlTransaction.CommitAsync"/>) rolls back every row this call would
    /// otherwise have written — never a partially-installed pack.
    ///
    /// <para>
    /// Matches an EXISTING row on <c>(pack_slug, sponsor_id)</c> — deliberately NOT
    /// <see cref="UpsertAsync"/>'s own <c>ON CONFLICT (sponsor_id, premise_key)</c>: a pack's declared
    /// brief for one brand is ONE slot across reinstalls (T405 review F2 — "content refreshes,
    /// operator state persists" on the SAME row), and <c>premise_key</c> is <paramref name="premise"/>
    /// text folded, so keying the match on it would make an ordinary premise-copy edit look like a
    /// BRAND-NEW brief and duplicate the row instead of refreshing it — <c>ad_brief_pack_slug_sponsor_id_key</c>
    /// is what enforces this identity now, a partial index rather than a plain constraint SPECIFICALLY
    /// because an owner-authored brief (<c>pack_slug is null</c>) legitimately keeps several premises
    /// per sponsor (that's what <c>ad_brief_sponsor_id_premise_key</c> scopes), while a PACK brief caps
    /// at one row per sponsor. Every INSERT half hardcodes <c>enabled = true</c> (a brand-new pack
    /// brief is always born live, SPEC F162.2) — never a per-brief parameter, since
    /// <see cref="AdBriefUpsertInput"/> deliberately carries none (that record's own remarks); the
    /// UPDATE half never touches <c>enabled</c> at all, the SAME PRESERVE-on-conflict rule
    /// <see cref="ConflictUpdateSet"/>'s own remarks document one method over — reused verbatim here,
    /// since both methods' UPDATE half sets the identical three columns.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<AdBrief>> UpsertAllAsync(
        string packSlug, IReadOnlyList<AdBriefUpsertInput> briefs, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var results = new List<AdBrief>(briefs.Count);
        foreach (var brief in briefs)
        {
            var row = await conn.QuerySingleAsync<AdBrief>(new CommandDefinition(
                $"""
                insert into station.ad_brief (pack_slug, sponsor_id, premise, tone, structure, enabled)
                values (@packSlug, @sponsorId, @premise, @tone, @structure, true)
                on conflict (pack_slug, sponsor_id) where pack_slug is not null do update
                set {ConflictUpdateSet}
                returning {Columns}
                """,
                new { packSlug, sponsorId = brief.SponsorId, brief.Premise, brief.Tone, brief.Structure },
                transaction: tx,
                cancellationToken: ct));
            results.Add(row);
        }

        await tx.CommitAsync(ct);
        return results;
    }

    /// <summary><see cref="IAdBriefStore.SampleEnabledAsync"/> — Postgres' own <c>order by random()</c>,
    /// the SAME "let the database pick" shape <c>LibraryAdSpotSource</c>'s own live-Postgres random
    /// read uses one project over; the brief universe is small (an operator-curated catalog, not a
    /// media library), so a full-table <c>ORDER BY random()</c> costs nothing worth a more elaborate
    /// sampling scheme here.</summary>
    public async Task<AdBrief?> SampleEnabledAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AdBrief?>(new CommandDefinition(
            $"""
            select {Columns}
            from station.ad_brief
            where enabled
            order by random()
            limit 1
            """,
            cancellationToken: ct));
    }

    /// <summary><see cref="IAdBriefStore.ListAllAsync"/> — every brief, any pack/owner mix, newest
    /// created first (T403b's own YAGNI call: see the interface's own remarks for why this is a full
    /// list, never a paged one). Bounded at <see cref="MaxUnpagedRows"/> as a defensive ceiling, not a
    /// real paging mechanism — the SAME ceiling <c>AdSpotRepository</c>'s own unpaged reads already
    /// apply one table over.</summary>
    public async Task<IReadOnlyList<AdBrief>> ListAllAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AdBrief>(new CommandDefinition(
            $"""
            select {Columns}
            from station.ad_brief
            order by created_at desc, id desc
            limit @limit
            """,
            new { limit = MaxUnpagedRows },
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// <see cref="IAdBriefStore.CreateOwnerAsync"/> — <c>pack_slug</c> hardcoded <c>null</c> in the
    /// INSERT itself (never trusting a caller-supplied value the way <see cref="UpsertAsync"/> does —
    /// this member exists exactly BECAUSE a caller must never be able to silently update an existing
    /// owner brief the way <see cref="UpsertAsync"/> would), <c>on conflict ... do nothing</c> against
    /// the SAME <c>ad_brief_sponsor_id_premise_key</c> index <see cref="UpsertAsync"/> targets — a
    /// second call for the SAME <paramref name="sponsorId"/>/folded <paramref name="premise"/> pair
    /// (pack-owned or owner-authored — <c>pack_slug</c> plays no part in this constraint, PLAN T432)
    /// collides and inserts nothing; the SAME <see langword="null"/>-premise carve-out
    /// <see cref="UpsertAsync"/>'s own remarks document applies here too (an angle-less create never
    /// collides with an earlier angle-less one). <c>DO NOTHING</c> + <c>QuerySingleOrDefaultAsync</c>
    /// is the one-round-trip conflict check: a <see langword="null"/> result means the INSERT hit the
    /// conflict branch and inserted nothing, which the caller reads as "the key already holds" —
    /// <see cref="IAdBriefStore.CreateOwnerAsync"/>'s own words for this exact outcome (round-3 finding
    /// R4 — corrects this method's former "cap already holds" phrasing, which named the wrong concept:
    /// nothing here is a numeric ceiling, it's a duplicate-angle conflict).
    /// </summary>
    public async Task<AdBrief?> CreateOwnerAsync(
        long sponsorId, string? premise, string? tone, string? structure, bool enabled, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AdBrief?>(new CommandDefinition(
            $"""
            insert into station.ad_brief (pack_slug, sponsor_id, premise, tone, structure, enabled)
            values (null, @sponsorId, @premise, @tone, @structure, @enabled)
            on conflict (sponsor_id, premise_key) do nothing
            returning {Columns}
            """,
            new { sponsorId, premise, tone, structure, enabled },
            cancellationToken: ct));
    }

    /// <summary><see cref="IAdBriefStore.SetEnabledAsync"/> — a guarded, single-round-trip
    /// <c>UPDATE ... RETURNING</c> (the <c>AdSpotRepository.RunGuardedTransitionAsync</c> shape one
    /// table over, without the xmin guard — see the interface's own remarks for why a brief toggle
    /// carries no If-Match ceremony). <see langword="null"/> back means the <c>WHERE id = @id</c>
    /// matched nothing — an unknown id, the caller's own 404 signal.</summary>
    public async Task<AdBrief?> SetEnabledAsync(long id, bool enabled, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AdBrief?>(new CommandDefinition(
            $"""
            update station.ad_brief
            set enabled = @enabled
            where id = @id
            returning {Columns}
            """,
            new { id, enabled },
            cancellationToken: ct));
    }
}
