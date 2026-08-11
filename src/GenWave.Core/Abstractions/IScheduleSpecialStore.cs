using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F120.1, STORY-317, PLAN T258) — CRUD access to <c>station.schedule_special</c>, the
/// dated-specials tail that shadows <see cref="IScheduleStore"/>'s weekly grid for a single calendar
/// date's span. Deliberately minimal (SPEC F120.1's own "CRUD minimal" instruction, PLAN T258): list +
/// create + delete only — no update (a caller wanting to change a special deletes and re-creates it;
/// nothing in this epic's own scope needs an in-place edit). Shipped the same "dark seam" way
/// <see cref="IScheduleStore"/> itself did at T118 and <see cref="IShowStore"/> did at T239: this
/// interface, its implementation, and its DI registration extension all existed after PLAN T258 with no
/// Host composition root calling the registration and no consumer reading from it — the resolver's own
/// specials-first rung (<c>GenWave.Orchestration.ScheduleResolver</c>) took a specials LIST as a plain
/// argument, never this store, so it stayed reachable and unit-testable with zero DI/database
/// involvement even before this seam went live. That dark period is over as of PLAN T260:
/// <c>GenWave.Host.Api.SpecialsController</c> (PLAN T259) is this seam's first Host consumer, for
/// authoring; <c>GenWave.Orchestration.CachingScheduleResolver</c> (PLAN T260) is the second, and the
/// one that makes a written special shadow the weekly grid LIVE, on the production feeder tick — see
/// that type's own remarks for the caching/invalidation design, and <c>ScheduleResolver</c>'s own
/// remarks for the specials-first rung it feeds.
/// </summary>
public interface IScheduleSpecialStore
{
    /// <summary>
    /// Every <c>station.schedule_special</c> row on or after <paramref name="fromDate"/>, ordered by
    /// date then start minute — the store stays time-agnostic itself (no wall clock dependency, mirrors
    /// <see cref="IScheduleStore.LoadWeekAsync"/>'s own "Postgres is the only truth" posture): the
    /// caller supplies "today" (station-local, via whatever clock seam it already holds) rather than
    /// this method reading one. Unbounded above <paramref name="fromDate"/> deliberately — specials are
    /// rare rows (SPEC F120.1's own framing), so an admin list view showing "every special from today
    /// forward" carries no real pagination concern; a caller wanting a narrower window (e.g.
    /// <c>CachingScheduleResolver</c>'s own today+tomorrow lookahead, PLAN T260) filters the returned
    /// list itself rather than this method growing a second date parameter no other caller needs.
    /// </summary>
    Task<IReadOnlyList<ScheduleSpecial>> ListUpcomingAsync(DateOnly fromDate, CancellationToken ct);

    /// <summary>
    /// Inserts <paramref name="special"/> and returns a <see cref="ScheduleSpecialCreateResult"/>: on
    /// success, <see cref="ScheduleSpecialCreateResult.Created"/> carries the persisted row
    /// (store-assigned <c>Id</c>, and <c>Show</c> re-resolved by the same LEFT JOIN
    /// <see cref="ListUpcomingAsync"/> uses, never fabricated from <paramref name="special"/>'s own
    /// possibly-stale <c>Show</c> field — mirrors <c>ScheduleRepository</c>'s own "<c>ShowId</c> is
    /// write-authoritative, <c>Show</c> is a load-time projection" split). No application-side
    /// PRE-validation runs here (deliberately — SPEC F120.1's own "CRUD minimal" instruction, and
    /// unlike <see cref="IScheduleStore.ReplaceWeekAsync"/> this method has no per-cell error contract
    /// to report through): the database's own CHECK/EXCLUDE/FK constraints (db/36) are the ONLY line of
    /// defense against an off-grid minute, an overlapping span, or an unknown persona/show id — but
    /// unlike <see cref="IScheduleStore.ReplaceWeekAsync"/>'s own "raw <c>PostgresException</c>
    /// straight back" contract, THIS method translates db/36's own two possible rejections into
    /// <see cref="ScheduleSpecialCreateResult.Overlap"/> (the per-date EXCLUDE) and
    /// <see cref="ScheduleSpecialCreateResult.UnknownReference"/> (either FK) itself — a POST-hoc
    /// translation of the database's own rejection, not a validation this method performs, the same
    /// distinction <c>ShowRepository</c>'s own unique/foreign-key catches already draw (PLAN T259: a
    /// controller consuming this store may never reference <c>Npgsql.PostgresException</c> itself — see
    /// <see cref="ScheduleSpecialCreateResult"/>'s own remarks for why). A CHECK violation (an off-grid
    /// minute) is not one of the two cases above — <c>GenWave.Host.Api.SpecialsController</c>'s own
    /// app-side range validation is what an ordinary caller hits before this method is ever called,
    /// so an off-grid minute reaching this far at all is a caller bug, not a modeled outcome; the
    /// underlying <c>Npgsql.PostgresException</c> still propagates unmodified in that case. Raises
    /// <see cref="SpecialsChanged"/> exactly once, only on <see cref="ScheduleSpecialCreateResult.Created"/>.
    /// </summary>
    Task<ScheduleSpecialCreateResult> CreateAsync(ScheduleSpecial special, CancellationToken ct);

    /// <summary>
    /// Deletes the special identified by <paramref name="id"/>. Returns <see langword="true"/> and
    /// raises <see cref="SpecialsChanged"/> exactly once if a row was actually removed,
    /// <see langword="false"/> (no event) if no such row exists — mirrors
    /// <c>IScheduleStore.AssignShowAsync</c>'s own "never raised on a no-op" discipline.
    /// </summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct);

    /// <summary>
    /// Raised synchronously right after a successful <see cref="CreateAsync"/> or <see cref="DeleteAsync"/>
    /// commit — the sibling of <see cref="IScheduleStore.WeekChanged"/> (SPEC F120's own design note: "a
    /// sibling event") <c>GenWave.Orchestration.CachingScheduleResolver</c> (PLAN T260) subscribes to
    /// for invalidation. Never raised on a no-op or a rejected write.
    /// </summary>
    event Action? SpecialsChanged;
}
