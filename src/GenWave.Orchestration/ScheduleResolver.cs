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
///
/// <para>
/// <b>Specials-first rung (SPEC F120.2, PLAN T258): TODAY shadows, TODAY+TOMORROW race the boundary.</b>
/// <see cref="Resolve"/>'s optional <c>specials</c> parameter is the ONLY diff this rung makes — a
/// caller that never has specials to offer (e.g. a hand-built <see cref="ScheduleWeekSnapshot"/> in a
/// pure-function spec) stays byte-identical, since the parameter defaults to none.
/// <see cref="CachingScheduleResolver"/> is PLAN T260's own such caller: it feeds this parameter from
/// <c>IScheduleSpecialStore</c> on every resolve (see its own remarks). A dated row whose
/// <see cref="ScheduleSegment"/>-shaped projection covers "now" (see <see cref="ProjectSpecial"/>) is
/// fed through the SAME <see cref="EffectiveAssignment"/>/<see cref="BuildSegmentEnvelope"/> pipeline a
/// weekly block already uses — no downstream consumer of <see cref="OnAirSnapshot"/> gains a
/// special-aware branch, because there is nothing on <see cref="OnAirSnapshot"/> that could tell it
/// apart from an ordinary block (beyond <see cref="ScheduleSegment.Id"/>'s own sign — see
/// <see cref="ProjectSpecial"/>'s remarks — which no consumer treats as anything but an opaque
/// diagnostic token). Only TODAY's specials (by station-local date) can ever SHADOW "now" —
/// <see cref="Resolve"/> re-reads the clock on every call and holds no state, so a caller may safely
/// hand a multi-day window (PLAN T260's own bounded lookahead) without this method ever shadowing a day
/// that is not "today" for THIS call.
/// </para>
///
/// <para>
/// <b>The boundary-peek (PLAN T258 review MF3).</b> Shadowing alone is not enough: a special dated
/// TOMORROW, starting at 00:00, is invisible to <see cref="OnAirSnapshot.BoundaryAt"/>/
/// <see cref="OnAirSnapshot.NextSegment"/> right up until the exact instant it becomes "today" — and
/// production hand-off machinery (sign-on/sign-off ceremony included) arms off <c>BoundaryAt</c>, not
/// off a fresh re-resolve at the stroke of midnight. So the "what happens next" computation (never the
/// "what is on now" shadow) also considers TOMORROW's specials: <see cref="ResolveGap"/>'s race and
/// <see cref="ResolveNext"/>'s exact-start lookup both extend one day ahead. A weekly block or gap can
/// only ever run up TO midnight (SPEC F91.1's own <c>end_minute &lt;= 1440</c> CHECK) — so tomorrow's
/// earliest special can only ever be a NEXT-boundary candidate at minute 0, never able to truncate a
/// weekly block early the way a same-day special can (<see cref="ResolveCurrent"/>'s own remarks). This
/// stays a ONE-day peek, not an unbounded lookahead — the same "specials are rare rows, bound the
/// window honestly" posture PLAN T258's own design notes set for the (separate, T260) resolver cache.
/// </para>
///
/// <para>
/// Wired into <see cref="CachingScheduleResolver"/>'s live cache/invalidation as of PLAN T260 (PLAN
/// T258 shipped this rung dark, same posture <see cref="IScheduleStore"/> itself shipped at T118, and
/// PLAN T259 made <c>IScheduleSpecialStore</c> a Host call site for authoring only) — a special written
/// through <c>SpecialsController</c> now shadows the weekly grid on the production feeder tick within
/// one cache cycle. See <see cref="CachingScheduleResolver"/>'s own remarks for exactly how it feeds
/// this method's <c>specials</c> parameter and what invalidates its cache.
/// </para>
/// </summary>
public sealed class ScheduleResolver(
    TimeProvider timeProvider,
    IStationDefaultEnvelopeSource defaultEnvelopeSource,
    IStationClockProvider? stationClock = null)
{
    const int MinutesPerDay = 1440;
    const int MinutesPerWeek = MinutesPerDay * 7;

    /// <summary>Resolves <paramref name="snapshot"/> (and, for TODAY only as a shadow / TODAY+TOMORROW
    /// as a boundary race, <paramref name="specials"/> — SPEC F120.2) against station-local "now" (SPEC
    /// F91.2, F91.3). <paramref name="specials"/> defaults to none, so every pre-T258 call site is
    /// unaffected.</summary>
    public OnAirSnapshot Resolve(ScheduleWeekSnapshot snapshot, IReadOnlyList<ScheduleSpecial>? specials = null)
    {
        var zone = stationClock?.Zone ?? timeProvider.LocalTimeZone;
        var localNow = stationClock?.LocalNow ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var today = localNow.DayOfWeek;
        var tomorrow = (DayOfWeek)(((int)today + 1) % 7);
        var nowMinute = localNow.Hour * 60 + localNow.Minute;
        var todayDate = localNow.Date;
        var onDate = DateOnly.FromDateTime(todayDate);
        var tomorrowDate = onDate.AddDays(1);

        var allSpecials = specials ?? [];
        // Specials-first rung: only TODAY's rows can ever shadow "now" (see this type's own class
        // remarks for why a future-dated row in the list is safely inert for shadowing purposes).
        var todaysSpecials = allSpecials.Where(s => s.OnDate == onDate).OrderBy(s => s.StartMinute).ToList();
        // The boundary-peek (PLAN T258 review MF3): never shadows, only races for "what happens next"
        // when today's own answer runs right up to midnight.
        var tomorrowsSpecials = allSpecials.Where(s => s.OnDate == tomorrowDate).OrderBy(s => s.StartMinute).ToList();

        var currentSpecial = FindCurrentSpecial(todaysSpecials, nowMinute);
        if (currentSpecial is not null)
        {
            return ResolveCurrentSpecial(
                snapshot.Segments, todaysSpecials, tomorrowsSpecials, currentSpecial, today, tomorrow, zone, todayDate, nowMinute, localNow);
        }

        var current = FindCurrent(snapshot.Segments, today, nowMinute);
        return current is null
            ? ResolveGap(snapshot.Segments, todaysSpecials, tomorrowsSpecials, zone, todayDate, today, tomorrow, nowMinute, localNow)
            : ResolveCurrent(snapshot.Segments, todaysSpecials, tomorrowsSpecials, current, zone, todayDate, today, tomorrow, nowMinute, localNow);
    }

    /// <summary>
    /// Station-local "today" (SPEC F91.2's own clock seam), resolved fresh on every call through the
    /// SAME optional <see cref="IStationClockProvider"/>-over-<see cref="TimeProvider"/> resolution
    /// <see cref="Resolve"/> itself uses to compute its own <c>onDate</c> — pulled out as its own pure,
    /// side-effect-free public member (PLAN T260 review SF4) so a caller that needs ONLY the date, not
    /// a full <see cref="Resolve"/>, has one place to ask rather than a hand-rolled second copy of this
    /// arithmetic. <see cref="CachingScheduleResolver"/> is exactly that caller: anchoring its specials
    /// cache's reload date through THIS method (rather than resolving its own clock independently)
    /// makes "what day does the cache think it is" and "what day does the resolver think it is"
    /// structurally the SAME answer, not two independently-computed ones that could drift apart.
    /// </summary>
    public DateOnly StationToday()
    {
        var zone = stationClock?.Zone ?? timeProvider.LocalTimeZone;
        var localNow = stationClock?.LocalNow ?? TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        return DateOnly.FromDateTime(localNow.Date);
    }

    /// <summary>
    /// A special covers "now" — SPEC F120.2's shadow itself. <paramref name="special"/> is projected
    /// into a <see cref="ScheduleSegment"/> (<see cref="ProjectSpecial"/>) and fed through the same
    /// envelope/identity pipeline <see cref="ResolveCurrent"/> uses for a weekly block, so this method's
    /// own shape mirrors that one deliberately. <see cref="OnAirSnapshot.NextSegment"/> after the
    /// special ends may resume MID a weekly block (the special's own end need not land on any weekly
    /// block's start), or — when the special runs right up to midnight — be TOMORROW's own earliest
    /// special (the boundary-peek) — <see cref="ResolveNext"/> handles both, not the old
    /// exact-start-only lookup.
    /// </summary>
    OnAirSnapshot ResolveCurrentSpecial(
        IReadOnlyList<ScheduleSegment> segments, IReadOnlyList<ScheduleSpecial> todaysSpecials,
        IReadOnlyList<ScheduleSpecial> tomorrowsSpecials, ScheduleSpecial special, DayOfWeek today, DayOfWeek tomorrow,
        TimeZoneInfo zone, DateTime todayDate, int nowMinute, DateTimeOffset now)
    {
        var projected = ProjectSpecial(special, today);
        var envelope = BuildSegmentEnvelope(projected);
        var (boundaryDay, boundaryMinute) = NormalizeMinute(today, special.EndMinute);
        var boundaryAt = ResolveBoundaryInstant(todayDate, today, nowMinute, boundaryDay, boundaryMinute, zone, now);
        var next = ResolveNext(segments, todaysSpecials, tomorrowsSpecials, today, tomorrow, boundaryDay, boundaryMinute);
        var assignment = EffectiveAssignment.Resolve(projected, projected.Show);
        return new OnAirSnapshot(projected, assignment.PersonaId, envelope, boundaryAt, next, assignment.Show);
    }

    /// <summary>
    /// A weekly block covers "now", with no special shadowing it yet. SPEC F120.2's other half of "a
    /// special creates a boundary": when a special LATER TODAY would start before this block's own end,
    /// that special pre-empts it early — the reported boundary is the special's start (not the block's
    /// own natural end) and <see cref="OnAirSnapshot.NextSegment"/> is the special itself, projected.
    /// "Now" is still served by the unmodified weekly block either way — the special has not started.
    /// TOMORROW's specials are never candidates for this early-truncation check: a block's own
    /// <c>EndMinute</c> never exceeds <see cref="MinutesPerDay"/> (SPEC F91.1's CHECK), so tomorrow's
    /// earliest possible effective start (minute <see cref="MinutesPerDay"/> + 0) can never be STRICTLY
    /// before it — only exactly AT it, which is the ordinary <see cref="ResolveNext"/> boundary path
    /// below, not a truncation.
    /// </summary>
    OnAirSnapshot ResolveCurrent(
        IReadOnlyList<ScheduleSegment> segments, IReadOnlyList<ScheduleSpecial> todaysSpecials,
        IReadOnlyList<ScheduleSpecial> tomorrowsSpecials, ScheduleSegment current, TimeZoneInfo zone,
        DateTime todayDate, DayOfWeek today, DayOfWeek tomorrow, int nowMinute, DateTimeOffset now)
    {
        var envelope = BuildSegmentEnvelope(current);
        var assignment = EffectiveAssignment.Resolve(current, current.Show);

        var truncatingSpecial = todaysSpecials
            .Where(s => s.StartMinute > nowMinute && s.StartMinute < current.EndMinute)
            .OrderBy(s => s.StartMinute)
            .FirstOrDefault();

        if (truncatingSpecial is not null)
        {
            var truncatedBoundaryAt = ResolveBoundaryInstant(
                todayDate, today, nowMinute, today, truncatingSpecial.StartMinute, zone, now);
            return new OnAirSnapshot(
                current, assignment.PersonaId, envelope, truncatedBoundaryAt, ProjectSpecial(truncatingSpecial, today), assignment.Show);
        }

        var (boundaryDay, boundaryMinute) = NormalizeMinute(current.Day, current.EndMinute);
        var boundaryAt = ResolveBoundaryInstant(todayDate, today, nowMinute, boundaryDay, boundaryMinute, zone, now);
        var next = ResolveNext(segments, todaysSpecials, tomorrowsSpecials, today, tomorrow, boundaryDay, boundaryMinute);
        return new OnAirSnapshot(current, assignment.PersonaId, envelope, boundaryAt, next, assignment.Show);
    }

    /// <summary>
    /// No block (weekly or special) covers "now". Races every "what happens next" candidate — a later
    /// special TODAY, TOMORROW's earliest special (the boundary-peek, PLAN T258 review MF3), and the
    /// weekly grid's own cyclic next start (<see cref="FindNextUpcoming"/>) — by minutes-from-now
    /// distance, favoring a special on any tie (specials-first, SPEC F120.2, same rule every other
    /// resolved instant in this file already follows).
    /// </summary>
    OnAirSnapshot ResolveGap(
        IReadOnlyList<ScheduleSegment> segments, IReadOnlyList<ScheduleSpecial> todaysSpecials,
        IReadOnlyList<ScheduleSpecial> tomorrowsSpecials, TimeZoneInfo zone, DateTime todayDate,
        DayOfWeek today, DayOfWeek tomorrow, int nowMinute, DateTimeOffset now)
    {
        var envelope = defaultEnvelopeSource.Current;

        var nextWeekly = FindNextUpcoming(segments, today, nowMinute);
        var weeklyDistance = nextWeekly is null ? (int?)null : CyclicDistance(today, nowMinute, nextWeekly);

        var nearestSpecial = NearestUpcomingSpecial(todaysSpecials, tomorrowsSpecials, today, tomorrow, nowMinute);

        if (nearestSpecial is { } near && (weeklyDistance is null || near.Distance <= weeklyDistance))
        {
            var specialBoundaryAt = ResolveBoundaryInstant(todayDate, today, nowMinute, near.Day, near.Special.StartMinute, zone, now);
            return new OnAirSnapshot(
                Segment: null, PersonaId: null, envelope, specialBoundaryAt, ProjectSpecial(near.Special, near.Day), Show: null);
        }

        // No block is on air (SPEC F91.4) — nothing for EffectiveAssignment to resolve: persona and
        // show are both unconditionally none, the only honest answer for a grid gap.
        if (nextWeekly is null)
            return new OnAirSnapshot(Segment: null, PersonaId: null, envelope, BoundaryAt: null, NextSegment: null, Show: null);

        var boundaryAt = ResolveBoundaryInstant(todayDate, today, nowMinute, nextWeekly.Day, nextWeekly.StartMinute, zone, now);
        return new OnAirSnapshot(Segment: null, PersonaId: null, envelope, boundaryAt, nextWeekly, Show: null);
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
                segment.EnergyMax ?? stationDefault.EnergyRange.Max))
        {
            // SPEC F152.3 (STORY-372, PLAN T360): Rotation = block.Rotation ?? show.Rotation ?? null.
            // ResolveRotation's own remarks explain why "block" is always null here in v1.
            Rotation = ResolveRotation(blockRotation: null, segment.Show),
        };
    }

    /// <summary>
    /// SPEC F152.3's own layering formula (STORY-372, PLAN T360) — the ONE place
    /// <c>block.Rotation ?? show.Rotation ?? null</c> resolves, mirroring <see cref="EffectiveAssignment.Resolve"/>'s
    /// own "one chokepoint" shape for persona/show (SPEC F115.2). <paramref name="blockRotation"/> has
    /// no real v1 source: <c>segment_schedule</c>/<c>schedule_special</c> carry no rotation column, and
    /// ARCHITECTURE.md's own "Rejected: block-only predicate (the card can't carry the rule)" rules out
    /// ever giving a block its OWN authorable rule — so <see cref="BuildSegmentEnvelope"/>'s only call
    /// site always passes <see langword="null"/> here. The parameter exists so this formula, not the
    /// call site, is where "block always wins" lives — if a future slice ever DOES add a block-level
    /// source, that widening touches only this method's own body, the same "F115.2 layering is literally
    /// the code" contract <see cref="EffectiveAssignment"/> already keeps for persona/show.
    /// </summary>
    internal static RotationPredicate? ResolveRotation(RotationPredicate? blockRotation, ShowSummary? show) =>
        blockRotation ?? show?.Rotation;

    static ScheduleSegment? FindCurrent(IReadOnlyList<ScheduleSegment> segments, DayOfWeek day, int minute) =>
        segments.FirstOrDefault(s => s.Day == day && s.StartMinute <= minute && minute < s.EndMinute);

    /// <summary>The special (if any, from an already today-filtered list) covering <paramref name="minute"/>
    /// — the specials-only mirror of <see cref="FindCurrent"/> (SPEC F120.1's per-date EXCLUDE guarantees
    /// at most one match).</summary>
    static ScheduleSpecial? FindCurrentSpecial(IReadOnlyList<ScheduleSpecial> todaysSpecials, int minute) =>
        todaysSpecials.FirstOrDefault(s => s.StartMinute <= minute && minute < s.EndMinute);

    /// <summary>
    /// What plays at (<paramref name="day"/>, <paramref name="minute"/>) — a boundary target reached
    /// either by a weekly block's own natural end or by a special's own end (SPEC F120.2, PLAN T258).
    /// <paramref name="day"/> is always exactly <paramref name="today"/> or <paramref name="tomorrow"/>
    /// (the only two values <see cref="NormalizeMinute"/> can ever produce): checks the matching one of
    /// <paramref name="todaysSpecials"/>/<paramref name="tomorrowsSpecials"/> for an exact-start match
    /// first (a special immediately following another special or a weekly block — specials always
    /// shadow, and this is also PLAN T258 review MF3's boundary-peek for a special starting AT
    /// midnight), then falls back to a weekly "covers this minute" lookup (<see cref="FindCurrent"/>)
    /// rather than the narrower "starts exactly here" the pre-T258 code used: leaving a special can
    /// resume MID a weekly block (the special's own end need not land on any weekly block's start). For
    /// a boundary that is itself a weekly block's own end (no special involved at all), "covers" and
    /// "starts here" agree exactly — station.segment_schedule's own EXCLUDE constraint guarantees no two
    /// weekly blocks can both cover <paramref name="minute"/> unless one of them starts there — so this
    /// replaces the old exact-start-only <c>FindAdjacent</c> helper with zero behavior change on that
    /// path.
    /// </summary>
    static ScheduleSegment? ResolveNext(
        IReadOnlyList<ScheduleSegment> segments, IReadOnlyList<ScheduleSpecial> todaysSpecials,
        IReadOnlyList<ScheduleSpecial> tomorrowsSpecials, DayOfWeek today, DayOfWeek tomorrow, DayOfWeek day, int minute)
    {
        var sameDaySpecials = day == today ? todaysSpecials : day == tomorrow ? tomorrowsSpecials : [];
        var special = sameDaySpecials.FirstOrDefault(s => s.StartMinute == minute);
        if (special is not null) return ProjectSpecial(special, day);

        return FindCurrent(segments, day, minute);
    }

    /// <summary>The segment whose start is nearest in the future, searching forward cyclically across
    /// the whole week (SPEC F91.1's grid repeats every 7 days) — used only for a gap "now", so a
    /// distance of zero (a segment starting AT this exact instant) can never occur: that segment would
    /// already have been <see cref="FindCurrent"/>'s match.</summary>
    static ScheduleSegment? FindNextUpcoming(IReadOnlyList<ScheduleSegment> segments, DayOfWeek day, int minute)
    {
        ScheduleSegment? best = null;
        var bestDistance = int.MaxValue;

        foreach (var segment in segments)
        {
            var distance = CyclicDistance(day, minute, segment);
            if (distance == 0 || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = segment;
        }

        return best;
    }

    /// <summary>Minutes from (<paramref name="day"/>, <paramref name="minute"/>) forward to
    /// <paramref name="target"/>'s own start, searching cyclically across the whole week (SPEC F91.1's
    /// grid repeats every 7 days) — the shared distance metric <see cref="FindNextUpcoming"/> and
    /// <see cref="ResolveGap"/>'s own special-vs-weekly race both compare against.</summary>
    static int CyclicDistance(DayOfWeek day, int minute, ScheduleSegment target)
    {
        var fromWeekly = (int)day * MinutesPerDay + minute;
        var targetWeekly = (int)target.Day * MinutesPerDay + target.StartMinute;
        return Mod(targetWeekly - fromWeekly, MinutesPerWeek);
    }

    /// <summary>
    /// The closest upcoming special — TODAY's later-than-now rows or TOMORROW's earliest row (PLAN T258
    /// review MF3's boundary-peek), by plain minutes-from-now distance — or <see langword="null"/> when
    /// neither set has one. Only the SINGLE earliest tomorrow candidate is ever considered: if it does
    /// not win its race against the weekly grid's own next start, no later tomorrow row could either
    /// (the race is monotonic in distance), so nothing is lost by not carrying the whole list forward.
    /// </summary>
    static (ScheduleSpecial Special, DayOfWeek Day, int Distance)? NearestUpcomingSpecial(
        IReadOnlyList<ScheduleSpecial> todaysSpecials, IReadOnlyList<ScheduleSpecial> tomorrowsSpecials,
        DayOfWeek today, DayOfWeek tomorrow, int nowMinute)
    {
        (ScheduleSpecial Special, DayOfWeek Day, int Distance)? best = null;

        var laterToday = todaysSpecials.Where(s => s.StartMinute > nowMinute).OrderBy(s => s.StartMinute).FirstOrDefault();
        if (laterToday is not null)
            best = (laterToday, today, laterToday.StartMinute - nowMinute);

        var earliestTomorrow = tomorrowsSpecials.FirstOrDefault(); // already ordered by StartMinute by the caller
        if (earliestTomorrow is not null)
        {
            var distance = (MinutesPerDay - nowMinute) + earliestTomorrow.StartMinute;
            if (best is null || distance < best.Value.Distance)
                best = (earliestTomorrow, tomorrow, distance);
        }

        return best;
    }

    /// <summary>
    /// Projects <paramref name="special"/> into the existing <see cref="ScheduleSegment"/> shape (PLAN
    /// T258 design: "prefer projecting specials into the existing resolved-segment model so downstream
    /// truly doesn't change") — every consumer below (<see cref="BuildSegmentEnvelope"/>,
    /// <see cref="EffectiveAssignment.Resolve"/>, and every field of the resulting
    /// <see cref="OnAirSnapshot"/>) operates on this projected value completely unmodified. No
    /// downstream reader of <see cref="OnAirSnapshot"/> can distinguish a special-sourced
    /// <see cref="OnAirSnapshot.Segment"/> from an ordinary weekly one by SHAPE — there is nothing on
    /// either type that could tell them apart. <see cref="ScheduleSegment.Day"/> is set to
    /// <paramref name="day"/> (the day the special actually airs on — TODAY for a shadow, TOMORROW for
    /// a boundary-peek result), never re-derived from <paramref name="special"/> itself (which carries
    /// no day-of-week at all, only a calendar date).
    ///
    /// <para>
    /// <b><see cref="ScheduleSegment.Id"/> is NEGATED (PLAN T258 review should-fix 5).</b>
    /// <c>station.schedule_special.id</c> and <c>station.segment_schedule.id</c> are independent
    /// Postgres <c>serial</c> sequences, both starting at 1 — a special and a weekly block can carry the
    /// identical numeric id. The one place this codebase renders <c>Segment.Id</c> at all
    /// (<c>ScheduleEnvelopeProvider.EnvelopeId</c>'s <c>"segment:{id}"</c> per-pick DEBUG log token,
    /// <c>MusicSelectionPolicy</c>'s own trace line — no cache key, no branch, no equality check
    /// anywhere reads it) would otherwise silently conflate the two. Negating keeps the two id spaces
    /// disjoint by construction (a <c>serial</c> id is always &gt;= 1, so a special's projected id is
    /// always &lt;= -1) with no new type, no wrapper, and nothing for a future consumer to accidentally
    /// treat as a real <c>segment_schedule.id</c> — <see langword="null"/> only for the (currently
    /// impossible in practice — every special this method ever receives came from a store round trip)
    /// not-yet-persisted case, mirroring <see cref="ScheduleSegment.Id"/>'s own null contract.
    /// </para>
    /// </summary>
    static ScheduleSegment ProjectSpecial(ScheduleSpecial special, DayOfWeek day) => new(
        special.Id is { } id ? -id : null, day, special.StartMinute, special.EndMinute, special.PersonaId,
        special.Genres, special.EnergyMin, special.EnergyMax, special.Show, special.ShowId);

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
