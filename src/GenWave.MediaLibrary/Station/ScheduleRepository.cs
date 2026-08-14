using Dapper;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IScheduleStore"/> (SPEC F91.1, F91.3, F91.8;
/// STORY-240, STORY-242, PLAN T118) over <c>station.segment_schedule</c>. Connection-per-call, except
/// <see cref="ReplaceWeekAsync"/>'s delete-then-insert-then-reload, which shares one
/// <see cref="NpgsqlTransaction"/> — the same "the whole write is one transaction" shape
/// <see cref="RequestRepository.InsertAsync"/>'s insert-plus-sweep already establishes for a different
/// table.
///
/// The <see cref="WeekChanged"/> event exists purely so a future in-process cache (T119's
/// <c>ScheduleResolver</c>) can invalidate itself without re-querying on every 3s feeder tick (SPEC
/// F91.3) — Postgres, not this instance, is the truth <see cref="LoadWeekAsync"/> always re-derives
/// from on every call.
///
/// <para>
/// <b>gh-#406 slice 1:</b> <see cref="ReplaceWeekAsync"/> catches its own 23503 foreign-key violation
/// (mirrors <see cref="PersonaRepository.DeleteAsync"/>'s own race-backstop idiom) instead of letting
/// <see cref="PostgresException"/> escape to the caller — an L2 Postgres-confinement violation this
/// class exists specifically to close. <c>ScheduleController</c> used to catch that exception itself;
/// it now only ever sees the typed <see cref="ScheduleReplaceResult.PersonaVanished"/> case.
/// </para>
/// </summary>
sealed class ScheduleRepository(Lazy<NpgsqlDataSource> dataSource, ILogger<ScheduleRepository> logger) : IScheduleStore
{
    // Postgres SQLSTATE for foreign_key_violation — mirrors PersonaRepository's own ForeignKeyViolation
    // const (no Npgsql.PostgresErrorCodes dependency, house idiom). gh-#406 slice 1: this mapping moved
    // down from ScheduleController, which used to catch the raw PostgresException itself.
    const string ForeignKeyViolation = "23503";

    public event Action? WeekChanged;

    /// <summary>
    /// Ephemeral Dapper projection — settable properties, not a positional record, mirrors
    /// <see cref="RequestRepository"/>'s own <c>RequestRow</c> remarks: Npgsql reports a <c>text[]</c>
    /// column (<c>genres</c>) as the general <see cref="Array"/> CLR type, which Dapper's stricter
    /// positional-record constructor matching rejects; the property-setter fallback this shape uses
    /// coerces it instead.
    ///
    /// <para>
    /// <c>ShowId</c>/<c>ShowName</c>/<c>ShowTagline</c>/<c>ShowFlavor</c> (SPEC F116.1, PLAN T241) are
    /// <see cref="SelectColumns"/>'s own LEFT JOIN against <c>station.show</c> — deliberately never
    /// <c>show.persona_id</c>/<c>show.envelope</c> (SPEC F115.2's dormant-columns-unread pin: this row
    /// shape has no property to even receive them). All four are null together on an unnamed block
    /// (no matching join row).
    /// </para>
    /// </summary>
    sealed record ScheduleRow
    {
        public long Id { get; init; }
        public int DayOfWeek { get; init; }
        public int StartMinute { get; init; }
        public int EndMinute { get; init; }
        public long? PersonaId { get; init; }
        public string[]? Genres { get; init; }
        public double? EnergyMin { get; init; }
        public double? EnergyMax { get; init; }
        public long? ShowId { get; init; }
        public string? ShowName { get; init; }
        public string? ShowTagline { get; init; }
        public string? ShowFlavor { get; init; }
    }

