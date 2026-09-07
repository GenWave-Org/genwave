using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IVoicePackStore"/> (SPEC F164.5/F164.6/F166.4,
/// STORY-395/396/398/401, PLAN T413) over <c>station.voice_pack</c>(+<c>_voice</c>). Copied from
/// <see cref="FontPackRepository"/>'s own shape: one connection + one <see cref="NpgsqlTransaction"/>
/// for the whole upsert (pack row, then delete-then-reinsert-all of its voices), connection-per-query
/// everywhere else. <paramref name="dataSource"/> is a <see cref="Lazy{T}"/> — merely resolving
/// <see cref="IVoicePackStore"/> from DI must never be enough to trigger a connection attempt against
/// an empty/dev-mode <c>ConnectionStrings:Station</c>.
/// </summary>
sealed class VoicePackRepository(Lazy<NpgsqlDataSource> dataSource, ILogger<VoicePackRepository> logger) : IVoicePackStore
{
    // Postgres SQLSTATE for unique_violation — mirrors FontPackRepository's own 23505 mapping.
    const string UniqueViolation = "23505";

    // Every ad_spot state a live voice_plan can still be acted on from (db/42's own station.ad_state
    // enum) — 'failed'/'retired' spots are dead ends a pack uninstall must never be blocked by.
    const string ActiveAdSpotStates = "'draft', 'approved', 'rendering', 'ready'";

    /// <summary>
    /// The single DELETE statement STORY-401's "delete IS the guard" contract depends on (SPEC
    /// F164.6) — a field, not an inline literal inside <see cref="DeleteAsync"/>, so this IS the ONE
    /// place the guard text lives: a mutation that weakens the guard (e.g. swapping it for an
    /// advisory pre-<c>SELECT count(*)</c> plus an unguarded DELETE) has no second copy of the SQL to
    /// leave untouched, and can only pass by mutating this exact field — which
    /// <c>tests/GenWave.MediaLibrary.Tests/Specs/Story401_VoicePackDeleteGuardSql.cs</c> asserts on
    /// directly (T413 review round 1 finding F3), independent of any HTTP arc's own
    /// insert-then-delete call ordering. <c>internal</c> — <c>InternalsVisibleTo</c> already grants
    /// <c>GenWave.MediaLibrary.Tests</c> (see this project's own .csproj).
    /// </summary>
    internal static readonly string DeleteGuardSql = $"""
        delete from station.voice_pack vp
        where vp.slug = @slug
          and not exists (
            select 1 from station.voice_pack_voice v
            join station.ad_spot s on s.state in ({ActiveAdSpotStates})
            join lateral jsonb_array_elements(coalesce(s.voice_plan, '[]'::jsonb)) e on e->>'voiceId' = v.voice_id
            where v.pack_id = vp.id)
          and not exists (
            select 1 from station.voice_pack_voice v
            join station.persona p on p.voice = v.voice_id and p.voice <> ''
            where v.pack_id = vp.id)
        returning vp.id
        """;

    public async Task<IReadOnlyList<VoicePackVoiceOwner>> FindInstalledVoiceIdsAsync(
        IReadOnlyList<string> voiceIds, string excludingSlug, CancellationToken ct)
    {
        if (voiceIds.Count == 0)
            return [];

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<VoicePackVoiceOwner>(new CommandDefinition(
            """
            select v.voice_id as "VoiceId", vp.slug as "OwnerSlug"
            from station.voice_pack_voice v
            join station.voice_pack vp on vp.id = v.pack_id
            where v.voice_id = any(@VoiceIds) and vp.slug <> @ExcludingSlug
            """,
            new { VoiceIds = voiceIds.ToArray(), ExcludingSlug = excludingSlug },
            cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<IReadOnlyList<string>> FindVoiceIdsForPackAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            """
            select v.voice_id
            from station.voice_pack_voice v
            join station.voice_pack vp on vp.id = v.pack_id
            where vp.slug = @slug
            """,
            new { slug },
            cancellationToken: ct));

        return rows.ToList();
    }

