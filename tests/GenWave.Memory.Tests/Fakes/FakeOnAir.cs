using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>A hand-set on-air snapshot: tests move it between shows the way the schedule resolver would.</summary>
public sealed class FakeOnAir : IOnAirSnapshotSource
{
    public OnAirSnapshot? Current { get; set; }

    /// <summary>A named show hosted by <paramref name="personaId"/> starting at <paramref name="startMinute"/> of its day.</summary>
    public static OnAirSnapshot Show(long showId, string name, long personaId, int startMinute) =>
        new(
            new ScheduleSegment(null, DayOfWeek.Tuesday, startMinute, startMinute + 240, personaId, null, null, null),
            personaId,
            SegmentEnvelope.StationDefault,
            null,
            null,
            new ShowSummary(showId, name, null, null));

    /// <summary>An unscheduled gap: the schedule resolved, and nothing (no show) is on.</summary>
    public static OnAirSnapshot Gap() => new(null, null, SegmentEnvelope.StationDefault, null, null);
}
