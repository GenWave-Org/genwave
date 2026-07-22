using System.Text.Json;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IPersonaTasteAccrualStore"/> (SPEC F84.1-F84.6;
/// STORY-215, PLAN T70). Unlike <see cref="PersonaTasteRepository"/> — connection-per-query —
/// <see cref="ThumbAsync"/> opens ONE connection and runs the ENTIRE thumb (booth-log attribution
/// read, the idempotency-ledger insert, the read-modify-write nudge, and the cap-50 eviction sweep)
/// inside ONE <see cref="NpgsqlTransaction"/> — the same "dedicated accrual method owning one
/// connection/transaction" shape <see cref="PersonaImportRepository"/> already established for
/// <c>PersonaImportRepository.ImportAsync</c> (PLAN T67).
///
/// <para>
/// CARRIED T59 REVIEW NOTE, closed here: <see cref="PersonaTasteRepository.ReplaceAsync"/>'s own
/// two-statement UPDATE-then-INSERT upsert is not concurrency-safe on its own — two concurrent
/// thumbs for the same persona could both read the same "current weight" and race each other's
/// write. This method never calls <c>ReplaceAsync</c>. Instead, right after resolving attribution,
/// it takes <c>pg_advisory_xact_lock(personaId)</c> — released automatically at commit or rollback,
/// no separate unlock call — which serializes every nudge/eviction for that ONE persona for the
/// duration of this transaction. A second, unique-index-based upsert (F84.1's own review-note
/// alternative) is not viable here for the same reason <c>PersonaTasteRepository.ReplaceAsync</c>'s
/// own remarks give: jsonb has no default btree opclass usable as an <c>ON CONFLICT</c> target.
/// </para>
///
/// <para>
/// F84.5 idempotency (persona, airing, direction) is a durable ledger row
/// (<c>station.persona_taste_thumb</c>), not an in-memory set — an in-memory de-dup would forget on
/// every process restart, and would never protect two concurrent requests racing the same triple
/// the way a real unique constraint plus <c>ON CONFLICT DO NOTHING</c> does.
/// </para>
/// </summary>
sealed class PersonaTasteAccrualRepository(Lazy<NpgsqlDataSource> dataSource) : IPersonaTasteAccrualStore
{
    // SPEC F84.1 — the nudge step and clamp bounds. `real` column width (see PersonaTasteRepository's
    // own remarks) — kept as float end-to-end here so the SQL-side clamp arithmetic and the C#-side
    // parameters agree exactly; only the outcome widens to double, matching TasteRule.Weight's own
    // domain-facing type.
    const float Step = 0.2f;
    const float MinWeight = -1.0f;
    const float MaxWeight = 1.0f;

    // SPEC F84.3 — accrued-rule cap per persona. Authored/operator rows are exempt BY CONSTRUCTION:
    // every query below that touches the cap is scoped to `source = 'accrued'`, so there is no second
    // WHERE clause anywhere else that could drift out of sync with this exemption.
    const int Cap = 50;

    const string TrackStartedKind = "track-started";

    static readonly TasteContext NoGate = new([], null, null);

    /// <summary>Ephemeral Dapper projection of the one booth-log row a thumb attributes against (F84.1, F84.6).</summary>
    sealed record BoothLogAttribution(string Kind, long? PersonaId, string? Artist);

    public async Task<TasteThumbOutcome> ThumbAsync(long boothLogId, TasteThumbDirection direction, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<BoothLogAttribution>(new CommandDefinition(
            """
            select kind, persona_id::bigint as persona_id, artist
            from station.booth_log
            where id = @BoothLogId
            """,
            new { BoothLogId = boothLogId },
            transaction: tx,
            cancellationToken: ct));

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return new TasteThumbOutcome.RowNotFound();
        }

        // F84.6 — un-thumbable: not a track-start row, no persona stamped at air time, or no known
        // artist to attribute a rule to (all three are the row's OWN stamped state; nothing here ever
        // asks "who is active now").
        if (row.Kind != TrackStartedKind || row.PersonaId is not long personaId || string.IsNullOrWhiteSpace(row.Artist))
        {
            await tx.RollbackAsync(ct);
            return new TasteThumbOutcome.NotThumbable();
        }

