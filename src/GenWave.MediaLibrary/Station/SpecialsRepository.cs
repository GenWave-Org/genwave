using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IScheduleSpecialStore"/> (SPEC F120.1, STORY-317, PLAN
/// T258) over <c>station.schedule_special</c>. Connection-per-call, mirroring
/// <see cref="ShowRepository"/>'s own wiring; the join/projection shape mirrors
/// <see cref="ScheduleRepository"/>'s own <c>SelectColumns</c>/<c>ScheduleRow</c> idiom exactly —
/// <c>station.show</c> LEFT JOINed at load time (SPEC F116.1's same "resolve identity once, never a
/// per-tick lookup" rule), never <c>show.persona_id</c>/<c>show.envelope</c> (SPEC F115.2's
/// dormant-columns-unread pin extends to this table too — there is no member on <see cref="SpecialRow"/>
/// to receive either even if the query tried).
///
/// <para>
/// <see cref="DateOnlyTypeHandler"/> (this repository's own <c>on_date</c> column needs it) is
/// registered by <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>, NOT here — this
/// type ships dark (SPEC F120.1: no Host call site until PLAN T260 wires <c>AddScheduleSpecialStore</c>
/// in), so a registration tied to THIS class's own construction (a static constructor, an earlier
/// revision's choice) would never fire in production until T260 lands. <c>AddMediaLibrary</c> runs
/// unconditionally at Host startup regardless — the same reason it is also where
/// <c>DefaultTypeMap.MatchNamesWithUnderscores</c> is set, not any one repository's own static
/// constructor. <c>GenWave.MediaLibrary.Tests.DatabaseFixture</c>'s own <c>InitializeAsync</c> registers
/// it too, for the identical "tests construct the repository directly, never through AddMediaLibrary"
/// reason that fixture already sets <c>MatchNamesWithUnderscores</c> itself.
/// </para>
/// </summary>
sealed class SpecialsRepository(Lazy<NpgsqlDataSource> dataSource) : IScheduleSpecialStore
{
    public event Action? SpecialsChanged;

    /// <summary>
    /// Ephemeral Dapper projection — settable properties, not a positional record, mirrors
    /// <see cref="ScheduleRepository"/>'s own <c>ScheduleRow</c> remarks: Npgsql reports the <c>text[]</c>
    /// <c>genres</c> column as the general <see cref="Array"/> CLR type, which Dapper's stricter
    /// positional-record constructor matching rejects; the property-setter fallback this shape uses
    /// coerces it instead.
    /// </summary>
    sealed record SpecialRow
    {
        public long Id { get; init; }
        public DateOnly OnDate { get; init; }
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

    // Mirrors ScheduleRepository's own SelectColumns constant, keyed on on_date instead of
    // day_of_week/start_minute. sh.id/name/tagline/flavor only — never persona_id/envelope.
    const string SelectColumns =
        """
        select s.id::bigint as id, s.on_date, s.start_minute, s.end_minute,
               s.persona_id::bigint as persona_id, s.genres,
               s.energy_min::double precision as energy_min, s.energy_max::double precision as energy_max,
               sh.id::bigint as show_id, sh.name as show_name, sh.tagline as show_tagline, sh.flavor as show_flavor
        from station.schedule_special s
        left join station.show sh on sh.id = s.show_id
        """;

    public async Task<IReadOnlyList<ScheduleSpecial>> ListUpcomingAsync(DateOnly fromDate, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<SpecialRow>(new CommandDefinition(
            $"{SelectColumns} where s.on_date >= @fromDate order by s.on_date, s.start_minute",
            new { fromDate },
            cancellationToken: ct));
        return rows.Select(ToSpecial).ToList();
    }

    /// <summary>
    /// Single-statement insert-then-join (SPEC F120.1) via a CTE — one round trip, atomic by
    /// construction (a single statement needs no explicit transaction). No application-side
    /// pre-validation (see <see cref="IScheduleSpecialStore.CreateAsync"/>'s own remarks): an off-grid
    /// minute, an overlapping per-date span, or an unknown persona/show id all surface as a raw
    /// <see cref="PostgresException"/> from db/36's own CHECK/EXCLUDE/FK constraints, never caught or
    /// translated here.
    /// </summary>
    public async Task<ScheduleSpecial> CreateAsync(ScheduleSpecial special, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleAsync<SpecialRow>(new CommandDefinition(
            """
            with ins as (
                insert into station.schedule_special
                    (on_date, start_minute, end_minute, persona_id, show_id, genres, energy_min, energy_max)
                values (@OnDate, @StartMinute, @EndMinute, @PersonaId, @ShowId, @Genres::text[], @EnergyMin, @EnergyMax)
                returning id, on_date, start_minute, end_minute, persona_id, show_id, genres, energy_min, energy_max
            )
            select ins.id::bigint as id, ins.on_date, ins.start_minute, ins.end_minute,
                   ins.persona_id::bigint as persona_id, ins.genres,
                   ins.energy_min::double precision as energy_min, ins.energy_max::double precision as energy_max,
                   sh.id::bigint as show_id, sh.name as show_name, sh.tagline as show_tagline, sh.flavor as show_flavor
            from ins
            left join station.show sh on sh.id = ins.show_id
            """,
            new
            {
                special.OnDate,
                special.StartMinute,
                special.EndMinute,
                special.PersonaId,
                special.ShowId,
                Genres = special.Genres?.ToArray(),
                special.EnergyMin,
                special.EnergyMax,
            },
            cancellationToken: ct));

        SpecialsChanged?.Invoke();
        return ToSpecial(row);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "delete from station.schedule_special where id = @id",
            new { id },
            cancellationToken: ct));

        if (affected == 0) return false;

        SpecialsChanged?.Invoke();
        return true;
    }

    static ScheduleSpecial ToSpecial(SpecialRow row) => new(
        row.Id, row.OnDate, row.StartMinute, row.EndMinute, row.PersonaId,
        row.Genres, row.EnergyMin, row.EnergyMax, ToShowSummary(row), row.ShowId);

    /// <summary>Mirrors <see cref="ScheduleRepository"/>'s own <c>ToShowSummary</c> — see that
    /// method's remarks for why <c>ShowName</c> is checked alongside <c>ShowId</c>.</summary>
    static ShowSummary? ToShowSummary(SpecialRow row) =>
        row.ShowId is { } showId && row.ShowName is { } showName
            ? new ShowSummary(showId, showName, row.ShowTagline, row.ShowFlavor)
            : null;
}
