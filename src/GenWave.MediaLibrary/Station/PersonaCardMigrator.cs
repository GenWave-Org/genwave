using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// One-shot, idempotent boot migration reconciling <c>station.persona</c> onto the F71.1 card schema
/// (STORY-192, SPEC F71.2). Two steps, both re-run every boot:
///
/// <list type="number">
/// <item>Every row still at the <c>definition = '{}'::jsonb</c> sentinel (schema-migration default —
/// see db/06, db/11) gets a real card built from its own <c>name/backstory/style/voice</c> via
/// <see cref="LegacyPersonaCardMapper"/> — "Nova Q must survive" (the reconciliation this task
/// promises for any persona created before this ships).</item>
/// <item>A dedicated <c>slug = "default"</c> row is ensured, sourced from the station's current DJ
/// config — the currently active persona, resolved through <see cref="IActivePersonaAccessor"/> (the
/// same live seam <c>LlmCopyWriter</c> reads for on-air prompts) — or a neutral empty-soul card when
/// none is active. Always a fresh row (never a mutation of an existing admin-managed persona's own
/// slug), so today's named personas keep their own identity untouched.</item>
/// </list>
///
/// Mirrors <c>GenWave.Host.Seeding.SafeLoopSeeder</c>'s resilience shape one project up: both steps
/// are self-idempotent by construction (the sentinel definition marks "not yet reconciled"; the
/// default row's existence is checked by slug before any insert), so no separate marker table is
/// needed. Any failure degrades to a WARN and the host starts normally — the next boot retries.
/// </summary>
public sealed class PersonaCardMigrator(
    NpgsqlDataSource dataSource,
    IActivePersonaAccessor activePersonaAccessor,
    ILogger<PersonaCardMigrator> logger)
{
    /// <summary>The reserved slug F71.2's startup migration ensures always exists.</summary>
    public const string DefaultSlug = "default";

    // Postgres SQLSTATE for unique_violation — mirrors PersonaRepository's own mapping.
    const string UniqueViolation = "23505";

    // How many times EnsureDefaultPersonaAsync retries with a disambiguated legacy `name` before
    // giving up (surfacing as a WARN + next-boot retry, same as any other failure here). The legacy
    // `name` column is irrelevant to the card itself — only real if an operator's own persona happens
    // to be literally named "Default".
    const int MaxDefaultNameAttempts = 3;

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await ReconcileLegacyRowsAsync(conn, ct);
            await EnsureDefaultPersonaAsync(conn, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Persona card migration failed — host starting normally, will retry next boot");
        }
    }

    async Task ReconcileLegacyRowsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<Persona>(new CommandDefinition(
            """
            select id::bigint as id, name, backstory, style, voice, created_at, updated_at
            from station.persona
            where definition = '{}'::jsonb
            """,
            cancellationToken: ct))).ToList();

        foreach (var row in rows)
        {
            var json = PersonaCardSerializer.Serialize(
                LegacyPersonaCardMapper.BuildCard(row.Name, row.Backstory, row.Style, row.Voice));

            await conn.ExecuteAsync(new CommandDefinition(
                "update station.persona set definition = @json::jsonb where id = @id",
                new { json, id = row.Id },
                cancellationToken: ct));
        }

        if (rows.Count > 0)
        {
            logger.LogInformation(
                "Persona card migration: reconciled {Count} legacy persona row(s) into card definitions",
                rows.Count);
        }
    }

    async Task EnsureDefaultPersonaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from station.persona where slug = @slug)",
            new { slug = DefaultSlug },
            cancellationToken: ct));
        if (exists)
            return;

        // F71.2: "existing single-DJ personality config" is whatever persona is currently active —
        // the same live read LlmCopyWriter's prompt composition uses. None active degrades to a
        // neutral card (empty soul), matching today's persona-less prompt exactly (zero quirks/lore
        // either way, so the "zero prompt change" guarantee holds regardless).
        var active = await activePersonaAccessor.ResolveAsync(ct);
        var json = PersonaCardSerializer.Serialize(LegacyPersonaCardMapper.BuildCard(
            name: active?.Name ?? "Default",
            backstory: active?.Backstory ?? "",
            style: active?.Style ?? "",
            voice: active?.Voice ?? ""));

        var legacyName = "Default";
        for (var attempt = 1; attempt <= MaxDefaultNameAttempts; attempt++)
        {
            try
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into station.persona (name, slug, definition, enabled)
                    values (@legacyName, @slug, @json::jsonb, true)
                    """,
                    new { legacyName, slug = DefaultSlug, json },
                    cancellationToken: ct));

                logger.LogInformation(
                    "Persona card migration: created default persona row (active persona: {ActiveName})",
                    active?.Name ?? "none");
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == UniqueViolation && attempt < MaxDefaultNameAttempts)
            {
                // The legacy `name` column (irrelevant to the card itself) collided with an
                // operator-created persona coincidentally named "Default" — disambiguate and retry.
                // The slug ('default') and the card's own definition.name are unaffected by this.
                legacyName = $"Default ({attempt + 1})";
            }
        }
    }
}
