// STORY-317 — Dated specials shadow the grid (F120) — resolver-rung half · 🪂 DROPPABLE SLICE
//
// BDD specification — xUnit. Implements PLAN T258's resolver half: ScheduleResolver.Resolve gains an
// optional specials list (SPEC F120.2) — a pure-function widening, no DI/database involved (mirrors
// this file's own "resolver-level" option over ProductionChainHarness: ScheduleResolver is already the
// one place SPEC F91.2/F91.3 put ALL of the (snapshot, wall clock) -> OnAirSnapshot logic, so proving the
// rung here needs no fake store/harness at all). Downstream consumers (ceremony, idents, the booth
// stamp, spectator) are unchanged by construction — they read OnAirSnapshot, which is exactly why
// ScenarioTheShadow's second fact proves "zero special-casing" by structural comparison against an
// ordinary weekly-authored OnAirSnapshot, rather than re-invoking each of those four already-tested
// consumers directly. The store half lives in MediaLibrary.Tests/Story317_SpecialsStore.cs.
//
// PLAN T258 review (2026-08-11) MF1/MF3: ScenarioGapRace, ScenarioAdjacentSpecialsAndCompetingStarts,
// and ScenarioTheBoundaryPeek were added to close three previously-uncovered ScheduleResolver decision
// branches (the ResolveGap specials-vs-weekly race in both directions plus its tie-break, the
// ResolveNext exact-start branch for back-to-back specials and a special landing exactly on a weekly
// block's own boundary) and to prove the boundary-peek fix for real (a midnight-adjacent special is no
// longer invisible to BoundaryAt/NextSegment).

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureSpecialsRung
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static ScheduleResolver BuildResolver(DateTimeOffset now) =>
        new(new FakeTimeProvider(now), new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

    static ScheduleSegment Weekly(
        DayOfWeek day, int start, int end, long? personaId, ShowSummary? show = null,
        string[]? genres = null, double? energyMin = null, double? energyMax = null) =>
        new(null, day, start, end, personaId, genres, energyMin, energyMax, show, show?.Id);

    static ScheduleSpecial Special(
        DateOnly onDate, int start, int end, long? personaId, ShowSummary? show = null,
        string[]? genres = null, double? energyMin = null, double? energyMax = null) =>
        new(null, onDate, start, end, personaId, genres, energyMin, energyMax, show, show?.Id);

    // A Monday, chosen so DayOfWeek.Monday-keyed weekly blocks line up with a concrete calendar date
    // for the specials list. FakeTimeProvider's own default LocalTimeZone is UTC, so this instant IS
    // station-local "now" with no offset arithmetic to reason about.
    static readonly DateOnly Monday = new(2026, 8, 10);
    static readonly DateOnly Tuesday = Monday.AddDays(1); // "tomorrow" — the boundary-peek facts (PLAN T258 review MF3)
    static readonly DateOnly NextMonday = new(2026, 8, 17); // same weekday, one week later — the "day after" the special's own date has passed

    static DateTimeOffset MondayAt(int hour, int minute = 0) => new(Monday.Year, Monday.Month, Monday.Day, hour, minute, 0, TimeSpan.Zero);
    static DateTimeOffset TuesdayAt(int hour, int minute = 0) => new(Tuesday.Year, Tuesday.Month, Tuesday.Day, hour, minute, 0, TimeSpan.Zero);
    static DateTimeOffset NextMondayAt(int hour, int minute = 0) => new(NextMonday.Year, NextMonday.Month, NextMonday.Day, hour, minute, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the shadow (SPEC F120.2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheShadow
    {
        [Fact]
        public void TheResolverServesTheSpecialForItsSpan()
        {
            // Given a special covering 19:00-21:00 today over a differently-staffed weekly block
            var weeklyShow = new ShowSummary(10, "Regular Monday Show", "The usual Monday lineup", "chill");
            var specialShow = new ShowSummary(20, "Holiday Countdown", "One night only", "festive, upbeat");
            var week = new ScheduleWeekSnapshot([
                Weekly(DayOfWeek.Monday, 18 * 60, 22 * 60, personaId: 1, weeklyShow, genres: ["rock"], energyMin: 0.3, energyMax: 0.6),
            ]);
            var special = Special(Monday, 19 * 60, 21 * 60, personaId: 2, specialShow, genres: ["holiday"], energyMin: 0.1, energyMax: 0.9);
            var resolver = BuildResolver(MondayAt(20)); // inside the special's own span

            // When the resolver snapshot is read inside the span
            var onAir = resolver.Resolve(week, [special]);

            // Then persona/show/envelope come from the special (specials-first rung, F120.2)
            Assert.Equal(2, onAir.PersonaId);
            Assert.Equal(specialShow, onAir.Show);
            Assert.Equal(["holiday"], onAir.Envelope.Genres);
            Assert.Equal(0.1, onAir.Envelope.EnergyRange.Min);
            Assert.Equal(0.9, onAir.Envelope.EnergyRange.Max);

            // And the special's own edges are real boundaries: it ends at 21:00 today, and the weekly
            // block underneath RESUMES mid-block (18:00-22:00 did not itself end) rather than the
            // resolver reporting a gap or null.
            Assert.Equal(MondayAt(21), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(1, onAir.NextSegment.PersonaId);
            Assert.Equal(weeklyShow, onAir.NextSegment.Show);
        }

        [Fact]
        public void DownstreamConsumersFollowWithZeroSpecialCasing()
        {
            // Given the special on the air — built two ways: once as a special shadowing a DIFFERENTLY
            // staffed weekly block, once as an ordinary weekly-authored block carrying the identical
            // identity/envelope. Every downstream reader (ceremony context, ident preference, the booth
            // stamp — T248/T250/T242, all already spec'd and unmodified elsewhere) reads exclusively off
            // OnAirSnapshot.PersonaId/Show/Envelope; proving those three are structurally IDENTICAL
            // between the two paths proves none of those readers could ever special-case one — there is
            // nothing on OnAirSnapshot for a special-case to key on.
            var show = new ShowSummary(20, "Holiday Countdown", "One night only", "festive, upbeat");

            var weekWithOrdinaryBlock = new ScheduleWeekSnapshot([
                Weekly(DayOfWeek.Monday, 19 * 60, 21 * 60, personaId: 2, show, genres: ["holiday"], energyMin: 0.1, energyMax: 0.9),
            ]);
            var viaOrdinaryBlock = BuildResolver(MondayAt(20)).Resolve(weekWithOrdinaryBlock);

            var weekWithDifferentWeeklyStaffing = new ScheduleWeekSnapshot([
                Weekly(DayOfWeek.Monday, 18 * 60, 22 * 60, personaId: 1, new ShowSummary(10, "Regular Monday Show", null, null)),
            ]);
            var special = Special(Monday, 19 * 60, 21 * 60, personaId: 2, show, genres: ["holiday"], energyMin: 0.1, energyMax: 0.9);
            var viaSpecial = BuildResolver(MondayAt(20)).Resolve(weekWithDifferentWeeklyStaffing, [special]);

            // When ceremony context, ident preference, and the booth stamp read identity — all read the
            // same shape, whichever path produced it
            Assert.Equal(viaOrdinaryBlock.PersonaId, viaSpecial.PersonaId);
            Assert.Equal(viaOrdinaryBlock.Show, viaSpecial.Show);
            Assert.Equal(viaOrdinaryBlock.Envelope.Genres, viaSpecial.Envelope.Genres);
            Assert.Equal(viaOrdinaryBlock.Envelope.EnergyRange, viaSpecial.Envelope.EnergyRange);
        }

        [Fact]
        public void ASpecialLaterTodayTruncatesTheCurrentlyServingWeeklyBlocksBoundary()
        {
            // Given "now" is inside a weekly block, and a special STARTS later today, before that
            // block's own natural end (SPEC F120.2: "a special starting mid-weekly-block creates a
            // boundary" even before it has started airing).
            var weeklyShow = new ShowSummary(10, "Regular Monday Show", null, null);
            var specialShow = new ShowSummary(20, "Holiday Countdown", null, null);
            var week = new ScheduleWeekSnapshot([Weekly(DayOfWeek.Monday, 18 * 60, 22 * 60, personaId: 1, weeklyShow)]);
            var special = Special(Monday, 19 * 60, 21 * 60, personaId: 2, specialShow);
            var resolver = BuildResolver(MondayAt(18, 30)); // inside the weekly block, before the special starts

            // When the resolver snapshot is read
            var onAir = resolver.Resolve(week, [special]);

            // Then "now" is still served by the unmodified weekly block...
            Assert.Equal(1, onAir.PersonaId);
            Assert.Equal(weeklyShow, onAir.Show);

            // ...but the reported boundary is the special's OWN start (19:00), not the block's natural
            // end (22:00) — and what plays next is the special itself, not whatever the weekly grid
            // would otherwise say follows at 22:00.
            Assert.Equal(MondayAt(19), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(2, onAir.NextSegment.PersonaId);
            Assert.Equal(specialShow, onAir.NextSegment.Show);
        }

        [Fact]
        public void AProjectedSpecialCarriesANegatedIdSoItCanNeverCollideWithARealWeeklyBlocksId()
        {
            // Given a special and a weekly block that happen to share the identical store-assigned id
            // (station.schedule_special.id and station.segment_schedule.id are independent Postgres
            // sequences, both starting at 1 — SPEC PLAN T258 review should-fix 5)
            var week = new ScheduleWeekSnapshot([]);
            var special = new ScheduleSpecial(Id: 7, Monday, 600, 900, PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null);
            var resolver = BuildResolver(MondayAt(11));

            // When the special is projected into the resolved segment
            var onAir = resolver.Resolve(week, [special]);

            // Then its id is negated — disjoint by construction from any real segment_schedule id,
            // which is always >= 1
            Assert.Equal(-7, onAir.Segment?.Id);
        }
    }

    // ---------------------------------------------------------------------
    // SAD-ish PATH — the gap race between a special and the weekly grid's own cyclic next start
    // (SPEC F120.2, PLAN T258 review MF1a/MF1b: ResolveGap's race, both directions, plus the tie-break)
    // ---------------------------------------------------------------------

    public sealed class ScenarioGapRace
    {
        [Fact]
        public void AGapWhereASpecialIsCloserThanTheNextWeeklyStartServesTheSpecial()
        {
            // Given a gap at 10:00 with the next weekly block ten hours away (20:00) and a special only
            // one hour away (11:00)
            var week = new ScheduleWeekSnapshot([Weekly(DayOfWeek.Monday, 20 * 60, 22 * 60, personaId: 1)]);
            var special = Special(Monday, 11 * 60, 12 * 60, personaId: 2);
            var resolver = BuildResolver(MondayAt(10));

            // When the resolver reads the gap
            var onAir = resolver.Resolve(week, [special]);

            // Then the special wins the race
            Assert.Null(onAir.Segment);
            Assert.Null(onAir.PersonaId);
            Assert.Equal(MondayAt(11), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(2, onAir.NextSegment.PersonaId);
        }

        [Fact]
        public void AGapWhereTheNextWeeklyStartIsCloserThanAnySpecialServesTheWeeklyBlock()
        {
            // Given a gap at 10:00 with the next weekly block thirty minutes away (10:30) and a special
            // four hours away (14:00)
            var week = new ScheduleWeekSnapshot([Weekly(DayOfWeek.Monday, 10 * 60 + 30, 11 * 60, personaId: 1)]);
            var special = Special(Monday, 14 * 60, 15 * 60, personaId: 2);
            var resolver = BuildResolver(MondayAt(10));

            // When the resolver reads the gap
            var onAir = resolver.Resolve(week, [special]);

            // Then the weekly grid wins the race — the special is simply too far away to matter yet
            Assert.Null(onAir.Segment);
            Assert.Equal(MondayAt(10, 30), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(1, onAir.NextSegment.PersonaId);
        }

        [Fact]
        public void AGapWhereASpecialAndTheNextWeeklyStartTieFavorsTheSpecial()
        {
            // Given a gap at 10:00 with a weekly block AND a special both starting at the identical
            // instant, 11:00 — a genuine distance tie
            var week = new ScheduleWeekSnapshot([Weekly(DayOfWeek.Monday, 11 * 60, 12 * 60, personaId: 1)]);
            var special = Special(Monday, 11 * 60, 11 * 60 + 30, personaId: 2);
            var resolver = BuildResolver(MondayAt(10));

            // When the resolver reads the gap
            var onAir = resolver.Resolve(week, [special]);

            // Then the tie favors the special (specials-first, SPEC F120.2)
            Assert.Null(onAir.Segment);
            Assert.Equal(MondayAt(11), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(2, onAir.NextSegment.PersonaId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ResolveNext's exact-start branch (SPEC F120.2, PLAN T258 review MF1c): back-to-back
    // specials, and a special landing exactly on a weekly block's own boundary alongside a REAL
    // competing weekly block at that same instant.
    // ---------------------------------------------------------------------

    public sealed class ScenarioAdjacentSpecialsAndCompetingStarts
    {
        [Fact]
        public void BackToBackSpecialsOnTheSameDateHandOffToEachOtherAtTheSeam()
        {
            // Given two specials on the same date with no gap between them (10:00-15:00, then 15:00-20:00)
            var week = new ScheduleWeekSnapshot([]);
            var first = Special(Monday, 10 * 60, 15 * 60, personaId: 1);
            var second = Special(Monday, 15 * 60, 20 * 60, personaId: 2);
            var resolver = BuildResolver(MondayAt(11)); // inside the first

            // When the resolver snapshot is read
            var onAir = resolver.Resolve(week, [first, second]);

            // Then the boundary/next hand off directly to the second special — no weekly fallback, no gap
            Assert.Equal(1, onAir.PersonaId);
            Assert.Equal(MondayAt(15), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(2, onAir.NextSegment.PersonaId);
        }

        [Fact]
        public void ASpecialStartingExactlyAtAWeeklyBlocksEndWinsOverARealCompetingWeeklyBlock()
        {
            // Given two ADJACENT weekly blocks (A ends exactly where B starts, 10:00 — both real,
            // legal, non-overlapping rows) and a special dated today that ALSO starts exactly at
            // 10:00 — a genuine competing candidate, not merely an absence of anything else scheduled.
            var week = new ScheduleWeekSnapshot([
                Weekly(DayOfWeek.Monday, 8 * 60, 10 * 60, personaId: 1),  // A
                Weekly(DayOfWeek.Monday, 10 * 60, 15 * 60, personaId: 2), // B — the weekly competitor at 10:00
            ]);
            var special = Special(Monday, 10 * 60, 13 * 60, personaId: 3);
            var resolver = BuildResolver(MondayAt(9)); // inside A

            // When the resolver snapshot is read
            var onAir = resolver.Resolve(week, [special]);

            // Then "now" is still A, but the special — not B — is what plays at the 10:00 boundary
            Assert.Equal(1, onAir.PersonaId);
            Assert.Equal(MondayAt(10), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(3, onAir.NextSegment.PersonaId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the boundary-peek (SPEC F120.2, PLAN T258 review MF3): a midnight-adjacent special
    // dated TOMORROW must still be visible to BoundaryAt/NextSegment today — production hand-off
    // machinery (ceremony included) arms off BoundaryAt, not a fresh re-resolve at the stroke of
    // midnight, so an invisible boundary means a missed sign-off.
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheBoundaryPeek
    {
        [Fact]
        public void AGapTonightSeesTomorrowsMidnightSpecialAsTheNextBoundary()
        {
            // Given a gap at 23:00 tonight (nothing covers it) and a special dated TOMORROW, 00:00-02:00
            var week = new ScheduleWeekSnapshot([]);
            var tomorrowsSpecial = Special(Tuesday, 0, 120, personaId: 9);
            var resolver = BuildResolver(MondayAt(23));

            // When the resolver reads tonight's gap
            var onAir = resolver.Resolve(week, [tomorrowsSpecial]);

            // Then the boundary is midnight, and what plays next is the special — never invisible just
            // because its own date is not "today" yet
            Assert.Null(onAir.Segment);
            Assert.Equal(TuesdayAt(0), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(9, onAir.NextSegment.PersonaId);
        }

        [Fact]
        public void ABlockRunningToMidnightHandsOffToTomorrowsSpecialNotTomorrowsWeeklyBlock()
        {
            // Given a weekly block that runs right up to midnight tonight, ANOTHER weekly block picking
            // up tomorrow at 00:00 (a real competing candidate — not merely an absence), and a special
            // ALSO dated tomorrow at 00:00
            var week = new ScheduleWeekSnapshot([
                Weekly(DayOfWeek.Monday, 22 * 60, 24 * 60, personaId: 1),
                Weekly(DayOfWeek.Tuesday, 0, 6 * 60, personaId: 2),
            ]);
            var tomorrowsSpecial = Special(Tuesday, 0, 120, personaId: 9);
            var resolver = BuildResolver(MondayAt(23)); // inside tonight's block

            // When the resolver snapshot is read
            var onAir = resolver.Resolve(week, [tomorrowsSpecial]);

            // Then "now" is still tonight's block, but the special — not tomorrow's weekly persona —
            // is what plays at the midnight boundary
            Assert.Equal(1, onAir.PersonaId);
            Assert.Equal(TuesdayAt(0), onAir.BoundaryAt);
            Assert.NotNull(onAir.NextSegment);
            Assert.Equal(9, onAir.NextSegment.PersonaId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the day after (SPEC F120.2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheDayAfter
    {
        [Fact]
        public void TheWeeklyGridServesExactlyAsBefore()
        {
            // Given the special's date has passed — it was dated for last Monday; "today" is the
            // following Monday, the identical wall-clock span.
            var week = new ScheduleWeekSnapshot([
                Weekly(DayOfWeek.Monday, 19 * 60, 21 * 60, personaId: 1, new ShowSummary(10, "Regular Monday Show", null, null)),
            ]);
            var pastSpecial = Special(Monday, 19 * 60, 21 * 60, personaId: 2, new ShowSummary(20, "Holiday Countdown", null, null));
            var resolver = BuildResolver(NextMondayAt(20));

            // When the same wall-clock span arrives next day/week
            var withPastSpecialInTheList = resolver.Resolve(week, [pastSpecial]);
            var withNoSpecialsAtAll = resolver.Resolve(week);

            // Then the weekly grid serves exactly as before — byte-identical whether or not a
            // now-irrelevant dated special is still sitting in the caller's own list.
            Assert.Equal(withNoSpecialsAtAll, withPastSpecialInTheList);
            Assert.Equal(1, withPastSpecialInTheList.PersonaId);
        }
    }
}