    // SPEC F116.1/F91.3 (PLAN T241): the LEFT JOIN resolves every block's show identity at THIS one
    // load (LoadWeekAsync/ReplaceWeekAsync's own post-write reload both share this constant) rather
    // than a per-tick lookup — ScheduleResolver only ever sees the already-joined ScheduleWeekSnapshot
    // (ARCHITECTURE.md "the 3s feeder tick performs no schedule query", now extended to show identity
    // too). Selects ONLY show.id/name/tagline/flavor — never show.persona_id/envelope (SPEC F115.2's
    // dormant-columns-unread pin), enforced here at the query itself, not merely by ScheduleRow's own
    // shape above.
    const string SelectColumns =
        """
        select s.id::bigint as id, s.day_of_week, s.start_minute, s.end_minute,
               s.persona_id::bigint as persona_id, s.genres,
               s.energy_min::double precision as energy_min, s.energy_max::double precision as energy_max,
               sh.id::bigint as show_id, sh.name as show_name, sh.tagline as show_tagline, sh.flavor as show_flavor
        from station.segment_schedule s
        left join station.show sh on sh.id = s.show_id
        """;

    public async Task<ScheduleWeekSnapshot> LoadWeekAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var segments = await LoadSegmentsAsync(conn, transaction: null, ct);
        return new ScheduleWeekSnapshot(segments);
    }

    public async Task<ScheduleReplaceResult> ReplaceWeekAsync(
        IReadOnlyList<ScheduleSegment> week, string? expectedVersion, CancellationToken ct)
    {
        var errors = await ValidateAsync(week, ct);
        if (errors.Count > 0)
            return new ScheduleReplaceResult.ValidationFailed(errors);

        try
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Staleness guard (gh-#255): compare the stored week's content fingerprint against what the
            // caller loaded, INSIDE this same transaction — a mismatch means another writer replaced the
            // week since then, and honoring this full-replace would silently destroy that writer's work
            // (observed live: demo Loki 2026-07-28, segmentCount 54 → 48 with no error anywhere). Read
            // committed leaves a narrow same-instant race two concurrent replaces can still thread; the
            // guard exists for the real-world case — a stale tab/session minutes-to-hours old — not as a
            // serializable-isolation substitute.
            if (expectedVersion is not null)
            {
                var stored = await LoadSegmentsAsync(conn, tx, ct);
                var storedVersion = ScheduleWeekVersion.Compute(stored);
                if (storedVersion != expectedVersion)
                    return new ScheduleReplaceResult.VersionConflict(storedVersion);
            }

            await conn.ExecuteAsync(new CommandDefinition(
                "delete from station.segment_schedule", transaction: tx, cancellationToken: ct));

            if (week.Count > 0)
            {
                // PLAN T243: ScheduleSegment.ShowId (SPEC F116.1, PLAN T241) now rides this insert too —
                // the bare foreign key, written straight into show_id, never Show's own Name/Tagline/Flavor
                // (those are station.show's own entity fields, re-resolved by SelectColumns' own LEFT JOIN
                // on the reload below, never written from here — ScheduleSegment's own remarks on why ShowId,
                // not Show, is the write-authoritative field). This is deliberately the ONLY way
                // ReplaceWeekAsync itself changed for T243: whatever ShowId a caller's own ScheduleSegment
                // already carries survives a whole-week replace instead of being silently dropped — closing
                // the "load-only" gap this comment used to describe. The Host GET/PUT wire
                // (ScheduleSegmentDto/ScheduleController) round-trips this same field too (ScheduleController's
                // own class remarks) — a whole-grid repaint no longer silently erases a show assignment set
                // through AssignShowAsync's own dedicated endpoint.
                //
                // Also the FK-race surface (gh-#406 slice 1, this method's own catch below): a
                // PersonaId a validated row named, or a ShowId ValidateAsync never checks the existence
                // of at all, can each be missing by the time this statement runs.
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into station.segment_schedule
                        (day_of_week, start_minute, end_minute, persona_id, genres, energy_min, energy_max, show_id)
                    values (@Day, @StartMinute, @EndMinute, @PersonaId, @Genres::text[], @EnergyMin, @EnergyMax, @ShowId)
                    """,
                    week.Select(seg => new
                    {
                        Day = (int)seg.Day,
                        seg.StartMinute,
                        seg.EndMinute,
                        seg.PersonaId,
                        Genres = seg.Genres?.ToArray(),
                        seg.EnergyMin,
                        seg.EnergyMax,
                        seg.ShowId,
                    }),
                    transaction: tx,
                    cancellationToken: ct));
            }

            // Reload the just-written rows INSIDE this same transaction, before commit — the returned
            // Replaced.Snapshot must reflect exactly what THIS call wrote, never a concurrent writer's
            // interleaved state that a post-commit reload on a fresh connection could otherwise observe.
            var segments = await LoadSegmentsAsync(conn, tx, ct);

            await tx.CommitAsync(ct);

            var snapshot = new ScheduleWeekSnapshot(segments);
            WeekChanged?.Invoke();
            return new ScheduleReplaceResult.Replaced(snapshot);
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            // gh-#406 slice 1: this mapping moved down from ScheduleController, which used to catch
            // Npgsql.PostgresException directly (an L2 Postgres-confinement violation) — see this
            // class's own remarks and ScheduleReplaceResult.PersonaVanished's own remarks for the race
            // itself. Logged with full detail here — the repository is the only layer that still ever
            // sees the raw exception; every other caller (the controller included) gets only the
            // generic PersonaVanished case, never the raw SQLSTATE/message.
            logger.LogWarning(ex,
                "Schedule replace raced a concurrent write (likely a persona deleted mid-validation)");
            return new ScheduleReplaceResult.PersonaVanished();
        }
    }

    /// <summary>
    /// Ephemeral Dapper projection for <see cref="AssignShowAsync"/>'s own single day-scoped read —
    /// settable properties, not a positional record, mirrors this file's own
    /// <see cref="ScheduledSlotRow"/>/<see cref="ScheduleRow"/> idiom for a plain-int projection.
    /// </summary>
    sealed record RunRow
    {
        public long Id { get; init; }
        public int DayOfWeek { get; init; }
        public int StartMinute { get; init; }
        public int EndMinute { get; init; }
        public long? PersonaId { get; init; }
    }

    public async Task<ShowAssignResult> AssignShowAsync(long blockId, long? showId, bool applyToRun, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Single statement (PLAN T243 review F3): the clicked block's own row and every OTHER row on
        // its day, read together — the day is resolved by a correlated subquery against blockId
        // itself, so a blockId naming no row at all returns an empty set (day_of_week = NULL matches
        // nothing in SQL) rather than needing a separate existence SELECT first. This also closes a
        // READ COMMITTED race the previous two-statement shape left open: a concurrent
        // ReplaceWeekAsync's delete-then-insert landing between two separate SELECTs (a first read
        // that found the block, a second keyed by the day value that first read returned) could leave
        // blockId absent from the second read's result — this fold makes "is blockId present in THIS
        // one result set" the single existence check, never two, so there is nothing left for a
        // between-reads race to land in.
        var dayRows = (await conn.QueryAsync<RunRow>(new CommandDefinition(
            """
            select id::bigint as id, day_of_week, start_minute, end_minute, persona_id::bigint as persona_id
            from station.segment_schedule
            where day_of_week = (select day_of_week from station.segment_schedule where id = @blockId)
            order by start_minute
            """,
            new { blockId },
            transaction: tx,
            cancellationToken: ct))).ToList();

        // No explicit rollback on any early return below — mirrors ReplaceWeekAsync's own
        // VersionConflict path (this class's remarks): the transaction is disposed un-committed, and
        // NpgsqlTransaction's own DisposeAsync rolls back, so nothing this method has read so far can
        // ever be mistaken for a write.
        //
        // N3 review: this existence check IS the FindIndex ComputeRun would otherwise repeat — folded
        // into one scan so a blockId absent from dayRows can never reach ComputeRun's own indexer at
        // all (dayRows[index] with index == -1 would throw, not return BlockNotFound), and the day's
        // row set is only ever walked once to answer "is blockId here."
        var blockIndex = dayRows.FindIndex(r => r.Id == blockId);
        if (blockIndex < 0)
            return new ShowAssignResult.BlockNotFound();

        if (showId is { } targetShowId)
        {
            var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from station.show where id = @targetShowId)",
                new { targetShowId },
                transaction: tx,
                cancellationToken: ct));

            // Checked INSIDE this same transaction, immediately before the write below — a show
            // deleted between this check and the UPDATE cannot land a dangling show_id either way,
            // since Postgres would still hold this transaction's own read committed snapshot for the
            // FK check the UPDATE itself is subject to.
            if (!exists)
                return new ShowAssignResult.ShowNotFound();
        }

        var runIds = applyToRun ? ComputeRun(dayRows, blockIndex) : [blockId];

        // RETURNING id (PLAN T243 review F3): UpdatedBlockIds reports what the UPDATE actually
        // touched, never the pre-computed runIds list — a row named in runIds a moment ago but deleted
        // by a concurrent ReplaceWeekAsync before this statement runs simply does not come back, so
        // this call can never claim to have changed a row it didn't. An empty result means every row
        // this call intended to touch is already gone by the time the write ran — reported as
        // BlockNotFound, the same "nothing was written" contract as the existence check above, never a
        // hollow Assigned with an empty list.
        var updatedIds = (await conn.QueryAsync<long>(new CommandDefinition(
            """
            update station.segment_schedule set show_id = @showId, updated_at = now()
            where id = any(@runIds)
            returning id::bigint
            """,
            new { showId, runIds = runIds.ToArray() },
            transaction: tx,
            cancellationToken: ct))).ToList();

        if (updatedIds.Count == 0)
            return new ShowAssignResult.BlockNotFound();

        // The fresh week fingerprint (SPEC F2, gh-#255), computed from the post-write rows INSIDE this
        // same transaction — mirrors ReplaceWeekAsync's own "reload before commit" discipline just
        // above, so AssignShowResponseDto.Version and a subsequent GET's own Version agree on exactly
        // the same content, never a version read from a connection that could observe a concurrent
        // writer's interleaved state.
        var segments = await LoadSegmentsAsync(conn, tx, ct);
        var version = ScheduleWeekVersion.Compute(segments);

        await tx.CommitAsync(ct);

        WeekChanged?.Invoke();
        return new ShowAssignResult.Assigned(updatedIds, version);
    }

    /// <summary>
    /// SPEC F119.2's span rule: <paramref name="dayRows"/> is every block on the clicked block's own
    /// day, ordered by start minute (the EXCLUDE constraint already guarantees no two rows overlap, so
    /// "ordered by start minute" and "ordered by end minute" agree). <paramref name="index"/> is the
    /// clicked block's own position in <paramref name="dayRows"/> — resolved ONCE by
    /// <see cref="AssignShowAsync"/>'s own existence guard (never re-derived here, and never negative:
    /// that guard already returned <see cref="ShowAssignResult.BlockNotFound"/> before this method is
    /// ever called). Walks outward from <paramref name="index"/> in both directions, extending the run
    /// one row at a time only while the neighbor is BOTH time-adjacent (its end/start minute exactly
    /// meets the run's own edge — a gap in the grid ends the run) AND persona-matching (<c>long?</c>'s
    /// own <c>==</c> already implements Postgres's <c>IS NOT DISTINCT FROM</c> — two
    /// <see langword="null"/> persona ids compare equal, so a run of contiguous music-only blocks is
    /// exactly as legal a run as one of contiguous same-persona blocks; RATIFIED by Dean, 2026-08-10 —
    /// see <see cref="Abstractions.IScheduleStore.AssignShowAsync"/>'s own remarks for the ruling).
    /// Returns every id in the run, never the <see cref="RunRow"/>s themselves —
    /// <see cref="AssignShowAsync"/>'s own callers only ever need the id list.
    /// </summary>
    static IReadOnlyList<long> ComputeRun(List<RunRow> dayRows, int index)
    {
        var persona = dayRows[index].PersonaId;

        var start = index;
        while (start > 0
               && dayRows[start - 1].EndMinute == dayRows[start].StartMinute
               && dayRows[start - 1].PersonaId == persona)
            start--;

        var end = index;
        while (end < dayRows.Count - 1
               && dayRows[end + 1].StartMinute == dayRows[end].EndMinute
               && dayRows[end + 1].PersonaId == persona)
            end++;

        return dayRows.Skip(start).Take(end - start + 1).Select(r => r.Id).ToList();
    }

    static async Task<IReadOnlyList<ScheduleSegment>> LoadSegmentsAsync(
        NpgsqlConnection conn, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<ScheduleRow>(new CommandDefinition(
            $"{SelectColumns} order by s.day_of_week, s.start_minute",
            transaction: transaction,
            cancellationToken: ct));
        return rows.Select(ToSegment).ToList();
    }

    /// <summary>
    /// App-side validation (SPEC F91.1) run BEFORE any statement touches
    /// <c>station.segment_schedule</c> — the database's own CHECK/EXCLUDE/FK constraints are the last
    /// line, never the first. Returns one <see cref="ScheduleCellError"/> per offending row in
    /// <paramref name="week"/>, keyed by its position in that list so a caller (T122) can map straight
    /// back onto the submitted document; a valid <paramref name="week"/> returns an empty list.
    /// </summary>
    async Task<IReadOnlyList<ScheduleCellError>> ValidateAsync(IReadOnlyList<ScheduleSegment> week, CancellationToken ct)
    {
        var errors = new List<ScheduleCellError>();

        for (var i = 0; i < week.Count; i++)
        {
            var seg = week[i];

            if (!Enum.IsDefined(seg.Day))
                errors.Add(Error(i, seg, ScheduleCellErrorKind.InvalidDay, $"'{seg.Day}' is not a day of the week."));

            if (seg.StartMinute % 30 != 0 || seg.StartMinute is < 0 or > 1410)
                errors.Add(Error(i, seg, ScheduleCellErrorKind.InvalidMinuteRange,
                    $"start_minute {seg.StartMinute} must be a multiple of 30 within [0, 1410]."));

            if (seg.EndMinute % 30 != 0 || seg.EndMinute is < 30 or > 1440)
                errors.Add(Error(i, seg, ScheduleCellErrorKind.InvalidMinuteRange,
                    $"end_minute {seg.EndMinute} must be a multiple of 30 within [30, 1440]."));

            if (seg.EndMinute <= seg.StartMinute)
                errors.Add(Error(i, seg, ScheduleCellErrorKind.InvalidMinuteRange,
                    $"end_minute {seg.EndMinute} must be greater than start_minute {seg.StartMinute}."));
        }

        // Overlap — per day, sort by start and track the running-max end seen so far; a start before
        // that running max means SOME earlier segment on the same day still covers this moment,
        // whether or not it is the immediately preceding one (comparing only to the previous row would
        // miss a segment nested inside an earlier, wider one).
        foreach (var group in week.Select((seg, index) => (seg, index)).GroupBy(x => x.seg.Day))
        {
            var ordered = group.OrderBy(x => x.seg.StartMinute).ToList();
            var runningMaxEnd = ordered[0].seg.EndMinute;
            for (var k = 1; k < ordered.Count; k++)
            {
                var (seg, index) = ordered[k];
                if (seg.StartMinute < runningMaxEnd)
                    errors.Add(Error(index, seg, ScheduleCellErrorKind.Overlap, $"overlaps another segment on {seg.Day}."));
                runningMaxEnd = Math.Max(runningMaxEnd, seg.EndMinute);
            }
        }

        // Unknown persona — one round trip for every distinct id named in the submission.
        // OfType<long> both filters the nulls (music-only rows) and unwraps the rest — no
        // null-forgiving operator needed to get from long? to long.
        var personaIds = week.Select(s => s.PersonaId).OfType<long>().Distinct().ToList();
        if (personaIds.Count > 0)
        {
            await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
            var known = new HashSet<long>(await conn.QueryAsync<long>(new CommandDefinition(
                "select id::bigint from station.persona where id = any(@ids)",
                new { ids = personaIds },
                cancellationToken: ct)));

            for (var i = 0; i < week.Count; i++)
            {
                var seg = week[i];
                if (seg.PersonaId is long personaId && !known.Contains(personaId))
                    errors.Add(Error(i, seg, ScheduleCellErrorKind.UnknownPersona, $"persona id {personaId} does not exist."));
            }
        }

        return errors;
    }

    static ScheduleCellError Error(int rowIndex, ScheduleSegment seg, ScheduleCellErrorKind kind, string message) =>
        new(rowIndex, seg.Day, seg.StartMinute, seg.EndMinute, kind, message);

    // PLAN T243 — Show and ShowId are set TOGETHER here, both derived from the same row.ShowId, so a
    // loaded segment's two show fields never disagree (ScheduleSegment's own remarks: Show is the
    // load-time display projection, ShowId is what every writer and ScheduleWeekVersion.Compute read).
    static ScheduleSegment ToSegment(ScheduleRow row) => new(
        row.Id, (DayOfWeek)row.DayOfWeek, row.StartMinute, row.EndMinute, row.PersonaId,
        row.Genres, row.EnergyMin, row.EnergyMax, ToShowSummary(row), row.ShowId);

    /// <summary>SPEC F116.1 (PLAN T241) — <see langword="null"/> when the block names no show (the
    /// LEFT JOIN found no matching <c>station.show</c> row); otherwise the four identity columns
    /// <see cref="SelectColumns"/> selected, never <c>persona_id</c>/<c>envelope</c> (SPEC F115.2's
    /// dormant-columns-unread pin — this method has no row data to even attempt reading either from).
    /// <c>ShowName</c> is checked alongside <c>ShowId</c> purely as belt-and-suspenders null-safety
    /// (never the actual guard in practice — <c>station.show.name</c> is <c>NOT NULL</c>, so any row
    /// the join finds by id always carries one): this avoids ever needing the null-forgiving operator
    /// to construct <see cref="ShowSummary"/> from a row Dapper types every column of as nullable.
    /// </summary>
    static ShowSummary? ToShowSummary(ScheduleRow row) =>
        row.ShowId is { } showId && row.ShowName is { } showName
            ? new ShowSummary(showId, showName, row.ShowTagline, row.ShowFlavor)
            : null;

    /// <summary>
    /// Ephemeral Dapper projection for <see cref="GetSlotsByShowIdAsync"/> — settable properties, not
    /// a positional record, mirrors <see cref="PersonaRepository"/>'s own identically-shaped
    /// <c>ScheduledSlotRow</c>: kept as a plain <see cref="int"/> here and cast to
    /// <see cref="DayOfWeek"/> only when building the public <see cref="ScheduledSlot"/>, rather than
    /// trusting Dapper's constructor-based binding to coerce an integer column straight into an
    /// enum-typed positional-record parameter.
    /// </summary>
    sealed record ScheduledSlotRow
    {
        public int DayOfWeek { get; init; }
        public int StartMinute { get; init; }
        public int EndMinute { get; init; }
    }

    /// <summary>
    /// The show delete guard's own detail read (SPEC F115.4, PLAN T240) — mirrors
    /// <see cref="PersonaRepository"/>'s own <c>QueryScheduledSlotsAsync</c> shape exactly, just
    /// against <c>show_id</c> instead of <c>persona_id</c> and as a public seam member rather than a
    /// private helper, since <see cref="Abstractions.IShowStore.DeleteAsync"/> deliberately never
    /// pre-queries this table itself (that store's own remarks) — <c>ShowsController</c> calls this
    /// directly instead.
    /// </summary>
    public async Task<IReadOnlyList<ScheduledSlot>> GetSlotsByShowIdAsync(long showId, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ScheduledSlotRow>(new CommandDefinition(
            """
            select day_of_week, start_minute, end_minute
            from station.segment_schedule
            where show_id = @showId
            order by day_of_week, start_minute
            """,
            new { showId },
            cancellationToken: ct));

        return rows.Select(row => new ScheduledSlot((DayOfWeek)row.DayOfWeek, row.StartMinute, row.EndMinute)).ToList();
    }
}
