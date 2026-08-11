namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of
/// <see cref="Abstractions.IScheduleSpecialStore.CreateAsync"/> (SPEC F120.1, STORY-317, PLAN T259).
/// Mirrors <see cref="ShowWriteResult"/>/<see cref="PersonaWriteResult"/>'s own closed-hierarchy shape:
/// a sealed record per case, a private constructor on the abstract base closing the hierarchy so a
/// caller pattern-matches exhaustively with no discard arm.
///
/// <para>
/// <b>Why this type exists (PLAN T259 correction to T258's own shipped doc comments).</b>
/// <see cref="SpecialsRepository"/> was originally documented to let db/36's own CHECK/EXCLUDE/FK
/// rejections propagate as a raw <see cref="Npgsql.PostgresException"/>, mirroring
/// <see cref="Abstractions.IScheduleStore.ReplaceWeekAsync"/>'s own "the store never catches, the
/// caller does" contract. <c>GenWave.Architecture.Tests</c>' L2 law (Npgsql/Dapper confined to
/// <c>GenWave.MediaLibrary</c>'s repository layer, <c>GenWave.MediaLibrary.Station</c>/
/// <c>GenWave.MediaLibrary.Catalog</c> only — no baseline exemption for new code) forbids that shape
/// for a NEW controller the way <c>ScheduleController</c>'s own pre-existing catch is grandfathered
/// (tracked debt, gh-#406) — a controller may never reference <c>Npgsql.PostgresException</c> at all.
/// <see cref="Abstractions.IScheduleSpecialStore.CreateAsync"/>'s own SQLSTATE-to-result translation
/// therefore moved INTO <see cref="SpecialsRepository"/> (still "no app-side PRE-validation", the
/// original design intent — this is a POST-hoc translation of the database's own rejection, exactly
/// the shape <see cref="ShowWriteResult"/>/<see cref="PersonaWriteResult"/>'s own stores already use
/// for their unique/foreign-key violations), and <see cref="SpecialsController.Create"/> (PLAN T259)
/// consumes this typed result instead of catching the exception itself.
/// </para>
/// </summary>
public abstract record ScheduleSpecialCreateResult
{
    private ScheduleSpecialCreateResult() { }

    /// <summary>The special was created; <see cref="Special"/> is the persisted row (store-assigned
    /// <c>Id</c>, <c>Show</c> re-resolved by the same LEFT JOIN <c>ListUpcomingAsync</c> uses).</summary>
    public sealed record Created(ScheduleSpecial Special) : ScheduleSpecialCreateResult;

    /// <summary>The submitted span overlaps another special on the SAME date — db/36's own per-date
    /// EXCLUDE constraint (SPEC F120.1), caught as SQLSTATE 23P01 (<c>exclusion_violation</c>).</summary>
    public sealed record Overlap : ScheduleSpecialCreateResult;

    /// <summary>The submitted persona or show id does not exist — db/36's own FK <c>ON DELETE
    /// RESTRICT</c>, caught as SQLSTATE 23503 (<c>foreign_key_violation</c>). This is a RACE backstop,
    /// never the primary signal: <see cref="SpecialsController.Create"/>'s own app-side persona/show
    /// existence check (SPEC F120.1) is what an ordinary submission with an unknown id hits first —
    /// this case only fires when a persona/show that passed that check is deleted by a concurrent
    /// caller before this insert commits (mirrors <c>ScheduleController.Put</c>'s own documented
    /// persona-race case).</summary>
    public sealed record UnknownReference : ScheduleSpecialCreateResult;
}
