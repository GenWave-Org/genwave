using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// The identity of an on-air run (SPEC F218.15). A named show is one run however many adjacent slots it spans
/// (keyed on its id); a slot with no show attached is a shift, keyed on the slot itself. A segment's own id is
/// nullable (specials, ad-hoc), so the slot is identified by its day, minutes and persona instead.
/// </summary>
readonly record struct RunKey(long? ShowId, DayOfWeek Day, int StartMinute, int EndMinute, long? PersonaId)
{
    /// <summary>A named show: the slot fields are unused placeholders; the non-null show id alone sets it apart from any shift.</summary>
    public static RunKey ForShow(long showId) => new(showId, default, 0, 0, null);

    public static RunKey ForShift(ScheduleSegment segment) =>
        new(null, segment.Day, segment.StartMinute, segment.EndMinute, segment.PersonaId);
}
