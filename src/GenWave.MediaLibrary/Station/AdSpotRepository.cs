using Dapper;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// <see cref="IAdSpotStore"/>'s one implementation (SPEC F159.1, F159.2; STORY-389; PLAN T398) over
/// <c>station.ad_spot</c> — connection-per-call against a lazily-built <c>station_svc</c>
/// <see cref="NpgsqlDataSource"/>, the same "resolving must never be enough to trigger a connection
/// attempt" discipline every other station-schema store in this directory documents for its own
/// <see cref="Lazy{T}"/> constructor parameter (see <see cref="AnnouncementRepository"/>'s own
/// remarks).
///
/// <para>
/// <b>State/source stay raw text at the SQL boundary, parsed manually (the <c>RotKindTokens</c>/
/// <c>RotStateTokens</c> precedent, PLAN T377) — NOT a Dapper <c>SqlMapper.ITypeHandler</c>.</b>
/// <c>AnnouncementRepository</c>'s own <c>AnnouncementStateTypeHandler</c> is the OTHER shape this
/// codebase uses for a station-schema enum column; this store follows <c>Garden.RotFindingRepository</c>
/// instead, per PLAN T398's own design note — every read casts <c>state</c>/<c>source</c> to
/// <c>::text</c> in SQL and every write binds a plain string parameter cast back to the Postgres enum
/// (<c>@state::station.ad_state</c>), with <see cref="AdStateTokens"/>/<see cref="AdSourceTokens"/>
/// as the ONE map on the C# side.
/// </para>
/// </summary>
sealed class AdSpotRepository(Lazy<NpgsqlDataSource> dataSource) : IAdSpotStore
{
    /// <summary>
    /// The full column list, shared verbatim by every <c>SELECT</c> (via <see cref="SelectColumns"/>)
    /// and every <c>RETURNING</c> clause in this file (the <c>Garden.RotFindingRepository.FindingWithMediaSelectList</c>
    /// precedent: one definition, every caller, so the two shapes can never drift on a column). Casts
    /// <c>source</c>/<c>state</c>/<c>voice_plan</c>/<c>xmin</c> exactly the way <see cref="AdSpotRow"/>'s
    /// own remarks describe.
    /// </summary>
    const string Columns =
        "id, brand, title, brief, script, source::text as source, pack_slug, spot_seconds, " +
        "voice_plan::text as voice_plan, bed_media_id, state::text as state, fail_reason, media_id, " +
        "generation, created_at, state_changed_at, rendered_at, retired_at, xmin::text as version";

    static readonly string SelectColumns = $"select {Columns} from station.ad_spot";

