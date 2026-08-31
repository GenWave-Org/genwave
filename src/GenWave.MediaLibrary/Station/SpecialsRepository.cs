using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IScheduleSpecialStore"/> (SPEC F120.1, STORY-317, PLAN
/// T258) over <c>station.schedule_special</c>. Connection-per-call, mirroring
/// <see cref="ShowRepository"/>'s own wiring; the join/projection shape mirrors
/// <see cref="ScheduleRepository"/>'s own <c>SelectColumns</c>/<c>ScheduleRow</c> idiom exactly —
/// <c>station.show</c> LEFT JOINed at load time (SPEC F116.1's same "resolve identity once, never a
/// per-tick lookup" rule), never <c>show.persona_id</c> or any <c>envelope</c> key beyond
/// <c>rotation</c> (SPEC F115.2's dormant-columns-unread pin extends to this table too, narrowed by
/// the same one field SPEC F152.3/PLAN T360 wakes on <see cref="ScheduleRepository"/> — there is no
/// member on <see cref="SpecialRow"/> to receive anything else even if the query tried).
///
/// <para>
/// <see cref="DateOnlyTypeHandler"/> (this repository's own <c>on_date</c> column needs it) is
/// registered by <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>, NOT here — a
/// registration tied to THIS class's own construction (a static constructor, an earlier revision's
/// choice) would only fire once something actually resolves <see cref="IScheduleSpecialStore"/>, which
/// is exactly the ordering <c>AddMediaLibrary</c>'s own unconditional, Host-startup-time registration
/// avoids depending on — the same reason it is also where <c>DefaultTypeMap.MatchNamesWithUnderscores</c>
/// is set, not any one repository's own static constructor. <c>GenWave.MediaLibrary.Tests.DatabaseFixture</c>'s
/// own <c>InitializeAsync</c> registers it too, for the identical "tests construct the repository
/// directly, never through AddMediaLibrary" reason that fixture already sets
/// <c>MatchNamesWithUnderscores</c> itself.
/// </para>
///
/// <para>
/// <b>PLAN T259 — <see cref="CreateAsync"/> now translates db/36's own rejections instead of letting
/// them propagate raw.</b> See <see cref="ScheduleSpecialCreateResult"/>'s own remarks for exactly why
/// this repository — not <c>GenWave.Host.Api.SpecialsController</c> — is where SQLSTATE 23P01
/// (exclusion_violation, an overlapping same-date span) and 23503 (foreign_key_violation, an unknown/
/// concurrently-deleted persona or show) become <see cref="ScheduleSpecialCreateResult.Overlap"/>/
/// <see cref="ScheduleSpecialCreateResult.UnknownReference"/>: <c>GenWave.Architecture.Tests</c>' L2
/// law confines every <c>Npgsql</c> reference to this project's <c>Catalog</c>/<c>Station</c>
/// namespaces, with no baseline exemption for a new controller.
/// </para>
/// </summary>
sealed class SpecialsRepository(Lazy<NpgsqlDataSource> dataSource, ILogger<SpecialsRepository> logger) : IScheduleSpecialStore
{
    // Postgres SQLSTATEs db/36's own CHECK/EXCLUDE/FK constraints can raise out of CreateAsync's
    // insert — mirrors ShowRepository's own well-known-constant idiom (no Npgsql.PostgresErrorCodes
    // dependency). exclusion_violation is the per-date EXCLUDE guard (SPEC F120.1); foreign_key_violation
    // is either of schedule_special's two ON DELETE RESTRICT FKs (persona_id/show_id) — db/36 never
    // distinguishes which column raised it in the SQLSTATE alone, and this repository doesn't need to:
    // ScheduleSpecialCreateResult.UnknownReference covers both, exactly like ScheduleSpecialCreateResult's
    // own remarks document.
    const string ExclusionViolation = "23P01";
    const string ForeignKeyViolation = "23503";

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
        public string? ShowSlug { get; init; }
        public string? ShowTagline { get; init; }
        public string? ShowFlavor { get; init; }
        public string? ShowRotationJson { get; init; }
    }

    // Mirrors ScheduleRepository's own SelectColumns constant, keyed on on_date instead of
    // day_of_week/start_minute — ScheduleShowJoinColumns.Select (PLAN T360 review LOW-4) is the
    // identical show-identity-plus-rotation column list both repositories share (SPEC F115.2/F152.3):
    // never persona_id or any other envelope key. ShowSlug joins at PLAN T285 (SPEC F127.8 review F4)
    // — a special-covered airing needs the same stable show identity a weekly block carries, so
    // CrosstalkPlanner.IsShowEnabled works identically whether "now" resolves to a weekly block or a
    // projected special (ScheduleResolver.ProjectSpecial). PLAN T360 review LOW-5: this JOIN is
    // divergence-free by construction (one query, one consistent Show); a live station.show WRITE
    // reaching an already-cached snapshot is IShowStore.ShowChanged's own job instead (mirrors
    // ScheduleRepository's own SelectColumns remarks).
    const string SelectColumns =
        "select s.id::bigint as id, s.on_date, s.start_minute, s.end_minute, " +
        "s.persona_id::bigint as persona_id, s.genres, " +
        "s.energy_min::double precision as energy_min, s.energy_max::double precision as energy_max, " +
        ScheduleShowJoinColumns.Select +
        " from station.schedule_special s left join station.show sh on sh.id = s.show_id";

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
    /// PRE-validation (see <see cref="IScheduleSpecialStore.CreateAsync"/>'s own remarks): db/36's own
    /// CHECK/EXCLUDE/FK constraints are the ONLY line of defense against an off-grid minute, an
    /// overlapping per-date span, or an unknown persona/show id. An overlapping span (exclusion_violation)
    /// or an unknown/concurrently-deleted persona/show (foreign_key_violation) are caught here and
    /// translated (PLAN T259 — see <see cref="ScheduleSpecialCreateResult"/>'s own remarks for why this
    /// moved into the repository layer); an off-grid minute (a CHECK violation) is NOT one of those two
    /// SQLSTATEs and still propagates as a raw <see cref="PostgresException"/> — <c>SpecialsController</c>'s
    /// own app-side range validation means an ordinary caller never reaches that path.
    /// </summary>
    public async Task<ScheduleSpecialCreateResult> CreateAsync(ScheduleSpecial special, CancellationToken ct)
    {
        await using var conn = await dataSource.Value.OpenConnectionAsync(ct);

        SpecialRow row;
        try
        {
            row = await conn.QuerySingleAsync<SpecialRow>(new CommandDefinition(
                $"""
                with ins as (
                    insert into station.schedule_special
                        (on_date, start_minute, end_minute, persona_id, show_id, genres, energy_min, energy_max)
                    values (@OnDate, @StartMinute, @EndMinute, @PersonaId, @ShowId, @Genres::text[], @EnergyMin, @EnergyMax)
                    returning id, on_date, start_minute, end_minute, persona_id, show_id, genres, energy_min, energy_max
                )
                select ins.id::bigint as id, ins.on_date, ins.start_minute, ins.end_minute,
                       ins.persona_id::bigint as persona_id, ins.genres,
                       ins.energy_min::double precision as energy_min, ins.energy_max::double precision as energy_max,
                       {ScheduleShowJoinColumns.Select}
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
        }
        catch (PostgresException ex) when (ex.SqlState == ExclusionViolation)
        {
            return new ScheduleSpecialCreateResult.Overlap();
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            return new ScheduleSpecialCreateResult.UnknownReference();
        }

        SpecialsChanged?.Invoke();
        return new ScheduleSpecialCreateResult.Created(ToSpecial(row));
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

    ScheduleSpecial ToSpecial(SpecialRow row) => new(
        row.Id, row.OnDate, row.StartMinute, row.EndMinute, row.PersonaId,
        row.Genres, row.EnergyMin, row.EnergyMax, ToShowSummary(row), row.ShowId);

    /// <summary>Mirrors <see cref="ScheduleRepository"/>'s own <c>ToShowSummary</c> — see that
    /// method's remarks for why <c>ShowName</c> is checked alongside <c>ShowId</c>, for
    /// <c>ShowSlug</c>'s own <c>""</c>-default fallback (PLAN T285, SPEC F127.8 review F4), and for
    /// <c>ShowRotationJson</c>'s own never-throw WARN-and-normalize parse (PLAN T360, SPEC F152.3/
    /// F152.4).</summary>
    ShowSummary? ToShowSummary(SpecialRow row) =>
        row.ShowId is { } showId && row.ShowName is { } showName
            ? new ShowSummary(showId, showName, row.ShowTagline, row.ShowFlavor)
            {
                Slug = row.ShowSlug ?? "",
                Rotation = RotationEnvelopeCodec.Parse(row.ShowRotationJson, showName, logger),
            }
            : null;
}
