using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// <see cref="IAdBriefStore"/>'s one implementation (SPEC F159.1, F162.1, F162.2; STORY-389,
/// STORY-392; PLAN T398, T403b) over <c>station.ad_brief</c> — connection-per-call, mirrors
/// <see cref="AdSpotRepository"/>'s own <see cref="Lazy{T}"/> data-source discipline one table over.
/// No column here needs a raw-text/enum split the way <see cref="AdSpotRow"/> does, so
/// <see cref="AdBrief"/> (the Core-level record itself) is Dapper's own projection target — no
/// separate internal row type.
/// </summary>
sealed class AdBriefRepository(Lazy<NpgsqlDataSource> dataSource) : IAdBriefStore
{
    /// <summary>Every column <see cref="AdBrief"/> projects — one shared literal so
    /// <see cref="ListAllAsync"/>/<see cref="CreateOwnerAsync"/>/<see cref="SetEnabledAsync"/> can
    /// never drift from <see cref="UpsertAsync"/>'s own column list.</summary>
    const string Columns = "id, pack_slug, brand, premise, tone, structure, enabled, created_at";

    /// <summary>Defensive ceiling on <see cref="ListAllAsync"/>'s otherwise-unpaged read — the SAME
    /// <c>AdSpotRepository.MaxUnpagedRows</c> value, one table over: the Briefs tab is an
    /// operator-curated catalog, never expected to approach this, but the read stays bounded rather
    /// than genuinely unbounded (T403b's own YAGNI call on real paging, not a YAGNI call on a
    /// ceiling).</summary>
    const int MaxUnpagedRows = 1000;

    /// <summary>
    /// <see cref="IAdBriefStore.UpsertAsync"/> — one round trip IS the check (the
    /// <c>Catalog.ArtworkTokenRepository</c>/<c>AnnouncementRepository.InsertAsync</c> lazy-upsert
    /// precedent): <c>on conflict (pack_slug, brand)</c> infers <c>station.ad_brief</c>'s own
    /// <c>ad_brief_pack_slug_brand_key</c> constraint (<c>UNIQUE NULLS NOT DISTINCT</c> — inference
    /// works the same regardless of that modifier), so a second call for the SAME
    /// <c>(pack_slug, brand)</c> pair — including two owner-authored calls for the same brand, both
    /// carrying a NULL <c>pack_slug</c> — updates the existing row in place rather than raising
    /// 23505 or forking a duplicate. <c>created_at</c> is never in the <c>SET</c> list, so the update
    /// half leaves it untouched.
    /// </summary>
    public async Task<AdBrief> UpsertAsync(
        string? packSlug, string brand, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<AdBrief>(new CommandDefinition(
            $"""
            insert into station.ad_brief (pack_slug, brand, premise, tone, structure, enabled)
            values (@packSlug, @brand, @premise, @tone, @structure, @enabled)
            on conflict (pack_slug, brand) do update
            set premise = excluded.premise, tone = excluded.tone, structure = excluded.structure,
                enabled = excluded.enabled
            returning {Columns}
            """,
            new { packSlug, brand, premise, tone, structure, enabled },
            cancellationToken: ct));
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
    /// the SAME <c>ad_brief_pack_slug_brand_key</c> index <see cref="UpsertAsync"/> targets — since
    /// every row this method ever inserts carries a NULL <c>pack_slug</c>, the NULLS-NOT-DISTINCT
    /// unique index can only ever collide with an EXISTING owner brief for the same
    /// <paramref name="brand"/> (a pack brief's own non-null, distinct <c>pack_slug</c> never
    /// collides) — exactly the cap PLAN T403b/SPEC F159.1's rider ratifies, and exactly the
    /// coexistence <c>Story389_AdSpotLifecycleStore.AnOwnerBriefAndAPackBriefForTheSameBrandAreTwoSeparateRows</c>
    /// already pins at the constraint level. <c>DO NOTHING</c> + <c>QuerySingleOrDefaultAsync</c> is
    /// the one-round-trip conflict check: a <see langword="null"/> result means the INSERT hit the
    /// conflict branch and inserted nothing, which the caller reads as "cap already holds".
    /// </summary>
    public async Task<AdBrief?> CreateOwnerAsync(
        string brand, string? premise, string? tone, string? structure, bool enabled, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AdBrief?>(new CommandDefinition(
            $"""
            insert into station.ad_brief (pack_slug, brand, premise, tone, structure, enabled)
            values (null, @brand, @premise, @tone, @structure, @enabled)
            on conflict (pack_slug, brand) do nothing
            returning {Columns}
            """,
            new { brand, premise, tone, structure, enabled },
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
