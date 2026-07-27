using Dapper;
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
/// </summary>
sealed class ScheduleRepository(Lazy<NpgsqlDataSource> dataSource) : IScheduleStore
{
    public event Action? WeekChanged;

    /// <summary>
    /// Ephemeral Dapper projection — settable properties, not a positional record, mirrors
    /// <see cref="RequestRepository"/>'s own <c>RequestRow</c> remarks: Npgsql reports a <c>text[]</c>
    /// column (<c>genres</c>) as the general <see cref="Array"/> CLR type, which Dapper's stricter
    /// positional-record constructor matching rejects; the property-setter fallback this shape uses
    /// coerces it instead.
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
    }

    const string SelectColumns =
        """
        select id::bigint as id, day_of_week, start_minute, end_minute,
               persona_id::bigint as persona_id, genres,
               energy_min::double precision as energy_min, energy_max::double precision as energy_max
        from station.segment_schedule
        """;

    public async Task<ScheduleWeekSnapshot> LoadWeekAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var segments = await LoadSegmentsAsync(conn, transaction: null, ct);
        return new ScheduleWeekSnapshot(segments);
    }

    public async Task<ScheduleReplaceResult> ReplaceWeekAsync(IReadOnlyList<ScheduleSegment> week, CancellationToken ct)
    {
        var errors = await ValidateAsync(week, ct);
        if (errors.Count > 0)
            return new ScheduleReplaceResult.ValidationFailed(errors);

        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.segment_schedule", transaction: tx, cancellationToken: ct));

        if (week.Count > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into station.segment_schedule
                    (day_of_week, start_minute, end_minute, persona_id, genres, energy_min, energy_max)
                values (@Day, @StartMinute, @EndMinute, @PersonaId, @Genres::text[], @EnergyMin, @EnergyMax)
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

    static async Task<IReadOnlyList<ScheduleSegment>> LoadSegmentsAsync(
        NpgsqlConnection conn, NpgsqlTransaction? transaction, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<ScheduleRow>(new CommandDefinition(
            $"{SelectColumns} order by day_of_week, start_minute",
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

    static ScheduleSegment ToSegment(ScheduleRow row) => new(
        row.Id, (DayOfWeek)row.DayOfWeek, row.StartMinute, row.EndMinute, row.PersonaId,
        row.Genres, row.EnergyMin, row.EnergyMax);
}
