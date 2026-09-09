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
        "id, sponsor_id, sponsor_name, title, brief, script, source::text as source, pack_slug, " +
        "spot_seconds, voice_plan::text as voice_plan, bed_media_id, state::text as state, fail_reason, " +
        "media_id, generation, created_at, state_changed_at, rendered_at, retired_at, xmin::text as version, " +
        "preview_path, preview_at, preview_key, job_kind, job_started_at, job_error";

    static readonly string SelectColumns = $"select {Columns} from station.ad_spot";

    /// <summary>
    /// <see cref="IAdSpotStore.CreateAsync"/> — the C# half of the "born only into Draft/Approved/
    /// Failed" and "<c>fail_reason</c> iff Failed" invariants (see <see cref="NewAdSpot"/>'s own
    /// remarks and <see cref="IAdSpotStore"/>'s own remarks on <see cref="CreateAsync"/>); db/43's
    /// own <c>CHECK</c> constraints are the DB-level backstop for the SAME two invariants, reachable
    /// only if a future caller bypasses this method entirely. <c>sponsor_name</c> is snapshotted here,
    /// in the SAME statement, via a correlated subquery against <c>station.sponsor.name</c> (SPEC
    /// F171.7, PLAN T432) — <see cref="NewAdSpot"/> carries only <see cref="NewAdSpot.SponsorId"/>, no
    /// name, so the INSERT itself is the one place this snapshot is taken; a
    /// <paramref name="spot"/>.<see cref="NewAdSpot.SponsorId"/> naming no row fails the FK/NOT NULL
    /// pair at the DB (an unhandled exception — every caller of this store resolves a real sponsor id
    /// first, the compile-bridge's own find-or-create discipline).
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
                (sponsor_id, sponsor_name, title, brief, script, source, pack_slug, spot_seconds,
                 voice_plan, bed_media_id, state, fail_reason)
            values
                (@sponsorId, (select name from station.sponsor where id = @sponsorId), @title, @brief,
                 @script, @source::station.ad_source, @packSlug, @spotSeconds, @voicePlan::jsonb,
                 @bedMediaId, @state::station.ad_state, @failReason)
            returning {Columns}
            """,
            new
            {
                sponsorId = spot.SponsorId,
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

    /// <summary><see cref="IAdSpotStore.GetByIdAsync"/> — a plain, unguarded read; any state, any
    /// row.</summary>
    public async Task<AdSpot?> GetByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AdSpotRow>(new CommandDefinition(
            $"{SelectColumns} where id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : ToAdSpot(row);
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

    /// <summary><see cref="IAdSpotStore.RetireAsync"/> — <see cref="AdState.Ready"/>,
    /// <see cref="AdState.Draft"/>, <see cref="AdState.Approved"/>, or <see cref="AdState.Failed"/> to
    /// <see cref="AdState.Retired"/> (SPEC F159.2's as-built rider, PLAN T403 — widened from
    /// ready|draft only: see the interface's own remarks for the discard-gap ruling), xmin-guarded,
    /// stamping <c>retired_at</c>. <see cref="AdState.Rendering"/> is deliberately absent from this
    /// list — it stays undiscardable. Clears <c>fail_reason</c> as part of the SAME statement — a
    /// Failed row carries a non-null one, and db/43's own <c>ad_spot_fail_reason_iff_failed</c> CHECK
    /// demands it be null the instant <c>state</c> is no longer Failed (the <see cref="RetryAsync"/>
    /// precedent, one transition over); harmless for every other FROM state, where it is already
    /// null.</summary>
    public Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct) =>
        RunGuardedTransitionAsync(
            $"""
            update station.ad_spot
            set state = 'retired'::station.ad_state, fail_reason = null, state_changed_at = now(),
                retired_at = now()
            where id = @id
              and state in (
                'ready'::station.ad_state, 'draft'::station.ad_state,
                'approved'::station.ad_state, 'failed'::station.ad_state)
              and xmin = @expectedVersion::xid
            returning {Columns}
            """,
            new { id, expectedVersion }, id, ct);

    /// <summary><see cref="IAdSpotStore.UpdateAsync"/> — a sparse content edit, legal only against
    /// <see cref="AdState.Draft"/> or <see cref="AdState.Failed"/>, xmin-guarded. Builds its own
    /// <c>SET</c> clause from only <paramref name="edit"/>'s non-null fields (the
    /// <c>Catalog.MediaRepository.UpdateCoreAsync</c> precedent) rather than <c>COALESCE</c> — the
    /// same reason that method gives: a dynamic clause list never emits a redundant self-assignment
    /// for a column the caller left untouched. Never changes <c>state</c>/<c>state_changed_at</c>
    /// itself (this is a content edit, not a transition). A non-null <see cref="AdSpotEdit.SponsorId"/>
    /// ALWAYS adds a SECOND clause alongside <c>sponsor_id</c> — refreshing <c>sponsor_name</c> from
    /// <c>station.sponsor.name</c> in the SAME statement (SPEC F171.7, PLAN T432) — so a re-sponsored
    /// spot's snapshot never goes stale relative to the row it now points at.</summary>
    public Task<AdSpotTransitionOutcome> UpdateAsync(
        long id, AdSpotEdit edit, string expectedVersion, CancellationToken ct)
    {
        var setClauses = new List<string>();
        if (edit.SponsorId is not null)
        {
            setClauses.Add("sponsor_id = @sponsorId");
            setClauses.Add("sponsor_name = (select name from station.sponsor where id = @sponsorId)");
        }
        if (edit.Title is not null) setClauses.Add("title = @title");
        if (edit.Brief is not null) setClauses.Add("brief = @brief");
        if (edit.Script is not null) setClauses.Add("script = @script");
        if (edit.VoicePlan is not null) setClauses.Add("voice_plan = @voicePlan::jsonb");
        if (edit.SpotSeconds is not null) setClauses.Add("spot_seconds = @spotSeconds");
        if (edit.BedMediaId is not null) setClauses.Add("bed_media_id = @bedMediaId");

        // AdsController enforces "at least one field present" at the HTTP door (400) — this store
        // stays honest either way rather than trusting the caller: an empty edit still runs the
        // SAME guarded existence/state/version check via a plain SELECT, never a 0-column UPDATE
        // (Postgres itself rejects `set` with no assignments).
        var sql = setClauses.Count == 0
            ? $"""
              {SelectColumns}
              where id = @id
                and state in ('draft'::station.ad_state, 'failed'::station.ad_state)
                and xmin = @expectedVersion::xid
              """
            : $"""
              update station.ad_spot
              set {string.Join(", ", setClauses)}
              where id = @id
                and state in ('draft'::station.ad_state, 'failed'::station.ad_state)
                and xmin = @expectedVersion::xid
              returning {Columns}
              """;

        return RunGuardedTransitionAsync(
            sql,
            new
            {
                id, expectedVersion,
                sponsorId = edit.SponsorId, title = edit.Title, brief = edit.Brief, script = edit.Script,
                voicePlan = edit.VoicePlan, spotSeconds = edit.SpotSeconds, bedMediaId = edit.BedMediaId,
            },
            id, ct);
    }

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

    /// <summary>
    /// <see cref="IAdSpotStore.StampVoicePlanIfNullAsync"/>'s SQL, exposed so
    /// <c>tests/GenWave.MediaLibrary.Tests/Specs/Story402_AdSpotStampVoicePlanSql.cs</c> can assert the
    /// never-overwrite <c>coalesce</c> and the <c>rendering</c>-only guard directly (the
    /// <see cref="VoicePackRepository.DeleteGuardSql"/> precedent). <c>internal</c> —
    /// <c>InternalsVisibleTo</c> already grants <c>GenWave.MediaLibrary.Tests</c> (see this project's
    /// own .csproj). <c>static readonly</c>, not <c>const</c> — a raw-string interpolating
    /// <see cref="Columns"/> cannot itself be a compile-time constant.
    /// </summary>
    internal static readonly string StampVoicePlanSql = $"""
        update station.ad_spot
        set voice_plan = coalesce(voice_plan, @voicePlan::jsonb)
        where id = @id and state = 'rendering'::station.ad_state
        returning {Columns}
        """;

    /// <summary>
    /// <see cref="IAdSpotStore.StampVoicePlanIfNullAsync"/> — never-overwrite enforced by
    /// <see cref="StampVoicePlanSql"/>'s own <c>coalesce</c>, not merely by the caller checking
    /// <see cref="AdSpot.VoicePlan"/> first. Total: see this interface method's own remarks.
    /// </summary>
    public async Task<AdSpot?> StampVoicePlanIfNullAsync(long id, string voicePlanJson, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AdSpotRow>(new CommandDefinition(
            StampVoicePlanSql,
            new { id, voicePlan = voicePlanJson },
            cancellationToken: ct));

        return row is null ? null : ToAdSpot(row);
    }

    /// <summary>
    /// <see cref="IAdSpotStore.StampBedIfNullAsync"/>'s SQL, exposed so
    /// <c>tests/GenWave.MediaLibrary.Tests/Specs/Story403_AdSpotStampBedSql.cs</c> can assert the
    /// never-overwrite <c>coalesce</c> and the <c>rendering</c>-only guard directly — the
    /// <see cref="StampVoicePlanSql"/> precedent, one column over. <c>internal</c> —
    /// <c>InternalsVisibleTo</c> already grants <c>GenWave.MediaLibrary.Tests</c> (see this project's
    /// own .csproj). <c>static readonly</c>, not <c>const</c> — a raw-string interpolating
    /// <see cref="Columns"/> cannot itself be a compile-time constant.
    /// </summary>
    internal static readonly string StampBedSql = $"""
        update station.ad_spot
        set bed_media_id = coalesce(bed_media_id, @bedMediaId)
        where id = @id and state = 'rendering'::station.ad_state
        returning {Columns}
        """;

    /// <summary>
    /// <see cref="IAdSpotStore.StampBedIfNullAsync"/> — never-overwrite enforced by
    /// <see cref="StampBedSql"/>'s own <c>coalesce</c>, not merely by the caller checking
    /// <see cref="AdSpot.BedMediaId"/> first. Total: see this interface method's own remarks.
    /// </summary>
    public async Task<AdSpot?> StampBedIfNullAsync(long id, long bedMediaId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AdSpotRow>(new CommandDefinition(
            StampBedSql,
            new { id, bedMediaId },
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

    /// <summary><see cref="IAdSpotStore.CountStockGeneratedAsync"/> — the SPEC F159.3 stock count,
    /// draft through ready (gh-#689: the ready shelf alone left the draft pile unbounded under
    /// <c>AutoApprove=false</c>).</summary>
    public async Task<int> CountStockGeneratedAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            select count(*)::int from station.ad_spot
            where state in ('draft'::station.ad_state, 'approved'::station.ad_state,
                            'rendering'::station.ad_state, 'ready'::station.ad_state)
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
    /// <see cref="IAdSpotStore.ListAiringExclusionsAsync"/> (SPEC F171, F174; PLAN T432) — ONE query,
    /// no N+1 (the <see cref="ISponsorStore"/>-side "counts via one round trip" posture applied here):
    /// a <see cref="Domain.AdState.Ready"/> spot's <c>media_id</c> is withheld when its OWN sponsor is
    /// currently paused, or when its sponsor also owns any spot among the first
    /// <paramref name="window"/> entries of <paramref name="recentMediaIds"/> (the crosstalk/repeat-
    /// sponsor guard — looked up by joining <paramref name="recentMediaIds"/> back through
    /// <c>ad_spot.media_id</c>, never a separate rotation table). <c>IEnumerable.Take</c> on a
    /// non-positive <paramref name="window"/> yields an empty slice (documented .NET behavior, no
    /// separate clamp needed) — <c>window = 0</c> therefore excludes only paused-sponsor spots, and an
    /// empty <paramref name="recentMediaIds"/> has that same effect regardless of
    /// <paramref name="window"/>, exactly the interface's own remarks.
    /// </summary>
    public async Task<IReadOnlyList<long>> ListAiringExclusionsAsync(
        IReadOnlyList<long> recentMediaIds, int window, CancellationToken ct)
    {
        var recentWindow = recentMediaIds.Take(window).ToArray();

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            """
            select a.media_id
            from station.ad_spot a
            join station.sponsor s on s.id = a.sponsor_id
            where a.state = 'ready'::station.ad_state
              and a.media_id is not null
              and (
                s.paused
                or a.sponsor_id in (
                  select a2.sponsor_id
                  from station.ad_spot a2
                  where a2.media_id = any(@recentWindow)
                )
              )
            """,
            new { recentWindow }, cancellationToken: ct));
        return ids.AsList();
    }

    /// <summary>
    /// <see cref="IAdSpotStore.StampJobAsync"/> (SPEC F174, F175; PLAN T432) — the
    /// <see cref="RunGuardedTransitionAsync"/> "guarded UPDATE, then existence check disambiguates the
    /// zero-row result" shape, narrowed to a job claim rather than a state transition: guarded on
    /// <c>job_kind is null</c> rather than on <c>state</c>/<c>xmin</c>, and reporting
    /// <see cref="AdSpotJobStampResult.Busy"/> (row exists, already claimed) instead of
    /// <see cref="AdSpotWriteResult.Conflict"/> for the same "row exists but the guard didn't match"
    /// case. Clears <c>job_error</c> in the SAME statement — a fresh claim never inherits a previous
    /// attempt's failure text.
    /// </summary>
    public async Task<AdSpotJobStampOutcome> StampJobAsync(long id, string kind, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AdSpotRow>(new CommandDefinition(
            $"""
            update station.ad_spot
            set job_kind = @kind, job_started_at = now(), job_error = null
            where id = @id and job_kind is null
            returning {Columns}
            """,
            new { id, kind }, cancellationToken: ct));
        if (row is not null) return new AdSpotJobStampOutcome(AdSpotJobStampResult.Stamped, ToAdSpot(row));

        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from station.ad_spot where id = @id)", new { id }, cancellationToken: ct));
        return new AdSpotJobStampOutcome(
            exists ? AdSpotJobStampResult.Busy : AdSpotJobStampResult.NotFound, null);
    }

    /// <summary><see cref="IAdSpotStore.ClearJobAsync"/> (SPEC F174, F175; PLAN T432) — total by id,
    /// unconditional on the current <c>job_kind</c> (the interface's own "clearing an already-clear job
    /// is a harmless no-op" contract): the <c>WHERE</c> only tests <c>id = @id</c>, so a row with no
    /// claim in place is affected too, harmlessly re-writing the SAME null <c>job_kind</c>/
    /// <c>job_started_at</c> it already carried. Mirrors <see cref="MarkReadyAsync"/>/
    /// <see cref="MarkFailedAsync"/>'s own total posture.</summary>
    public async Task<bool> ClearJobAsync(long id, string? error, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.ad_spot
            set job_kind = null, job_started_at = null, job_error = @error
            where id = @id
            """,
            new { id, error }, cancellationToken: ct));
        return affected == 1;
    }

    /// <summary><see cref="IAdSpotStore.StampPreviewAsync"/> (SPEC F174; PLAN T432) — unconditional by
    /// id, no state guard (the interface's own remarks: a preview may be re-rendered regardless of the
    /// spot's current <see cref="Domain.AdState"/>). Mirrors <see cref="ClearJobAsync"/>'s own total
    /// shape.</summary>
    public async Task<bool> StampPreviewAsync(long id, string path, string key, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            """
            update station.ad_spot
            set preview_path = @path, preview_key = @key, preview_at = now()
            where id = @id
            """,
            new { id, path, key }, cancellationToken: ct));
        return affected == 1;
    }

    /// <summary>
    /// <see cref="IAdSpotStore.ClaimForPromotionAsync"/> (SPEC F174.5; PLAN T432) — a guarded
    /// <c>UPDATE ... RETURNING</c>, the SAME shape <see cref="ApproveAsync"/> uses one transition over,
    /// but WITHOUT <see cref="RunGuardedTransitionAsync"/>'s own existence-check disambiguation: the
    /// interface's own remarks collapse "not <see cref="Domain.AdState.Approved"/>" and "stale
    /// <paramref name="expectedVersion"/>" into one outcome on purpose (no caller needs to tell the two
    /// apart), so a zero-row result here is simply <see langword="null"/>, no second round trip.
    /// </summary>
    public async Task<AdSpot?> ClaimForPromotionAsync(long id, string expectedVersion, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AdSpotRow>(new CommandDefinition(
            $"""
            update station.ad_spot
            set state = 'rendering'::station.ad_state, state_changed_at = now()
            where id = @id and state = 'approved'::station.ad_state and xmin = @expectedVersion::xid
            returning {Columns}
            """,
            new { id, expectedVersion }, cancellationToken: ct));
        return row is null ? null : ToAdSpot(row);
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
        row.Id, row.SponsorId, row.SponsorName, row.Title, row.Brief, row.Script, ParseSource(row.Source),
        row.PackSlug, row.SpotSeconds, row.VoicePlan, row.BedMediaId, ParseState(row.State), row.FailReason,
        row.MediaId, row.Generation, row.CreatedAt, row.StateChangedAt, row.RenderedAt, row.RetiredAt,
        row.Version, row.PreviewPath, row.PreviewAt, row.PreviewKey, row.JobKind, row.JobStartedAt,
        row.JobError);

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
