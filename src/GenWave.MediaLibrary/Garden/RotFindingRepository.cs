using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// <see cref="IRotFindingStore"/>'s one implementation (SPEC F153.1-F153.3, F153.9; STORY-374,
/// STORY-375; PLAN T372, gh-#529) over <c>library.rot_finding</c> — connection-per-call against the
/// library's own <see cref="NpgsqlDataSource"/>, the same shape <see cref="MediaRotationRepository"/>/
/// <see cref="MediaThumbRepository"/> already establish one seam over.
///
/// <para>
/// <b>Reconcile is set-based, in one transaction, per postgres-dba rule 7</b> (F153.2's "as built at
/// T372" amendment, ORCHESTRATOR ruling): every reconcile method here is exactly two statements —
/// (1) an <c>insert … select … where &lt;predicate&gt; on conflict (media_id, kind) do update</c>
/// that opens a fresh finding or re-opens a <see cref="RotState.Resolved"/> one (a
/// <see cref="RotState.Dismissed"/> row is untouched by the <c>WHERE</c> gate on the conflict
/// action; an already-<see cref="RotState.Open"/> row has its evidence refreshed but keeps its own
/// <c>opened_at</c> — only a genuine resolved→open transition stamps a fresh one), then (2) an
/// <c>update … set state = 'resolved', resolved_at = now() where state = 'open' and not exists
/// (&lt;predicate&gt;)</c> that resolves whatever no longer matches. Both statements share the
/// SAME predicate text and run inside ONE <see cref="NpgsqlTransaction"/> — never a per-row C# loop.
/// </para>
/// </summary>
sealed class RotFindingRepository(NpgsqlDataSource dataSource) : IRotFindingStore
{
    /// <summary>
    /// T372 review MED-2: the dead_file predicate text, shared verbatim by the open/re-open half
    /// and the resolve half's own <c>not exists (...)</c> — the <c>MediaRepository.PlayablePredicate</c>
    /// idiom, one constant interpolated everywhere the SAME logical condition is checked so the two
    /// halves can never drift apart.
    /// </summary>
    const string DeadFilePredicate =
        "(m.state = 'failed' or (m.state = 'unavailable' and m.unavailable_since < now() - @grace))";

    /// <summary>
    /// <see cref="RotKind.DeadFile"/> (SPEC F153.3, T372's own state-based half — the push-guard
    /// report reason, <c>push_missing</c>, is T373): a row is dead when <c>library.media.state =
    /// 'failed'</c>, or when it is <c>'unavailable'</c> and has stayed that way past <paramref
    /// name="unavailableGrace"/>. Evidence names which half fired and since when — <c>failed</c>
    /// rows have no <c>unavailable_since</c> stamp, so <c>coalesce(unavailable_since, enriched_at,
    /// discovered_at)</c> falls back to the best "since" this codebase actually tracks for them
    /// (ORCHESTRATOR ruling).
    /// </summary>
    public async Task ReconcileDeadFilesAsync(TimeSpan unavailableGrace, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("grace", unavailableGrace);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            insert into library.rot_finding (media_id, kind, state, evidence, opened_at, updated_at)
            select
                m.id,
                'dead_file'::library.rot_kind,
                'open'::library.rot_state,
                jsonb_build_object(
                    'reason', case when m.state = 'failed' then 'failed' else 'unavailable' end,
                    'since', coalesce(m.unavailable_since, m.enriched_at, m.discovered_at)
                ),
                now(),
                now()
            from library.media m
            where {DeadFilePredicate}
            on conflict (media_id, kind) do update
              set state       = 'open',
                  evidence    = excluded.evidence,
                  opened_at   = case when library.rot_finding.state = 'resolved'
                                     then excluded.opened_at
                                     else library.rot_finding.opened_at end,
                  resolved_at = null,
                  updated_at  = now()
              where library.rot_finding.state <> 'dismissed'
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            $"""
            update library.rot_finding f
            set state = 'resolved', resolved_at = now(), updated_at = now()
            where f.kind = 'dead_file'::library.rot_kind
              and f.state = 'open'
              and not exists (
                  select 1
                  from library.media m
                  where m.id = f.media_id
                    and {DeadFilePredicate}
              )
            """,
            parameters,
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// STORY-374 AC4: an <c>open</c> row moves to <c>dismissed</c>; anything else (unknown id,
    /// already <c>dismissed</c>, or currently <c>resolved</c>) matches zero rows and returns
    /// <see langword="false"/> — a plain conditional <c>UPDATE</c>, no read-then-write TOCTOU gap.
    /// </summary>
    public async Task<bool> DismissAsync(long findingId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            """
            update library.rot_finding
            set state = 'dismissed', dismissed_at = now(), updated_at = now()
            where id = @findingId and state = 'open'
            """,
            new { findingId },
            cancellationToken: ct));

