namespace GenWave.Orchestration;

/// <summary>
/// SPEC F91.2 (PLAN T230 review F2) — the ONE place a naive "Unspecified" local wall-clock instant is
/// turned into a real <see cref="DateTimeOffset"/> across a DST transition. Extracted out of
/// <see cref="ScheduleResolver"/>'s own private <c>ResolveWallClockInstant</c> (its original home,
/// still the method every caller in this assembly reaches it through) once
/// <see cref="ClockAnchoredImagingProducer"/> needed the EXACT same two rules for its own top-of-hour
/// math — two independent copies of spring-forward/fall-back arithmetic is exactly the kind of drift a
/// shared internal helper exists to prevent. Sharing this closed a real bug (PLAN T230 review F3):
/// <see cref="ClockAnchoredImagingProducer"/>'s own prior copy omitted the already-elapsed fall-back
/// clause below, so a target hour landing inside a fall-back's repeated wall-clock window could resolve
/// to an ALREADY-PAST instant and re-arm past-dated on every subsequent tick — reproducible in any
/// zone whose fall-back exceeds one hour (e.g. Antarctica/Troll's 2-hour shift), where the target's
/// first occurrence can already have elapsed by the time a later tick lands during the same repeated
/// hour's second pass.
///
/// <para>
/// Internal, not public: both callers live in this assembly (<c>GenWave.Orchestration</c>); nothing
/// outside it needs this rule.
/// </para>
/// </summary>
internal static class WallClockInstantResolver
{
    /// <summary>
    /// Converts an "Unspecified" local wall-clock <paramref name="wallClock"/> into a real instant in
    /// <paramref name="zone"/>, choosing a deterministic rule for the two ways a wall clock lies (SPEC
    /// F91.2). <paramref name="now"/> is the real instant this resolution is anchored to — used only to
    /// break the fall-back tie below, never to change which wall-clock minute was targeted.
    /// <list type="bullet">
    /// <item>Spring-forward gap (the wall time never happens, e.g. 02:15 the morning the clock jumps
    /// 02:00→03:00): resolves FORWARD to the first wall-clock minute that DOES exist — the missing hour
    /// is simply skipped.</item>
    /// <item>Fall-back overlap (the wall time happens twice, e.g. 01:30 the morning the clock repeats
    /// 02:00→01:00): resolves to the FIRST occurrence by default — the offset still in effect before the
    /// clocks roll back. <see cref="TimeZoneInfo.GetAmbiguousTimeOffsets"/> returns the pre-transition
    /// offset as the numerically LARGER of the two candidates in every zone — a fall-back is defined as
    /// the UTC offset strictly DECREASING, so <c>Max()</c> always names the first occurrence,
    /// universally, regardless of the shift's SIZE (a normal 1-hour DST fall-back or a 2-hour zone like
    /// Antarctica/Troll alike). But the first occurrence can itself already be in the past by the time
    /// this runs — the SECOND pass through that same repeated window. Once the first-occurrence
    /// candidate is <c>&lt;= now</c>, this falls through to the second (later, <c>Min()</c>) occurrence
    /// instead, so a resolved instant can never already be in the past.</item>
    /// </list>
    /// </summary>
    internal static DateTimeOffset Resolve(DateTime wallClock, TimeZoneInfo zone, DateTimeOffset now)
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
}
