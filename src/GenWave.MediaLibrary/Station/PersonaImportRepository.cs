using System.Text.Json;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IPersonaImportStore"/> (SPEC F79.3, F79.6; STORY-209,
/// PLAN T67). Unlike <see cref="PersonaRepository"/>/<see cref="PersonaMemoryRepository"/>/
/// <see cref="PersonaTasteRepository"/> — each connection-per-query — <see cref="ImportAsync"/> opens
/// ONE connection and runs the ENTIRE import (persona upsert-by-slug, then the authored-row replace
/// of both <c>persona_memory</c> and <c>persona_taste</c>) inside ONE <see cref="NpgsqlTransaction"/>:
/// F79.6's "a rejected import changes nothing, a mid-import failure rolls back everything" is exactly
/// what a single transaction gives for free, with no ambient-transaction plumbing
/// (<c>System.Transactions.TransactionScope</c>) smeared across three otherwise-unrelated seams. Any
/// exception this method does not itself catch propagates out of the <c>await using</c> transaction
/// scope, which rolls back on <see cref="IAsyncDisposable.DisposeAsync"/> without a commit ever
/// having run — the only exception caught here is the one this method can actually turn into a
/// meaningful outcome (a name collision).
/// </summary>
sealed class PersonaImportRepository(Lazy<NpgsqlDataSource> dataSource) : IPersonaImportStore
{
    // Postgres SQLSTATE for unique_violation — mirrors PersonaRepository's own NameConflict mapping.
    const string UniqueViolation = "23505";

    public async Task<PersonaImportOutcome> ImportAsync(PersonaImportRequest request, CancellationToken ct)
    {
        var definition = PersonaCardSerializer.Serialize(request.Card);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var (personaId, wasCreated) = await UpsertPersonaAsync(conn, tx, request, definition, ct);
            await ReplaceAuthoredMemoryAsync(conn, tx, personaId, request.Card.Lore, ct);
            await ReplaceAuthoredTasteAsync(conn, tx, personaId, request.Card.Taste ?? [], ct);

            await tx.CommitAsync(ct);
            return new PersonaImportOutcome.Imported(personaId, wasCreated);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            return new PersonaImportOutcome.NameConflict();
        }
    }

    /// <summary>
    /// Looks up the target slug WITHIN the transaction (never a pre-check outside it — the same
    /// no-TOCTOU discipline <see cref="PersonaRepository.CreateAsync"/> follows by letting the insert
    /// itself be the uniqueness check) and either updates the existing row or inserts a new one.
    /// <c>backstory</c>/<c>style</c> are reset to <c>""</c> on both paths: an imported persona's
    /// narrative lives entirely in <see cref="PersonaCard.Soul"/>, never split back into the legacy
    /// two-field shape — <see cref="PersonaRepository.UpdateAsync"/>'s own edit-wipe guard already
    /// keeps a later admin PATCH from clobbering this <c>definition.soul</c> with an empty rebuild
    /// (it preserves the existing soul whenever the freshly rebuilt one would be empty).
    /// </summary>
    static async Task<(long PersonaId, bool WasCreated)> UpsertPersonaAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PersonaImportRequest request, string definition, CancellationToken ct)
    {
        var existingId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "select id::bigint from station.persona where slug = @Slug",
            new { request.Slug },
            transaction: tx,
            cancellationToken: ct));

        if (existingId is long id)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                update station.persona
                set name = @Name, backstory = '', style = '', voice = @Voice,
                    definition = @Definition::jsonb, updated_at = now()
                where id = @Id
                """,
                new { request.Card.Name, Voice = request.LegacyVoice, Definition = definition, Id = id },
                transaction: tx,
                cancellationToken: ct));

            return (id, false);
        }

        var newId = await conn.QuerySingleAsync<long>(new CommandDefinition(
            """
            insert into station.persona (name, backstory, style, voice, slug, definition, enabled)
            values (@Name, '', '', @Voice, @Slug, @Definition::jsonb, true)
            returning id::bigint
            """,
            new { request.Card.Name, Voice = request.LegacyVoice, request.Slug, Definition = definition },
            transaction: tx,
            cancellationToken: ct));

        return (newId, true);
    }

    /// <summary>
    /// Delete-then-insert scoped to <c>source = 'authored'</c> ONLY (SPEC F79.3): every accrued
    /// memory row for this persona is untouched by construction — there is no bare
    /// "delete all for this persona" statement anywhere in this method for a future edit to regress
    /// into. Imported lore carries no per-entry <c>kind</c> of its own (the card's <c>lore[]</c> is a
    /// flat list), so every row lands as <c>kind = 'lore'</c> — distinct from the accrual write
    /// path's own <c>"bit"</c>/<c>"callback"</c> kinds, and not yet read back by
    /// <see cref="Abstractions.IPersonaMemory.RecallAsync"/>'s kind-scoped windows (that wiring is a
    /// later task; this only has to round-trip through <see cref="Abstractions.IPersonaMemory.ListAsync"/>
    /// for a future re-export).
    /// </summary>
    static async Task ReplaceAuthoredMemoryAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long personaId, IReadOnlyList<string> lore, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.persona_memory where persona_id = @PersonaId and source = 'authored'",
            new { PersonaId = personaId },
            transaction: tx,
            cancellationToken: ct));

        foreach (var content in lore)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into station.persona_memory (persona_id, kind, content, source)
                values (@PersonaId, 'lore', @Content, 'authored')
                """,
                new { PersonaId = personaId, Content = content },
                transaction: tx,
                cancellationToken: ct));
        }
    }

    /// <summary>
    /// Same delete-then-insert-authored-only shape as <see cref="ReplaceAuthoredMemoryAsync"/>, over
    /// <c>station.persona_taste</c> — every accrued (and operator) row survives untouched. Predicate/
    /// context serialize through <see cref="PersonaCardSerializer.Options"/>, matching
    /// <see cref="PersonaTasteRepository"/>'s own convention exactly (the one canonical JSON shape for
    /// these two types, never a second differently-configured serializer).
    /// </summary>
    static async Task ReplaceAuthoredTasteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long personaId, IReadOnlyList<TasteRule> taste, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.persona_taste where persona_id = @PersonaId and source = 'authored'",
            new { PersonaId = personaId },
            transaction: tx,
            cancellationToken: ct));

        foreach (var rule in taste)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into station.persona_taste (persona_id, predicate, context, weight, source)
                values (@PersonaId, @Predicate::jsonb, @Context::jsonb, @Weight, 'authored')
                """,
                new
                {
                    PersonaId = personaId,
                    Predicate = JsonSerializer.Serialize(rule.Predicate, PersonaCardSerializer.Options),
                    Context = JsonSerializer.Serialize(rule.Context, PersonaCardSerializer.Options),
                    Weight = (float)rule.Weight,
                },
                transaction: tx,
                cancellationToken: ct));
        }
    }
}