    /// <summary>
    /// <see cref="IAdSpotStore.CreateAsync"/> — the C# half of the "born only into Draft/Approved/
    /// Failed" and "<c>fail_reason</c> iff Failed" invariants (see <see cref="NewAdSpot"/>'s own
    /// remarks and <see cref="IAdSpotStore"/>'s own remarks on <see cref="CreateAsync"/>); db/43's
    /// own <c>CHECK</c> constraints are the DB-level backstop for the SAME two invariants, reachable
    /// only if a future caller bypasses this method entirely.
    /// </summary>
    public async Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct)
    {
        if (spot.InitialState is not (AdState.Draft or AdState.Approved or AdState.Failed))
            throw new ArgumentOutOfRangeException(
                nameof(spot), spot.InitialState,
                "A new ad spot may only be created Draft, Approved, or Failed — every other state is reachable only via a transition on this store.");

        var failedWithNoReason = spot.InitialState == AdState.Failed && spot.FailReason is null;
        var reasonWithoutFailed = spot.InitialState != AdState.Failed && spot.FailReason is not null;
        if (failedWithNoReason || reasonWithoutFailed)
            throw new ArgumentException(
                "NewAdSpot.FailReason must be set if, and only if, InitialState is Failed.", nameof(spot));

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleAsync<AdSpotRow>(new CommandDefinition(
            $"""
            insert into station.ad_spot
                (brand, title, brief, script, source, pack_slug, spot_seconds, voice_plan, bed_media_id,
                 state, fail_reason)
            values
                (@brand, @title, @brief, @script, @source::station.ad_source, @packSlug, @spotSeconds,
                 @voicePlan::jsonb, @bedMediaId, @state::station.ad_state, @failReason)
            returning {Columns}
            """,
            new
            {
                brand = spot.Brand,
                title = spot.Title,
                brief = spot.Brief,
                script = spot.Script,
                source = AdSourceTokens.ToToken(spot.Source),
                packSlug = spot.PackSlug,
                spotSeconds = spot.SpotSeconds,
                voicePlan = spot.VoicePlan,
                bedMediaId = spot.BedMediaId,
                state = AdStateTokens.ToToken(spot.InitialState),
                failReason = spot.FailReason,
            },
            cancellationToken: ct));

        return ToAdSpot(row);
    }

    /// <summary><see cref="IAdSpotStore.ApproveAsync"/> — <see cref="AdState.Draft"/> to
    /// <see cref="AdState.Approved"/>, xmin-guarded.</summary>
    public Task<AdSpotTransitionOutcome> ApproveAsync(long id, string expectedVersion, CancellationToken ct) =>
        RunGuardedTransitionAsync(
            $"""
            update station.ad_spot
            set state = 'approved'::station.ad_state, state_changed_at = now()
            where id = @id and state = 'draft'::station.ad_state and xmin = @expectedVersion::xid
            returning {Columns}
            """,
            new { id, expectedVersion }, id, ct);

    /// <summary><see cref="IAdSpotStore.RetryAsync"/> — <see cref="AdState.Failed"/> to
    /// <see cref="AdState.Approved"/>, xmin-guarded. Clears <c>fail_reason</c> as part of the SAME
    /// statement — db/43's own <c>ad_spot_fail_reason_iff_failed</c> CHECK demands it (a retried row
    /// is no longer Failed, so a stale reason left behind would violate the "iff" the moment this
    /// UPDATE committed), and semantically the old failure no longer describes the row once it is
    /// cleared to render again.</summary>
    public Task<AdSpotTransitionOutcome> RetryAsync(long id, string expectedVersion, CancellationToken ct) =>
        RunGuardedTransitionAsync(
            $"""
            update station.ad_spot
            set state = 'approved'::station.ad_state, fail_reason = null, state_changed_at = now()
            where id = @id and state = 'failed'::station.ad_state and xmin = @expectedVersion::xid
            returning {Columns}
            """,
            new { id, expectedVersion }, id, ct);

    /// <summary><see cref="IAdSpotStore.RetireAsync"/> — <see cref="AdState.Ready"/> OR
    /// <see cref="AdState.Draft"/> to <see cref="AdState.Retired"/>, xmin-guarded, stamping
    /// <c>retired_at</c>.</summary>
    public Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct) =>
        RunGuardedTransitionAsync(
            $"""
            update station.ad_spot
            set state = 'retired'::station.ad_state, state_changed_at = now(), retired_at = now()
            where id = @id
              and state in ('ready'::station.ad_state, 'draft'::station.ad_state)
              and xmin = @expectedVersion::xid
            returning {Columns}
            """,
            new { id, expectedVersion }, id, ct);

    /// <summary>
    /// Opens its own connection, runs one xmin-guarded transition <paramref name="sql"/>, and
    /// disambiguates a zero-row result: is the row absent, or was it a stale-version/illegal-state
    /// attempt (the <c>Catalog.MediaRepository.UpdateCoreAsync</c> precedent)? Existence is checked
    /// FIRST — IDOR-safe: an unknown id always reports <see cref="AdSpotWriteResult.NotFound"/>,
    /// never a signal that would let a caller distinguish "stale version/illegal state" from
    /// "doesn't exist".
    /// </summary>
    async Task<AdSpotTransitionOutcome> RunGuardedTransitionAsync(
        string sql, object parameters, long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<AdSpotRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        if (row is not null) return new AdSpotTransitionOutcome(AdSpotWriteResult.Updated, ToAdSpot(row));

        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from station.ad_spot where id = @id)", new { id }, cancellationToken: ct));
        return new AdSpotTransitionOutcome(exists ? AdSpotWriteResult.Conflict : AdSpotWriteResult.NotFound, null);
    }

    /// <summary>
    /// <see cref="IAdSpotStore.ClaimNextApprovedAsync"/> — <see cref="AdState.Approved"/> to
    /// <see cref="AdState.Rendering"/>, one row, oldest <c>state_changed_at</c> first. The scalar
    /// subquery's own <c>FOR UPDATE SKIP LOCKED</c> is valid inside an <c>UPDATE ... WHERE id = (...)</c>
    /// (a single-table statement, unlike <c>AnnouncementRepository.ClaimOldestAsync</c>'s own
    /// multi-row CTE join, which needs table aliases to avoid a column-name collision this simpler,
    /// one-row shape never has) — two concurrent worker ticks each lock a DIFFERENT candidate row (or
    /// find none), never the same one twice.
    /// </summary>
    public async Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AdSpotRow>(new CommandDefinition(
            $"""
            update station.ad_spot
            set state = 'rendering'::station.ad_state, state_changed_at = now()
            where id = (
                select id from station.ad_spot
                where state = 'approved'::station.ad_state
                order by state_changed_at asc, id asc
                limit 1
                for update skip locked
            )
            returning {Columns}
            """,
            cancellationToken: ct));

        return row is null ? null : ToAdSpot(row);
    }

    /// <summary><see cref="IAdSpotStore.MarkReadyAsync"/> — <see cref="AdState.Rendering"/> to
    /// <see cref="AdState.Ready"/>, stamping <paramref name="mediaId"/> and <c>rendered_at</c>. Total:
    /// see this method's own interface remarks.</summary>
    public async Task<bool> MarkReadyAsync(long id, long mediaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.ad_spot
            set state = 'ready'::station.ad_state, media_id = @mediaId, rendered_at = now(),
                state_changed_at = now()
            where id = @id and state = 'rendering'::station.ad_state
            """,
            new { id, mediaId }, cancellationToken: ct));
        return affected == 1;
    }

    /// <summary><see cref="IAdSpotStore.MarkFailedAsync"/> — <see cref="AdState.Rendering"/> to
    /// <see cref="AdState.Failed"/>, stamping <paramref name="failReason"/>. Total, mirrors
    /// <see cref="MarkReadyAsync"/>'s own posture exactly.</summary>
    public async Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.ad_spot
            set state = 'failed'::station.ad_state, fail_reason = @failReason, state_changed_at = now()
            where id = @id and state = 'rendering'::station.ad_state
            """,
            new { id, failReason }, cancellationToken: ct));
        return affected == 1;
    }

    /// <summary>
    /// <see cref="IAdSpotStore.ListByStateAsync"/> — bounded, paged, with an exact total computed
    /// against the SAME state filter as the page, in one round trip (the
    /// <c>Garden.RotFindingRepository.ListFlatPageAsync</c> <c>QueryMultipleAsync</c> precedent: a
    /// genuinely separate <c>count(*)</c> statement, never a <c>count(*) over()</c> window, so a page
    /// past the last row still carries the true total). <paramref name="state"/>
    /// <see langword="null"/> omits the <c>where</c> clause entirely — every row, any state.
    /// </summary>
    public async Task<AdSpotPage> ListByStateAsync(AdState? state, int limit, int offset, CancellationToken ct)
    {
        (limit, offset) = ClampPaging(limit, offset);

        var parameters = new DynamicParameters();
        var where = "";
        if (state is not null)
        {
            where = "where state = @state::station.ad_state";
            parameters.Add("state", AdStateTokens.ToToken(state.Value));
        }

        parameters.Add("limit", limit);
        parameters.Add("offset", offset);

        var countSql = $"select count(*)::int from station.ad_spot {where}";
        var pageSql = $"""
            {SelectColumns}
            {where}
            order by state_changed_at desc, id desc
            limit @limit offset @offset
            """;

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            $"{countSql};\n{pageSql}", parameters, cancellationToken: ct));

        var total = await multi.ReadSingleAsync<int>();
        var rows = await multi.ReadAsync<AdSpotRow>();

        return new AdSpotPage(rows.Select(ToAdSpot).ToList(), total);
    }

    /// <summary><see cref="IAdSpotStore.CountReadyGeneratedAsync"/> — the SPEC F159.3 stock
    /// count.</summary>
    public async Task<int> CountReadyGeneratedAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            select count(*)::int from station.ad_spot
            where state = 'ready'::station.ad_state
              and source in ('llm'::station.ad_source, 'pack'::station.ad_source)
            """,
            cancellationToken: ct));
    }

    /// <summary><see cref="IAdSpotStore.ListReadyOlderThanAsync"/> — the SPEC F159.3 refresh
    /// candidates, owner-exempt. Bounded at <see cref="MaxUnpagedRows"/> — the same callee-enforced
    /// floor <see cref="ClampPaging"/> gives every paged read, applied here to an intentionally
    /// unpaged one (the stock pass wants every candidate in one call, but "every" still needs a
    /// ceiling against an unbounded scan).</summary>
    public async Task<IReadOnlyList<AdSpot>> ListReadyOlderThanAsync(TimeSpan age, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AdSpotRow>(new CommandDefinition(
            $"""
            {SelectColumns}
            where state = 'ready'::station.ad_state
              and source <> 'owner'::station.ad_source
              and state_changed_at < now() - @age
            order by state_changed_at asc, id asc
            limit @limit
            """,
            new { age, limit = MaxUnpagedRows }, cancellationToken: ct));
        return rows.Select(ToAdSpot).ToList();
    }

    /// <summary><see cref="IAdSpotStore.FindRenderingPastGraceAsync"/> — the stuck-render guardian's
    /// own candidate read (PLAN T402), mirrors <see cref="AnnouncementRepository.FindClaimedPastGraceAsync"/>'s
    /// exact shape one table over. Bounded at <see cref="MaxUnpagedRows"/>, the SAME ceiling
    /// <see cref="ListReadyOlderThanAsync"/> already applies to its own unpaged read.</summary>
    public async Task<IReadOnlyList<long>> FindRenderingPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            """
            select id from station.ad_spot
            where state = 'rendering'::station.ad_state and state_changed_at < @threshold
            order by state_changed_at asc, id asc
            limit @limit
            """,
            new { threshold = now - grace, limit = MaxUnpagedRows }, cancellationToken: ct));
        return ids.AsList();
    }

    /// <summary><see cref="IAdSpotStore.ReArmAsync"/> — <see cref="AdState.Rendering"/> to
    /// <see cref="AdState.Approved"/>, total, mirrors <see cref="MarkReadyAsync"/>/<see cref="MarkFailedAsync"/>'s
    /// own posture exactly (no xmin — see this store's own interface remarks).</summary>
    public async Task<bool> ReArmAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.ad_spot
            set state = 'approved'::station.ad_state, state_changed_at = now()
            where id = @id and state = 'rendering'::station.ad_state
            """,
            new { id }, cancellationToken: ct));
        return affected == 1;
    }

    /// <summary>
    /// The one ceiling every unbounded-by-caller read in this file shares — <see cref="ClampPaging"/>'s
    /// own cap and <see cref="ListReadyOlderThanAsync"/>'s own <c>limit</c> both read this constant
    /// rather than repeating the literal, so the two can never drift apart.
    /// </summary>
    const int MaxUnpagedRows = 1000;

    /// <summary>
    /// The shared paging floor (the <c>Garden.RotFindingRepository.ClampPaging</c> precedent, PLAN
    /// T377 — a THIRD copy of this exact shape would earn extracting a shared helper both files call,
    /// rather than a third hand-kept-in-sync pair): <paramref name="limit"/> to at least 1, capped at
    /// <see cref="MaxUnpagedRows"/>; <paramref name="offset"/> to at least 0 (a negative value errors
    /// in Postgres's own <c>OFFSET</c> clause rather than clamping there). Callee-enforced — never
    /// trust every caller to have already clamped.
    /// </summary>
    static (int Limit, int Offset) ClampPaging(int limit, int offset) =>
        (limit <= 0 ? 1 : Math.Min(limit, MaxUnpagedRows), Math.Max(0, offset));

    static AdSpot ToAdSpot(AdSpotRow row) => new(
        row.Id, row.Brand, row.Title, row.Brief, row.Script, ParseSource(row.Source), row.PackSlug,
        row.SpotSeconds, row.VoicePlan, row.BedMediaId, ParseState(row.State), row.FailReason,
        row.MediaId, row.Generation, row.CreatedAt, row.StateChangedAt, row.RenderedAt, row.RetiredAt,
        row.Version);

    /// <summary>A row read back from <c>station.ad_state</c> whose text does not round-trip through
    /// <see cref="AdStateTokens"/> is a data-integrity bug, not a caller error — the same throwing
    /// posture <c>Garden.RotFindingRepository.ParseKind</c> takes one seam over.</summary>
    static AdState ParseState(string state) =>
        AdStateTokens.TryParse(state, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unrecognised station.ad_state value '{state}'.");

    /// <summary>Same DB-invariant assertion as <see cref="ParseState"/>, over
    /// <see cref="AdSourceTokens"/>.</summary>
    static AdSource ParseSource(string source) =>
        AdSourceTokens.TryParse(source, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unrecognised station.ad_source value '{source}'.");
}
