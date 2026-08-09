using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F91.2/F91.3 (STORY-241, PLAN T119) — the pure function that answers "who/what is on the air
/// right now": resolves a station-local wall-clock instant against a <see cref="ScheduleWeekSnapshot"/>
/// into an <see cref="OnAirSnapshot"/>. This is the resolver ARCHITECTURE.md's 🕐 Format Clock section
/// designed as the long-named "ActiveEnvelopeResolver," now built.
///
/// <para>
/// Deliberately holds NO snapshot of its own — <see cref="Resolve"/> takes the
/// <see cref="ScheduleWeekSnapshot"/> as an explicit argument on every call, so this type stays a pure
/// function over (snapshot, wall clock) and is trivially unit-testable with a hand-built snapshot and a
/// <see cref="TimeProvider"/> double — no DB, no caching, no subscription. <see cref="CachingScheduleResolver"/>
/// is the thin wrapper that holds the in-memory snapshot (SPEC F91.3: "the 3s feeder tick performs no
/// schedule query") and calls into this type once per tick.
/// </para>
///
/// <para>
/// Resolves station-local "now" through the live <see cref="IStationClockProvider"/> seam
/// (<c>Station:Timezone</c>, gh-#224) when the composition supplies one — the seam's
/// <see cref="IStationClockProvider.Zone"/> also drives the DST boundary math below, so "which slot
/// is now" and "when does it end" are computed in the SAME zone — otherwise via
/// <c>TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone)</c> (the
/// container's clock, pre-gh-#224 behavior unchanged). Same optional-seam posture as
/// <see cref="PersonaRanker"/>'s own <c>StationLocalNow</c> and
/// <c>GenWave.Tts.LlmPromptBuilder.BuildStationClockLine</c>, so a
/// gh-#13 DJ prompt, a <c>TasteContext</c> gate, and the on-air grid all agree on what time it is.
/// A live <c>Station:Timezone</c> edit therefore instantly shifts which slot is "now" — by design.
/// </para>
///
/// <para>
/// <b>DST (SPEC F91.2):</b> a segment boundary's wall-clock minute is converted to a real instant via
/// <see cref="TimeZoneInfo"/>, choosing one deterministic rule for each way a wall clock lies —
/// spring-forward's missing hour resolves FORWARD to the next wall-clock minute that exists (the
/// crossing segment airs short); fall-back's repeated hour resolves to its FIRST occurrence (the
/// crossing segment airs long) — UNLESS that first occurrence has already elapsed relative to "now",
/// in which case the second occurrence is used instead, so a boundary can never resolve into the past
/// (PLAN T119 review F2). See <see cref="ResolveWallClockInstant"/>.
/// </para>
/// </summary>
public sealed class ScheduleResolver(
    TimeProvider timeProvider,
    IStationDefaultEnvelopeSource defaultEnvelopeSource,
    IStationClockProvider? stationClock = null)
{
    const int MinutesPerDay = 1440;
    const int MinutesPerWeek = MinutesPerDay * 7;

    /// <summary>Resolves <paramref name="snapshot"/> against station-local "now" (SPEC F91.2, F91.3).</summary>
    public OnAirSnapshot Resolve(ScheduleWeekSnapshot snapshot)
    {
        var zone = stationClock?.Zone ?? timeProvider.LocalTimeZone;
        var localNow = stationClock?.LocalNow ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var today = localNow.DayOfWeek;
        var nowMinute = localNow.Hour * 60 + localNow.Minute;
        var todayDate = localNow.Date;

        var current = FindCurrent(snapshot.Segments, today, nowMinute);
        return current is null
            ? ResolveGap(snapshot.Segments, zone, todayDate, today, nowMinute, localNow)
            : ResolveCurrent(snapshot.Segments, current, zone, todayDate, today, nowMinute, localNow);
    }

    OnAirSnapshot ResolveCurrent(
        IReadOnlyList<ScheduleSegment> segments, ScheduleSegment current, TimeZoneInfo zone,
        DateTime todayDate, DayOfWeek today, int nowMinute, DateTimeOffset now)
    {
        var envelope = BuildSegmentEnvelope(current);
        var (boundaryDay, boundaryMinute) = NormalizeMinute(current.Day, current.EndMinute);
        var boundaryAt = ResolveBoundaryInstant(todayDate, today, nowMinute, boundaryDay, boundaryMinute, zone, now);
        var next = FindAdjacent(segments, boundaryDay, boundaryMinute);
        return new OnAirSnapshot(current, current.PersonaId, envelope, boundaryAt, next);
    }

    OnAirSnapshot ResolveGap(
        IReadOnlyList<ScheduleSegment> segments, TimeZoneInfo zone, DateTime todayDate, DayOfWeek today, int nowMinute,
        DateTimeOffset now)
    {
        var envelope = defaultEnvelopeSource.Current;
        var next = FindNextUpcoming(segments, today, nowMinute);
        if (next is null)
            return new OnAirSnapshot(Segment: null, PersonaId: null, envelope, BoundaryAt: null, NextSegment: null);

        var boundaryAt = ResolveBoundaryInstant(todayDate, today, nowMinute, next.Day, next.StartMinute, zone, now);
        return new OnAirSnapshot(Segment: null, PersonaId: null, envelope, boundaryAt, next);
    }

    SegmentEnvelope BuildSegmentEnvelope(ScheduleSegment segment)
    {
        var stationDefault = defaultEnvelopeSource.Current;
        return new SegmentEnvelope(
            ToTimeOnly(segment.StartMinute),
            ToTimeOnly(segment.EndMinute),
            segment.Genres ?? stationDefault.Genres,
            new EnergyRange(
                segment.EnergyMin ?? stationDefault.EnergyRange.Min,
                segment.EnergyMax ?? stationDefault.EnergyRange.Max));
    }

    static ScheduleSegment? FindCurrent(IReadOnlyList<ScheduleSegment> segments, DayOfWeek day, int minute) =>
        segments.FirstOrDefault(s => s.Day == day && s.StartMinute <= minute && minute < s.EndMinute);

    /// <summary>The segment (if any) whose start is exactly the given (already-normalized) day/minute —
    /// i.e. the segment that plays on immediately once the current one ends, with no gap between.</summary>
    static ScheduleSegment? FindAdjacent(IReadOnlyList<ScheduleSegment> segments, DayOfWeek day, int minute) =>
        segments.FirstOrDefault(s => s.Day == day && s.StartMinute == minute);

    /// <summary>The segment whose start is nearest in the future, searching forward cyclically across
    /// the whole week (SPEC F91.1's grid repeats every 7 days) — used only for a gap "now", so a
    /// distance of zero (a segment starting AT this exact instant) can never occur: that segment would
    /// already have been <see cref="FindCurrent"/>'s match.</summary>
    static ScheduleSegment? FindNextUpcoming(IReadOnlyList<ScheduleSegment> segments, DayOfWeek day, int minute)
    {
        var nowWeekly = (int)day * MinutesPerDay + minute;
        ScheduleSegment? best = null;
        var bestDistance = int.MaxValue;

        foreach (var segment in segments)
        {
            var startWeekly = (int)segment.Day * MinutesPerDay + segment.StartMinute;
            var distance = Mod(startWeekly - nowWeekly, MinutesPerWeek);
            if (distance == 0 || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = segment;
        }

        return best;
    }

    /// <summary>Rolls a day-of-week/minute-of-day pair forward when <paramref name="minute"/> is the
    /// schema's own end-of-day value (1440, i.e. midnight) — a <see cref="ScheduleSegment.EndMinute"/>
    /// of 1440 means "runs to midnight," which is wall-clock minute 0 of the NEXT day.</summary>
    static (DayOfWeek Day, int Minute) NormalizeMinute(DayOfWeek day, int minute) =>
        minute >= MinutesPerDay
            ? ((DayOfWeek)(((int)day + 1) % 7), minute - MinutesPerDay)
            : (day, minute);

    /// <summary>Converts a (day-of-week, minute-of-day) boundary target into a real instant, anchored at
    /// <paramref name="todayDate"/>/<paramref name="today"/>/<paramref name="nowMinute"/> ("now") so the
    /// target always lands on the nearest matching day-of-week strictly in the future — including the
    /// one legitimate same-weekday-but-a-week-away case: the target's minute has already passed today.</summary>
    static DateTimeOffset ResolveBoundaryInstant(
        DateTime todayDate, DayOfWeek today, int nowMinute, DayOfWeek targetDay, int targetMinute, TimeZoneInfo zone,
        DateTimeOffset now)
    {
        var daysAhead = Mod((int)targetDay - (int)today, 7);
        if (daysAhead == 0 && targetMinute <= nowMinute)
            daysAhead = 7;

        var targetDate = todayDate.AddDays(daysAhead);
        var wallClock = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMinutes(targetMinute);

        return ResolveWallClockInstant(wallClock, zone, now);
    }

    /// <summary>
    /// Converts an "Unspecified" local wall-clock <paramref name="wallClock"/> into a real instant in
    /// <paramref name="zone"/> (SPEC F91.2, PLAN T119) — delegates to
    /// <see cref="WallClockInstantResolver.Resolve"/>, the shared DST rule
    /// <see cref="ClockAnchoredImagingProducer"/>'s own top-of-hour math also uses (PLAN T230 review
    /// F2) — see that helper's own remarks for the two rules (spring-forward steps forward, fall-back
    /// resolves to its first occurrence unless already elapsed) and why a boundary resolving to an
    /// elapsed instant would violate <see cref="OnAirSnapshot"/>'s "next instant" contract.
    /// <paramref name="now"/> is the real instant this resolution is anchored to — used only to break
    /// the fall-back tie, never to change which day/minute was targeted. This method's behavior is
    /// unchanged by the extraction (PLAN T230 review F2 requirement): byte-identical to its own prior
    /// inline implementation.
    /// </summary>
    static DateTimeOffset ResolveWallClockInstant(DateTime wallClock, TimeZoneInfo zone, DateTimeOffset now) =>
        WallClockInstantResolver.Resolve(wallClock, zone, now);

    /// <summary>Converts a schedule minute-of-day into a display <see cref="TimeOnly"/>. A schema
    /// <c>EndMinute</c> of <see cref="MinutesPerDay"/> (1440) means "runs to midnight" — but
    /// <see cref="TimeOnly"/> cannot represent 24:00; naively wrapping it would produce 00:00, making the
    /// envelope's <c>EndsAt</c> read as BEFORE its own <c>StartsAt</c>. 1440 (and anything at/above it)
    /// therefore clamps to <see cref="TimeOnly.MaxValue"/> (23:59:59.9999999) instead of wrapping.</summary>
    static TimeOnly ToTimeOnly(int minute) => minute switch
    {
        <= 0 => TimeOnly.MinValue,
        >= MinutesPerDay => TimeOnly.MaxValue,
        _ => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute)),
    };

    static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
