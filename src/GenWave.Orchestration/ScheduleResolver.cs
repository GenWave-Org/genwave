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
/// Resolves station-local "now" via
/// <c>TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone)</c> — the exact
/// idiom <see cref="PersonaRanker"/>'s own <c>StationLocalNow</c> and
/// <c>GenWave.Tts.LlmPromptBuilder.BuildStationClockLine</c> already use for "station-local now", so a
/// gh-#13 DJ prompt, a <c>TasteContext</c> gate, and the on-air grid all agree on what time it is.
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
public sealed class ScheduleResolver(TimeProvider timeProvider, IStationDefaultEnvelopeSource defaultEnvelopeSource)
{
    const int MinutesPerDay = 1440;
    const int MinutesPerWeek = MinutesPerDay * 7;

    /// <summary>Resolves <paramref name="snapshot"/> against station-local "now" (SPEC F91.2, F91.3).</summary>
    public OnAirSnapshot Resolve(ScheduleWeekSnapshot snapshot)
    {
        var zone = timeProvider.LocalTimeZone;
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
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
    /// <paramref name="zone"/>, choosing a deterministic rule for the two ways a wall clock lies (SPEC
    /// F91.2, PLAN T119). <paramref name="now"/> is the real instant this resolution is anchored to —
    /// used only to break the fall-back tie below, never to change which day/minute was targeted.
    /// <list type="bullet">
    /// <item>Spring-forward gap (the wall time never happens, e.g. 02:15 the morning the clock jumps
    /// 02:00→03:00): resolves FORWARD to the first wall-clock minute that DOES exist — the missing hour
    /// is simply skipped, which is exactly why a segment spanning the jump airs an hour short.</item>
    /// <item>Fall-back overlap (the wall time happens twice, e.g. 01:30 the morning the clock repeats
    /// 02:00→01:00): resolves to the FIRST occurrence by default — the offset still in effect before the
    /// clocks roll back — which is exactly why a segment spanning the repeat airs an hour long.
    /// <see cref="TimeZoneInfo.GetAmbiguousTimeOffsets"/> returns the pre-transition offset as the
    /// numerically LARGER of the two candidates in every zone, not merely America/Denver's -06:00/-07:00
    /// pair — a fall-back is defined as the UTC offset strictly DECREASING, so <c>Max()</c> always names
    /// the first occurrence, universally. But the first occurrence can itself already be in the past by
    /// the time this runs: the SECOND pass through that same repeated hour (PLAN T119 review F2). A
    /// boundary resolving to an elapsed instant would violate <see cref="OnAirSnapshot"/>'s "next
    /// instant" contract, so once the first-occurrence candidate is <c>&lt;= now</c>, this falls through
    /// to the second (later, <c>Min()</c>) occurrence instead.</item>
    /// </list>
    /// </summary>
    static DateTimeOffset ResolveWallClockInstant(DateTime wallClock, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (zone.IsInvalidTime(wallClock))
        {
            var probe = wallClock;
            while (zone.IsInvalidTime(probe))
                probe = probe.AddMinutes(1);
            return new DateTimeOffset(probe, zone.GetUtcOffset(probe));
        }

        if (zone.IsAmbiguousTime(wallClock))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(wallClock);
            var firstOccurrence = new DateTimeOffset(wallClock, offsets.Max());
            return firstOccurrence > now ? firstOccurrence : new DateTimeOffset(wallClock, offsets.Min());
        }

        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
    }

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
