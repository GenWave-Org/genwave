using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// <see cref="IAdBriefStore"/>'s one implementation (SPEC F159.1, F162.2; STORY-389; PLAN T398) over
/// <c>station.ad_brief</c> — connection-per-call, mirrors <see cref="AdSpotRepository"/>'s own
/// <see cref="Lazy{T}"/> data-source discipline one table over. No column here needs a raw-text/enum
/// split the way <see cref="AdSpotRow"/> does, so <see cref="AdBrief"/> (the Core-level record
/// itself) is Dapper's own projection target — no separate internal row type.
/// </summary>
sealed class AdBriefRepository(Lazy<NpgsqlDataSource> dataSource) : IAdBriefStore
{
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
            """
            insert into station.ad_brief (pack_slug, brand, premise, tone, structure, enabled)
            values (@packSlug, @brand, @premise, @tone, @structure, @enabled)
            on conflict (pack_slug, brand) do update
            set premise = excluded.premise, tone = excluded.tone, structure = excluded.structure,
                enabled = excluded.enabled
            returning id, pack_slug, brand, premise, tone, structure, enabled, created_at
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
            """
            select id, pack_slug, brand, premise, tone, structure, enabled, created_at
            from station.ad_brief
            where enabled
            order by random()
            limit 1
            """,
            cancellationToken: ct));
    }
}
