// gh-#224 — The schedule grid and the taste gates follow the station, not the container.
//
// BDD specification — xUnit. gh-#117 routed the LLM's clocks through IStationClockProvider; this
// file pins the two non-LLM surfaces gh-#224 routes through the SAME seam: PersonaRanker's taste
// day/hour gating (SPEC F82.1) and ScheduleResolver's format-clock slot resolution (SPEC F91.2).
// Every fact runs at ONE fixed instant chosen so the container zone (the FakeTimeProvider's UTC)
// and the station zone (America/Edmonton) disagree on BOTH the hour and the day — so a surface
// still reading the container clock cannot pass by coincidence. The Host-side provider half
// (OptionsMonitorStationClockProvider.Zone, empty = container zone) lives in
// Host.Tests/Specs/Gh224_StationZoneProvider.cs (the Story117/121 split: facts live where their
// subject compiles).

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureStationZoneScheduleAndTasteClock
{
    // -------------------------------------------------------------------------
    // Helpers — one instant, two disagreeing zones
    // -------------------------------------------------------------------------

    // 02:00 UTC on Monday 2026-07-20 is 20:00 the PREVIOUS evening — Sunday 2026-07-19 — in
    // Edmonton (MDT, UTC-6): hour AND day both differ between the container and the station.
    static readonly DateTimeOffset MondaySmallHoursUtc = new(2026, 7, 20, 2, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset EdmontonSundayEvening = new(2026, 7, 19, 20, 0, 0, TimeSpan.FromHours(-6));

    static TimeZoneInfo Edmonton => TimeZoneInfo.FindSystemTimeZoneById("America/Edmonton");

    // ---------------------------------------------------------------------
    // HAPPY PATH — taste gates flip on the STATION-zone day/hour boundary
    // ---------------------------------------------------------------------

    public sealed class ScenarioTasteGatesFollowTheStationZone
    {
        // Arrange: one Sunday-evening artist rule; a single matching candidate so the pick (and
        // its FiredRules) is fully deterministic — StubRandomSource(0.99) suppresses the
        // exploration roll, and a one-entry Top-K is returned without a softmax draw.

        static readonly TasteRule SundayEveningRule = new(
            new TastePredicate(Artist: "Led Zeppelin", Genre: null, Tag: null),
            new TasteContext(DaysOfWeek: [DayOfWeek.Sunday], StartHour: 18, EndHour: 23),
            Weight: 1.0);

        internal static IReadOnlyList<PersonaRankCandidate> Pool() =>
            [new PersonaRankCandidate(MediaId: "zep1", Artist: "Led Zeppelin", Genre: "Rock", Moods: [], Energy: 0.5, RotationScore: 0.0)];

        internal static PersonaRanker BuildRanker(IStationClockProvider? stationClock) => new(
            new FakePersonaTasteReader([SundayEveningRule]),
            new StubRandomSource(0.99),
            new FakeTimeProvider(MondaySmallHoursUtc), // container = UTC: Monday 02:00
            new PersonaRankerOptions(),
            NullLogger<PersonaRanker>.Instance,
            stationClock);

        [Fact]
        public async Task TheGateOpensOnTheStationsSundayEvening()
        {
            // The container says Monday 02:00 — outside the rule's day AND hour gates — but the
            // station clock says Sunday 20:00, inside both: the rule fires (F82.1 gates resolve
            // station-local, gh-#224).
            var ranker = BuildRanker(new FakeStationClockProvider(EdmontonSundayEvening, Edmonton));

            var result = await ranker.PickAsync(
                personaId: 1, energyDisposition: 0.0, new EnergyRange(0.0, 1.0), Pool(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Contains(SundayEveningRule, result.FiredRules);
        }
    }

    // ── Sad path — no seam wired: the container's own clock, prior behavior unchanged ──────────

    public sealed class ScenarioNoStationClockKeepsTheContainersTasteGating
    {
        [Fact]
        public async Task TheSameInstantLeavesTheGateClosed()
        {
            // Empty Station:Timezone composes to no different behavior, and a rig with no seam at
            // all (every pre-gh-#224 construction) gates on the container's Monday 02:00 — the
            // Sunday rule stays shut: the prior-behavior pin.
            var ranker = ScenarioTasteGatesFollowTheStationZone.BuildRanker(stationClock: null);

            var result = await ranker.PickAsync(
                personaId: 1, energyDisposition: 0.0, new EnergyRange(0.0, 1.0),
                ScenarioTasteGatesFollowTheStationZone.Pool(), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Empty(result.FiredRules);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the schedule grid resolves the STATION-local slot
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheScheduleGridFollowsTheStationZone
    {
        // Arrange: a Sunday-evening slot and a Monday-overnight slot — the container's clock
        // (Monday 02:00 UTC) lands in the second, the station's (Sunday 20:00 MDT) in the first.

        internal static ScheduleWeekSnapshot Snapshot() => new([
            new ScheduleSegment(
                Id: 1, Day: DayOfWeek.Sunday, StartMinute: 1080, EndMinute: 1380,
                PersonaId: 7, Genres: ["Rock"], EnergyMin: null, EnergyMax: null),
            new ScheduleSegment(
                Id: 2, Day: DayOfWeek.Monday, StartMinute: 0, EndMinute: 240,
                PersonaId: 9, Genres: null, EnergyMin: null, EnergyMax: null),
        ]);

        internal static ScheduleResolver BuildResolver(IStationClockProvider? stationClock) => new(
            new FakeTimeProvider(MondaySmallHoursUtc), // container = UTC: Monday 02:00
            new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault),
            stationClock);

        [Fact]
        public void TheStationZonePicksTheSundayEveningSlot()
        {
            // F91.2 station-local resolution (gh-#224): Sunday 20:00 Edmonton is inside the
            // 18:00-23:00 Sunday slot — persona 7 is on the air, not the Monday-overnight slot the
            // container's own Monday 02:00 would have picked.
            var resolver = BuildResolver(new FakeStationClockProvider(EdmontonSundayEvening, Edmonton));

            var onAir = resolver.Resolve(Snapshot());

            Assert.Equal(1L, onAir.Segment?.Id);
            Assert.Equal(7L, onAir.PersonaId);
        }

        [Fact]
        public void TheBoundaryInstantResolvesInTheStationZone()
        {
            // The slot's end (Sunday 23:00 wall clock) converts through the STATION's zone —
            // 23:00 MDT (UTC-6), i.e. 05:00 UTC Monday — proving the DST-aware boundary math runs
            // on IStationClockProvider.Zone, not merely the "now" read.
            var resolver = BuildResolver(new FakeStationClockProvider(EdmontonSundayEvening, Edmonton));

            var onAir = resolver.Resolve(Snapshot());

            Assert.NotNull(onAir.BoundaryAt);
            Assert.Equal(TimeSpan.FromHours(-6), onAir.BoundaryAt.Value.Offset);
            Assert.Equal(new DateTime(2026, 7, 19, 23, 0, 0), onAir.BoundaryAt.Value.DateTime);
        }
    }

    // ── Sad path — no seam wired: the container's own slot, prior behavior unchanged ───────────

    public sealed class ScenarioNoStationClockKeepsTheContainersSlot
    {
        [Fact]
        public void TheSameInstantResolvesTheMondayOvernightSlot()
        {
            // The pre-gh-#224 pin: with no seam, the resolver reads the container's Monday 02:00
            // and picks the Monday-overnight slot — byte-identical to the prior behavior for every
            // rig that never registers the seam.
            var resolver = ScenarioTheScheduleGridFollowsTheStationZone.BuildResolver(stationClock: null);

            var onAir = resolver.Resolve(ScenarioTheScheduleGridFollowsTheStationZone.Snapshot());

            Assert.Equal(2L, onAir.Segment?.Id);
            Assert.Equal(9L, onAir.PersonaId);
        }
    }
}
