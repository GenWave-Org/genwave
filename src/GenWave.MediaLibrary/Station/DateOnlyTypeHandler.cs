using System.Data;
using Dapper;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's own <c>SqlMapper.LookupDbType</c> table predates <see cref="DateOnly"/> (added to .NET/
/// Npgsql well after Dapper's parameter-binding code was written) — passing a bare <see cref="DateOnly"/>
/// as a query parameter throws <see cref="NotSupportedException"/> before the command ever reaches
/// Postgres, even though Npgsql itself maps <c>date</c> to <see cref="DateOnly"/> natively. This handler
/// closes that gap for both directions (Dapper calls <see cref="Parse"/> on the way IN from a reader too,
/// though Npgsql already handles that side correctly on its own — registering both keeps this handler's
/// behavior uniform rather than solving only the half that currently breaks).
///
/// <para>
/// Registered once, process-wide, by <see cref="MediaLibraryServiceCollectionExtensions.AddMediaLibrary"/>
/// (PLAN T258 review MF2 — <c>station.schedule_special.on_date</c> is this codebase's first
/// <see cref="DateOnly"/>-typed column) right beside <c>DefaultTypeMap.MatchNamesWithUnderscores</c>,
/// NOT by <see cref="SpecialsRepository"/>'s own construction: at T258 that store shipped dark (no Host
/// call site at all yet), so a registration tied to ITS construction would never have fired in
/// production before <c>SpecialsController</c> (PLAN T259) and <c>CachingScheduleResolver</c> (PLAN
/// T260) later became its two Host call sites. <c>AddMediaLibrary</c> runs unconditionally at Host
/// startup regardless of which individual store DI extensions are wired, so any later repository adding
/// another date-only column inherits this registration for free — a claim that is only true because it
/// lives here, not on a dark seam.
/// </para>
/// </summary>
sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public static readonly DateOnlyTypeHandler Instance = new();

    public override void SetValue(IDbDataParameter parameter, DateOnly value) => parameter.Value = value;

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly dateOnly => dateOnly,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        _ => throw new NotSupportedException($"Cannot convert {value.GetType()} to {nameof(DateOnly)}."),
    };
}