        return rows > 0;
    }

    /// <summary>
    /// Bounded, paged (T372 review LOW-2; T377 pages it further for the admin surface, SPEC F153.9)
    /// — conditional predicates appended only when the matching filter is supplied, the same
    /// <c>MediaRotationRepository.AppendSafeExclusion</c>-style short-circuit rather than a
    /// nullable-cast comparison in SQL. <c>evidence::text</c> keeps the jsonb column opaque to
    /// Dapper (the <c>FontPack.Definition</c> precedent) — this Core-level record never parses it.
    /// <c>order by opened_at desc</c> is covered by the <c>(kind, state, opened_at desc)</c> index
    /// (db/41) rather than a sequential scan over the forever-growing table.
    /// </summary>
    public async Task<IReadOnlyList<RotFinding>> ListAsync(
        RotKind? kind, RotState? state, CancellationToken ct, int limit = 200, int offset = 0)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (kind is not null)
        {
            conditions.Add("kind = @kind::library.rot_kind");
            parameters.Add("kind", ToKindText(kind.Value));
        }

        if (state is not null)
        {
            conditions.Add("state = @state::library.rot_state");
            parameters.Add("state", ToStateText(state.Value));
        }

        parameters.Add("limit", limit);
        parameters.Add("offset", offset);

        var where = conditions.Count > 0 ? "where " + string.Join(" and ", conditions) : "";

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RotFindingRow>(new CommandDefinition(
            $"""
            select id, media_id, kind::text as kind, state::text as state, group_key,
                   evidence::text as evidence, opened_at, resolved_at, dismissed_at, updated_at
            from library.rot_finding
            {where}
            order by opened_at desc
            limit @limit offset @offset
            """,
            parameters,
            cancellationToken: ct));

        return rows.Select(ToFinding).ToList();
    }

    /// <summary>
    /// SPEC F153.9's <c>GET /api/status</c> Gardener tile (T377): one grouped count over
    /// <c>state = 'open'</c>. A kind with no open findings is simply absent from the returned
    /// dictionary rather than present with a zero.
    /// </summary>
    public async Task<IReadOnlyDictionary<RotKind, int>> CountOpenByKindAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RotFindingKindCountRow>(new CommandDefinition(
            """
            select kind::text as kind, count(*)::int as count
            from library.rot_finding
            where state = 'open'
            group by kind
            """,
            cancellationToken: ct));

        return rows.ToDictionary(r => ParseKind(r.Kind), r => r.Count);
    }

    static RotFinding ToFinding(RotFindingRow row) => new(
        row.Id, row.MediaId, ParseKind(row.Kind), ParseState(row.State), row.GroupKey, row.Evidence,
        row.OpenedAt, row.ResolvedAt, row.DismissedAt, row.UpdatedAt);

    static string ToKindText(RotKind kind) => kind switch
    {
        RotKind.DeadFile => "dead_file",
        RotKind.NearDuplicate => "near_duplicate",
        RotKind.StaleMetadata => "stale_metadata",
        RotKind.ShelfDust => "shelf_dust",
        RotKind.Unreachable => "unreachable",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped RotKind."),
    };

    static RotKind ParseKind(string kind) => kind switch
    {
        "dead_file" => RotKind.DeadFile,
        "near_duplicate" => RotKind.NearDuplicate,
        "stale_metadata" => RotKind.StaleMetadata,
        "shelf_dust" => RotKind.ShelfDust,
        "unreachable" => RotKind.Unreachable,
        _ => throw new InvalidOperationException($"Unrecognised library.rot_kind value '{kind}'."),
    };

    static string ToStateText(RotState state) => state switch
    {
        RotState.Open => "open",
        RotState.Dismissed => "dismissed",
        RotState.Resolved => "resolved",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped RotState."),
    };

    static RotState ParseState(string state) => state switch
    {
        "open" => RotState.Open,
        "dismissed" => RotState.Dismissed,
        "resolved" => RotState.Resolved,
        _ => throw new InvalidOperationException($"Unrecognised library.rot_state value '{state}'."),
    };
}