    /// <summary>
    /// Single-transaction upsert (SPEC F164.5 "Data model") — see <see cref="IVoicePackStore.UpsertAsync"/>'s
    /// own remarks. <b>THE 23505 MAPPING</b> mirrors <c>FontPackRepository.UpsertAsync</c>'s own: this
    /// single transaction means a mid-upsert unique-violation has ALREADY rolled back everything (the
    /// pack row insert/update included) by the time the <see langword="catch"/> below runs — never a
    /// partial pack.
    /// </summary>
    public async Task<VoicePackUpsertResult> UpsertAsync(
        string slug, string engine, string definition, string importedFrom,
        IReadOnlyList<VoicePackVoiceInput> voices, CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var packId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                insert into station.voice_pack (slug, engine, definition, imported_from, imported_at)
                values (@Slug, @Engine, @Definition::jsonb, @ImportedFrom, now())
                on conflict (slug) do update
                  set engine = @Engine,
                      definition = @Definition::jsonb,
                      imported_from = @ImportedFrom,
                      imported_at = now()
                returning id
                """,
                new { Slug = slug, Engine = engine, Definition = definition, ImportedFrom = importedFrom },
                transaction: tx,
                cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "delete from station.voice_pack_voice where pack_id = @PackId",
                new { PackId = packId },
                transaction: tx,
                cancellationToken: ct));

            foreach (var voice in voices)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into station.voice_pack_voice (pack_id, voice_id, file, gender_hint, age_hint, preview_sha)
                    values (@PackId, @VoiceId, @File, @GenderHint, @AgeHint, @PreviewSha)
                    """,
                    new { PackId = packId, voice.VoiceId, voice.File, voice.GenderHint, voice.AgeHint, voice.PreviewSha },
                    transaction: tx,
                    cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
            return new VoicePackUpsertResult.Upserted();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return await ResolveVoiceIdCollisionAsync(slug, voices, ex, ct);
        }
    }

    /// <summary>
    /// Names the actual colliding voice id and its owning pack slug for a 23505
    /// <see cref="UpsertAsync"/> just caught — mirrors <c>FontPackRepository.ResolveFileCollisionAsync</c>
    /// verbatim, re-using <see cref="FindInstalledVoiceIdsAsync"/> (a fresh, post-failure read) rather
    /// than trusting anything about the caught exception's own detail text (F15.7).
    /// </summary>
    async Task<VoicePackUpsertResult> ResolveVoiceIdCollisionAsync(
        string slug, IReadOnlyList<VoicePackVoiceInput> voices, PostgresException ex, CancellationToken ct)
    {
        logger.LogWarning(ex,
            "Voice pack install {Slug} refused: a voice id collided with an already-installed pack (23505 on voice_pack_voice_voice_id_uk)",
            LogSanitize.Strip(slug));

        var candidateIds = voices.Select(v => v.VoiceId).ToList();
        var owners = await FindInstalledVoiceIdsAsync(candidateIds, slug, ct);
        var ownerSlugByVoiceId = owners.ToDictionary(o => o.VoiceId, o => o.OwnerSlug, StringComparer.Ordinal);

        foreach (var voice in voices)
        {
            if (ownerSlugByVoiceId.TryGetValue(voice.VoiceId, out var ownerSlug))
                return new VoicePackUpsertResult.VoiceIdCollision(voice.VoiceId, ownerSlug);
        }

        // The lookup above should always resolve (a colliding id can only ever belong to a DIFFERENT,
        // already-installed pack) — this is a defensive fallback for the unexpected case where it does
        // not, never a silently swallowed failure.
        return new VoicePackUpsertResult.VoiceIdCollision(null, null);
    }

    /// <summary>
    /// SPEC F164.6, STORY-401 — see <see cref="IVoicePackStore.DeleteAsync"/>'s own remarks for the
    /// "delete IS the guard" contract this fulfils. The pack's own voice file names are read FIRST, in
    /// the same transaction, because <c>ON DELETE CASCADE</c> removes <c>station.voice_pack_voice</c>
    /// rows the instant the delete below succeeds — there is no reading them back afterward. The DELETE
    /// itself is ONE statement: both <c>not exists (...)</c> clauses are evaluated as part of the SAME
    /// statement that removes the row, so there is no separate round trip between "is it referenced" and
    /// "remove it" for a concurrent write to land inside.
    /// </summary>
    public async Task<VoicePackDeleteResult> DeleteAsync(string slug, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var files = (await conn.QueryAsync<string>(new CommandDefinition(
            """
            select vpv.file
            from station.voice_pack_voice vpv
            join station.voice_pack vp on vp.id = vpv.pack_id
            where vp.slug = @slug
            """,
            new { slug },
            transaction: tx,
            cancellationToken: ct))).ToList();

        var deletedId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            DeleteGuardSql,
            new { slug },
            transaction: tx,
            cancellationToken: ct));

        if (deletedId is not null)
        {
            await tx.CommitAsync(ct);
            return new VoicePackDeleteResult.Deleted(files);
        }

        // Either no such pack exists, or it does but the DELETE above refused it (referenced) — the
        // affected-row-count alone cannot distinguish the two, so both are re-queried here.
        var packId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "select id from station.voice_pack where slug = @slug",
            new { slug },
            transaction: tx,
            cancellationToken: ct));

        if (packId is null)
        {
            await tx.CommitAsync(ct);
            return new VoicePackDeleteResult.NotFound();
        }

        // Re-run AFTER the fact, purely to NAME the offenders for the 409 body — the DELETE's own
        // refusal above is already correct regardless of what this finds.
        var adSpotIds = (await conn.QueryAsync<long>(new CommandDefinition(
            $"""
            select distinct s.id
            from station.voice_pack_voice v
            join station.ad_spot s on s.state in ({ActiveAdSpotStates})
            join lateral jsonb_array_elements(coalesce(s.voice_plan, '[]'::jsonb)) e on e->>'voiceId' = v.voice_id
            where v.pack_id = @packId
            order by s.id
            """,
            new { packId },
            transaction: tx,
            cancellationToken: ct))).ToList();

        var personaNames = (await conn.QueryAsync<string>(new CommandDefinition(
            """
            select distinct p.name collate "C" as name
            from station.voice_pack_voice v
            join station.persona p on p.voice = v.voice_id and p.voice <> ''
            where v.pack_id = @packId
            order by name
            """,
            new { packId },
            transaction: tx,
            cancellationToken: ct))).ToList();

        await tx.CommitAsync(ct);
        return new VoicePackDeleteResult.Referenced(adSpotIds, personaNames);
    }
}