        // Serialize every nudge/eviction FOR THIS PERSONA (CARRIED T59 REVIEW NOTE — see class remarks).
        await conn.ExecuteAsync(new CommandDefinition(
            "select pg_advisory_xact_lock(@PersonaId)",
            new { PersonaId = personaId },
            transaction: tx,
            cancellationToken: ct));

        // F84.5 idempotency: (persona, airing, direction) is the natural key. `ON CONFLICT DO
        // NOTHING` is the ENTIRE dedup mechanism — a second tap for the same triple, from either
        // surface, never reaches the nudge below.
        var ledgerId = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            """
            insert into station.persona_taste_thumb (persona_id, booth_log_id, direction)
            values (@PersonaId, @BoothLogId, @Direction)
            on conflict (persona_id, booth_log_id, direction) do nothing
            returning id::bigint
            """,
            new { PersonaId = personaId, BoothLogId = boothLogId, Direction = ToDirectionText(direction) },
            transaction: tx,
            cancellationToken: ct));

        if (ledgerId is null)
        {
            // Nothing else was written this call — the ledger insert was the only statement in
            // flight, and it affected zero rows.
            await tx.CommitAsync(ct);
            return new TasteThumbOutcome.AlreadyRecorded(personaId);
        }

        var predicate = JsonSerializer.Serialize(new TastePredicate(row.Artist, Genre: null, Tag: null), PersonaCardSerializer.Options);
        var context = JsonSerializer.Serialize(NoGate, PersonaCardSerializer.Options);
        var step = direction == TasteThumbDirection.Up ? Step : -Step;

        // Two-statement upsert-by-identity, same shape as PersonaTasteRepository.ReplaceAsync (jsonb
        // has no ON CONFLICT target) — safe here specifically BECAUSE the advisory lock above already
        // serializes every reader/writer of this persona's rows for the rest of this transaction.
        var updatedWeight = await conn.QuerySingleOrDefaultAsync<float?>(new CommandDefinition(
            """
            update station.persona_taste
            set weight = greatest(@MinWeight, least(@MaxWeight, weight + @Step)), updated_at = now()
            where persona_id = @PersonaId and source = 'accrued'
              and predicate = @Predicate::jsonb and context = @Context::jsonb
            returning weight
            """,
            new { PersonaId = personaId, Step = step, MinWeight, MaxWeight, Predicate = predicate, Context = context },
            transaction: tx,
            cancellationToken: ct));

        float weight;
        if (updatedWeight is float existing)
        {
            weight = existing;
        }
        else
        {
            weight = await conn.QuerySingleAsync<float>(new CommandDefinition(
                """
                insert into station.persona_taste (persona_id, predicate, context, weight, source)
                values (@PersonaId, @Predicate::jsonb, @Context::jsonb, greatest(@MinWeight, least(@MaxWeight, @Step)), 'accrued')
                returning weight
                """,
                new { PersonaId = personaId, Predicate = predicate, Context = context, Step = step, MinWeight, MaxWeight },
                transaction: tx,
                cancellationToken: ct));
        }

        // F84.3 — cap-50-weakest-evicted, IN THE SAME TRANSACTION as the nudge above. A no-op unless
        // this persona's accrued row count just crossed the cap.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            delete from station.persona_taste
            where id in (
              select id from station.persona_taste
              where persona_id = @PersonaId and source = 'accrued'
              order by abs(weight) desc, created_at desc
              offset @Cap
            )
            """,
            new { PersonaId = personaId, Cap },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new TasteThumbOutcome.Nudged(personaId, weight);
    }

    static string ToDirectionText(TasteThumbDirection direction) => direction switch
    {
        TasteThumbDirection.Up => "up",
        TasteThumbDirection.Down => "down",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "unknown taste thumb direction"),
    };
}
