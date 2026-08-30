using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// <see cref="IThumbStore"/>'s one implementation (SPEC F150.1, F150.7, F150.9; STORY-371,
/// STORY-369; PLAN T365, gh-#529) over <c>library.media_thumb</c> + <c>library.media_rotation</c> —
/// the SAME "one connection, one transaction, own the whole write" shape
/// <see cref="Station.PersonaTasteAccrualRepository.ThumbAsync"/> already established for its own
/// taste-thumb accrual, one seam over: <see cref="RecordAsync"/> runs the eligibility check, the
/// upsert, the lifetime-counter bump, and the aggregate re-computation inside ONE
/// <see cref="NpgsqlTransaction"/>, so a caller never observes a thumb row without its counters and
/// nudge already reflecting it.
///
/// <para>
/// <b>F150.1's own disjointness, enforced by construction</b>: every statement in this file touches
/// only <c>library.media_thumb</c> and <c>library.media_rotation</c> — never
/// <c>library.media_rating</c>, never <c>station.persona_taste</c>. There is no code path here that
/// COULD write either of those tables; the disjointness is not a runtime check, it is the absence of
/// the SQL.
/// </para>
///
/// <para>
/// <b>gh-#99 safe-scope exclusion + unknown media, both refused the SAME way</b> —
/// <see cref="MediaRotationRepository"/>'s own precedent, one seam over: a single existence query
/// carries the safe-scope predicate (<paramref name="safeScope"/>, re-read live on every call), so a
/// safe-loop <c>mediaId</c> and an unrecognised <c>mediaId</c> both fail that ONE query and both
/// return <see cref="ThumbWriteResult.Ignored"/> — no distinguishing behavior for a caller to leak
/// through the wire (the T358 review binding note this task carries: the controller answers the
/// SAME constant 202 either way).
/// </para>
///
/// <para>
/// <b>Idempotency (F150.7), atomically</b>: <c>(media_id, airing_started_at, listener_key)</c> is a
/// real UNIQUE constraint (db/41/db/01). An earlier draft of this method pre-read the existing
/// <c>direction</c> before deciding whether to INSERT or UPDATE — T365 review HIGH-1, reproduced on
/// real Postgres: that read-then-write has a TOCTOU gap, and two concurrent <see cref="RecordAsync"/>
/// calls for the IDENTICAL key can both observe "no existing row" and both attempt the INSERT, with
/// the loser throwing a 23505 unique-violation straight out of this method. The fix is a single
/// <c>INSERT ... ON CONFLICT (media_id, airing_started_at, listener_key) DO UPDATE ... WHERE ...
/// RETURNING (xmax = 0) AS inserted</c> statement: Postgres itself serializes the two concurrent
/// upserts (one blocks on the other's row lock, then re-evaluates the conflict), so there is no
/// window for either caller to observe a stale "no row" answer. <c>xmax = 0</c> on the RETURNING row
/// is Postgres's own documented idiom for "this row was just INSERTed, not UPDATEd" (a freshly
/// inserted row has no deleting-transaction id yet); the conditional <c>WHERE
/// media_thumb.direction IS DISTINCT FROM excluded.direction</c> is what turns a same-direction
/// repeat into <see cref="ThumbWriteResult.Unchanged"/> — when that condition is false Postgres
/// performs no update AND returns no row, so the ABSENCE of a row (not a value on one) is what
/// <see cref="ThumbWriteResult.Unchanged"/>-ness is derived from.
/// </para>
///
/// <para>
/// <b>Lifetime counters count thumb EVENTS, not net sentiment</b> (F150.9's own "LIFETIME counters"
/// wording): a flip from up to down bumps <c>thumbs_down</c> by one — it does NOT decrement
/// <c>thumbs_up</c>. <c>thumbs_up</c>/<c>thumbs_down</c> are therefore an audit trail of how many
/// up/down EVENTS this track has ever received, not a live tally that could be reconstructed from
/// today's <c>library.media_thumb</c> rows alone (which the F150.9 retention sweep prunes) — the
/// counters are exactly what survives that sweep.
/// </para>
///
/// <para>
/// <b>The aggregate itself is the database's problem, not this class's</b>: every write that changes
/// what <c>nudge</c> should be (a fresh thumb, a flip) calls <c>library.recompute_nudge</c> (SPEC
/// F150.9) with <see cref="GardenerOptions.HalfLifeDays"/>/<see cref="GardenerOptions.Saturation"/>
/// read live off <paramref name="options"/> — never re-implemented in C#, the postgres-dba Rule-7
/// "set-based logic lives in plpgsql, invoked by the app" discipline this whole epic follows.
/// </para>
/// </summary>
sealed class MediaThumbRepository(
    NpgsqlDataSource dataSource, ISafeScopeProvider safeScope, IOptionsMonitor<GardenerOptions> options)
    : IThumbStore
{
    /// <summary>
    /// The maximum accepted <c>listener_key</c> length (T365 review LOW-1): a listener key is a
    /// <c>sha256</c> hex digest (64 chars) or the literal <c>"operator"</c> in production — 128 is
    /// double the longest real value with room to spare, and rejecting anything past it before it
    /// ever reaches SQL keeps an oversized value (an anonymous, unauthenticated caller controls this
    /// string end to end) from blowing the btree index row-size limit on the
    /// <c>(media_id, airing_started_at, listener_key)</c> UNIQUE constraint — an unhandled 500 on the
    /// spectator path, reproduced at T365 review.
    /// </summary>
    const int MaxListenerKeyLength = 128;

    /// <summary>
    /// One transaction: refuse (an out-of-range <paramref name="listenerKey"/>, a safe-scope row, or
    /// an unknown media id), then apply the atomic upsert (class remarks: <c>ON CONFLICT ... DO
    /// UPDATE ... WHERE ... RETURNING (xmax = 0)</c>) to classify Recorded/Flipped/Unchanged in ONE
    /// round trip with no TOCTOU gap, then — for a genuine write only — upsert
    /// <c>library.media_rotation</c> (creating it if this is the row's first-ever write; a
    /// thumbed-but-never-aired track must still carry a nudge) with the matching lifetime counter
    /// bumped in the SAME statement, and re-run <c>library.recompute_nudge</c>.
    /// <see cref="ThumbWriteResult.Unchanged"/> touches NEITHER the counters nor the nudge (F150.7's
    /// own idempotency: a repeat is a true no-op past the eligibility check).
    /// </summary>
    public async Task<ThumbWriteResult> RecordAsync(
        long mediaId, DateTimeOffset airingStartedAt, string listenerKey,
        ThumbDirection direction, ThumbSource source, CancellationToken ct)
    {
        if (listenerKey.Length is 0 or > MaxListenerKeyLength)
            return ThumbWriteResult.Ignored;

        var directionText = ToDirectionText(direction);
        var sourceText = ToSourceText(source);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        if (!await IsEligibleAsync(conn, tx, mediaId, ct))
        {
            await tx.RollbackAsync(ct);
            return ThumbWriteResult.Ignored;
        }

        // Atomic upsert (T365 review HIGH-1 — see class remarks for the concurrency bug this
        // replaces). `xmax = 0` distinguishes a genuine INSERT from an UPDATE; the conditional WHERE
        // means a same-direction repeat returns NO row at all, which is what Unchanged is derived
        // from below — never a value read off a returned row.
        var inserted = await conn.ExecuteScalarAsync<bool?>(new CommandDefinition(
            """
            insert into library.media_thumb (media_id, airing_started_at, listener_key, direction, source)
            values (
                @MediaId, @AiringStartedAt, @ListenerKey,
                @DirectionText::library.thumb_direction, @SourceText::library.thumb_source)
            on conflict (media_id, airing_started_at, listener_key) do update
              set direction = excluded.direction, source = excluded.source
              where media_thumb.direction is distinct from excluded.direction
            returning (xmax = 0) as inserted
            """,
            new
            {
                MediaId = mediaId, AiringStartedAt = airingStartedAt, ListenerKey = listenerKey,
                DirectionText = directionText, SourceText = sourceText,
            },
            transaction: tx,
            cancellationToken: ct));

        if (inserted is null)
        {
            // F150.7 idempotency: the identical direction was already recorded for this
            // (media, airing, listener) triple — the WHERE clause above suppressed the UPDATE
            // entirely, so no row came back. No counter changes, no re-aggregation.
            await tx.CommitAsync(ct);
            return ThumbWriteResult.Unchanged;
        }

        var result = inserted.Value ? ThumbWriteResult.Recorded : ThumbWriteResult.Flipped;

        // Ensure library.media_rotation exists AND bump the matching lifetime counter in ONE upsert
        // (T365 review LOW-2: no interpolated column name, one fewer round trip than a separate
        // ensure-row-then-UPDATE pair). A row created here has never aired: play_count/first_aired_at
        // stay at their column defaults (0/NULL) on the INSERT branch, and the DO UPDATE branch never
        // touches either — RecordAiringAsync (MediaRotationRepository) owns first_aired_at
        // exclusively (T365 review HIGH-2, that class's own fix). Lifetime counters count thumb
        // EVENTS (class remarks): a flip bumps the NEW direction's counter, it never decrements the
        // old one — both CASE expressions read the SAME @DirectionText the upsert above just wrote.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into library.media_rotation (media_id, thumbs_up, thumbs_down)
            values (
                @MediaId,
                case when @DirectionText = 'up' then 1 else 0 end,
                case when @DirectionText = 'down' then 1 else 0 end)
            on conflict (media_id) do update
              set thumbs_up   = library.media_rotation.thumbs_up + case when @DirectionText = 'up' then 1 else 0 end,
                  thumbs_down = library.media_rotation.thumbs_down + case when @DirectionText = 'down' then 1 else 0 end,
                  updated_at  = now()
            """,
            new { MediaId = mediaId, DirectionText = directionText },
            transaction: tx,
            cancellationToken: ct));

        var gardener = options.CurrentValue;
        await conn.ExecuteAsync(new CommandDefinition(
            "select library.recompute_nudge(@MediaId, @HalfLifeDays, @Saturation)",
            new { MediaId = mediaId, HalfLifeDays = gardener.HalfLifeDays, Saturation = gardener.Saturation },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// The F150.5 per-listener daily-cap read (STORY-369, PLAN T366) — a plain count, no eligibility
    /// check, no cap applied here: the caller (<c>SpectatorThumbsController</c>) compares the result
    /// against <c>GardenerOptions.ThumbDailyCap</c> itself, before ever calling <see cref="RecordAsync"/>.
    /// </summary>
    public async Task<int> CountByListenerSinceAsync(string listenerKey, DateTimeOffset since, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from library.media_thumb where listener_key = @ListenerKey and created_at >= @Since",
            new { ListenerKey = listenerKey, Since = since },
            cancellationToken: ct));
    }

    /// <summary>
    /// F150.9's retention sweep: deletes every <c>library.media_thumb</c> row older than
    /// <see cref="GardenerOptions.ThumbRetentionDays"/>, read live off <see cref="options"/>. Never
    /// touches <c>library.media_rotation</c> — the lifetime counters and the last-computed
    /// <c>nudge</c> are exactly what this sweep is FOR preserving once the individual event rows are
    /// gone.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var retentionDays = options.CurrentValue.ThumbRetentionDays;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "delete from library.media_thumb where created_at < now() - make_interval(days => @RetentionDays)",
            new { RetentionDays = retentionDays },
            cancellationToken: ct));
    }

    /// <summary>
    /// The gardener's hourly decay pass (F150.9): one set-based statement applies
    /// <c>library.recompute_nudge</c> to every DISTINCT media id that still carries at least one
    /// <c>library.media_thumb</c> row — a thumb's own age-decayed weight moves every hour even when
    /// no new thumb arrives, so this is what keeps an untouched <c>nudge</c> honest between writes.
    /// </summary>
    public async Task RecomputeAllAsync(CancellationToken ct)
    {
        var gardener = options.CurrentValue;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            select library.recompute_nudge(t.media_id, @HalfLifeDays, @Saturation)
            from (select distinct media_id from library.media_thumb) t
            """,
            new { HalfLifeDays = gardener.HalfLifeDays, Saturation = gardener.Saturation },
            cancellationToken: ct));
    }

    /// <summary>
    /// One query, both refusals (gh-#99 safe-scope, unknown media id) — the T358 review binding
    /// note this task carries: the two cases are structurally indistinguishable to the caller by
    /// design, so they are structurally indistinguishable here too. <see cref="ISafeScopeProvider.Current"/>
    /// is read fresh on every call (never cached), the same live-edit contract
    /// <see cref="MediaRotationRepository"/> already honors. Empty scope short-circuits to no extra
    /// predicate/parameter, mirroring <see cref="MediaRotationRepository.RecordAiringAsync"/>'s own
    /// short-circuit.
    /// </summary>
    async Task<bool> IsEligibleAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long mediaId, CancellationToken ct)
    {
        var scope = safeScope.Current;
        var parameters = new DynamicParameters();
        parameters.Add("MediaId", mediaId);

        var safeExclusion = "";
        if (!scope.IsEmpty)
        {
            parameters.Add("SafeLibraryIds", scope.LibraryIds.ToArray());
            safeExclusion = " and not (library_id = any(@SafeLibraryIds))";
        }

        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"select exists(select 1 from library.media where id = @MediaId{safeExclusion})",
            parameters,
            transaction: tx,
            cancellationToken: ct));
    }

    static string ToDirectionText(ThumbDirection direction) => direction switch
    {
        ThumbDirection.Up => "up",
        ThumbDirection.Down => "down",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unmapped ThumbDirection."),
    };

    static string ToSourceText(ThumbSource source) => source switch
    {
        ThumbSource.Spectator => "spectator",
        ThumbSource.Operator => "operator",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unmapped ThumbSource."),
    };
}
