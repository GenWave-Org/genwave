// STORY-308 — The flavor line shares the slot (F116.3) — gate-mechanics half
//
// BDD specification — xUnit. The prompt-arbitration half ("context wins the slot") lives in
// GenWave.Tts.Tests/Specs/Story308_FlavorLineSharedSlot.cs — that file proves LlmCopyWriter never
// even ASKS this gate when a context fact already claimed the slot. This file proves the GATE ITSELF:
// cadence elapse per show (TimeProvider, not wall DateTime), per-show independence, and that a
// null-returning call (not due, no flavor, no show, cadence off) never advances the gate's own state —
// the mechanism that makes "losing the slot never consumes the cadence" true in the first place.

namespace GenWave.Orchestration.Tests.Specs;

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

public static class FeatureShowFlavorLineGate
{
    static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;
    static readonly DateTimeOffset Noon = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

    // Two minutes before the 720-minute (noon) boundary DifferentShowsGateIndependently crosses —
    // still inside the morning show's own 0-720 block.
    static readonly DateTimeOffset JustBeforeNoon = new(2026, 3, 2, 11, 58, 0, TimeSpan.Zero);

    static readonly ShowSummary MorningShow =
        new(Id: 1, Name: "The Breakfast Show", Tagline: null, Flavor: "upbeat, chatty, coffee-fueled");
    static readonly ShowSummary NightShow =
        new(Id: 2, Name: "Night Moves", Tagline: null, Flavor: "moody, sparse, past midnight");
    static readonly ShowSummary FlavorlessShow =
        new(Id: 3, Name: "Quiet Hours", Tagline: null, Flavor: null);

    static ScheduleWeekSnapshot AllWeek(ShowSummary? show) => new(
    [
        new ScheduleSegment(
            Id: 1, Day: Monday, StartMinute: 0, EndMinute: 1440, PersonaId: null,
            Genres: null, EnergyMin: null, EnergyMax: null, Show: show, ShowId: show?.Id),
    ]);

    // DifferentShowsGateIndependently's own snapshot (mirrors Story307_CeremonyNamesTheShow's own
    // SameDjDifferentShowSchedule shape): the SAME Monday split into a morning block (0-720) and a
    // night block (720-1440) — one ScheduleWeekSnapshot, one CachingScheduleResolver, one
    // ShowFlavorLineGate instance, so the wall clock crossing the boundary is the ONLY thing that
    // changes which show TryGetCurrent().Show resolves to.
    static ScheduleWeekSnapshot MorningThenNightMonday() => new(
    [
        new ScheduleSegment(
            Id: 1, Day: Monday, StartMinute: 0, EndMinute: 720, PersonaId: null,
            Genres: null, EnergyMin: null, EnergyMax: null, Show: MorningShow, ShowId: MorningShow.Id),
        new ScheduleSegment(
            Id: 2, Day: Monday, StartMinute: 720, EndMinute: 1440, PersonaId: null,
            Genres: null, EnergyMin: null, EnergyMax: null, Show: NightShow, ShowId: NightShow.Id),
    ]);

    sealed record Harness(ShowFlavorLineGate Gate, FakeShowPatterCadenceProvider Cadence, FakeTimeProvider Time);

    static async Task<Harness> BuildAsync(ScheduleWeekSnapshot snapshot, int cadenceMinutes, DateTimeOffset now)
    {
        var time = new FakeTimeProvider(now);
        var store = new FakeScheduleStore(snapshot);
        var stationDefault = new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault);
        var resolver = new ScheduleResolver(time, stationDefault);
        var caching = new CachingScheduleResolver(store, resolver);
        await caching.ResolveAsync(CancellationToken.None); // Populates the cache TryGetCurrent reads.

        var cadence = new FakeShowPatterCadenceProvider(cadenceMinutes);
        return new Harness(new ShowFlavorLineGate(caching, cadence, time), cadence, time);
    }

    static Task<Harness> BuildAsync(ShowSummary? show, int cadenceMinutes, DateTimeOffset? now = null) =>
        BuildAsync(AllWeek(show), cadenceMinutes, now ?? Noon);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioCadenceElapsePerShow
    {
        [Fact]
        public async Task FirstCallOnADueShowReturnsTheFlavorFact()
        {
            // Given a show on the air with flavor text and a positive cadence...
            var h = await BuildAsync(MorningShow, cadenceMinutes: 10);

            // When the gate is asked for the first time...
            var fact = h.Gate.TryTakeDueShowLine();

            // Then it hands out the show's own name and flavor.
            Assert.Equal(new ShowFlavorFact("The Breakfast Show", "upbeat, chatty, coffee-fueled"), fact);
        }

        [Fact]
        public async Task ASecondImmediateCallIsNotDueYet()
        {
            // Given a line was already taken this cadence window...
            var h = await BuildAsync(MorningShow, cadenceMinutes: 10);
            Assert.NotNull(h.Gate.TryTakeDueShowLine());

            // When asked again with no time having passed...
            var second = h.Gate.TryTakeDueShowLine();

            // Then nothing is due — the window has not elapsed.
            Assert.Null(second);
        }

        [Fact]
        public async Task TheLineComesDueAgainOnceTheCadenceWindowElapses()
        {
            // Given a line was taken, and the cadence is 10 minutes...
            var h = await BuildAsync(MorningShow, cadenceMinutes: 10);
            Assert.NotNull(h.Gate.TryTakeDueShowLine());

            // When exactly the cadence window elapses (TimeProvider, not wall DateTime)...
            h.Time.Advance(TimeSpan.FromMinutes(10));

            // Then the line is due again for the SAME show.
            Assert.NotNull(h.Gate.TryTakeDueShowLine());
        }

        [Fact]
        public async Task DifferentShowsGateIndependently()
        {
            // Given ONE gate instance (one CachingScheduleResolver, one Dictionary) resolving
            // against a snapshot with two ADJACENT shows on the same day — morning (0-720) then
            // night (720-1440) — and a 10-minute cadence...
            var h = await BuildAsync(MorningThenNightMonday(), cadenceMinutes: 10, JustBeforeNoon);

            // When the morning show's line is taken just before the boundary...
            var morningFact = h.Gate.TryTakeDueShowLine();
            Assert.Equal(new ShowFlavorFact("The Breakfast Show", "upbeat, chatty, coffee-fueled"), morningFact);

            // ...and the wall clock crosses the boundary into the night show only 4 minutes later —
            // CachingScheduleResolver.TryGetCurrent re-derives against the live clock every call, a
            // pure function of (snapshot, now), so THIS SAME gate instance now sees a different
            // on-air show with no second resolve. 4 minutes is well inside the morning show's own
            // 10-minute window (ASecondImmediateCallIsNotDueYet, above, already pins that the SAME
            // show stays closed this soon after a stamp)...
            h.Time.Advance(TimeSpan.FromMinutes(4));

            // Then the DIFFERENT (night) show is due immediately — proving the gate keys per show
            // id, not a single shared "last spoken" instant. A collapsed/shared-field
            // implementation would still see only 4 elapsed minutes against the 10-minute cadence —
            // exactly the morning show's own still-closed window — and would wrongly answer null
            // here too; keying per show id is what lets the night show, never before stamped,
            // answer due on its own clock instead.
            var nightFact = h.Gate.TryTakeDueShowLine();
            Assert.Equal(new ShowFlavorFact("Night Moves", "moody, sparse, past midnight"), nightFact);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — every "nothing due" cause, and the invariant that makes
    // "losing the slot never consumes the cadence" true: a null-returning
    // call never advances the gate's own state.
    // ---------------------------------------------------------------------

    public sealed class ScenarioNothingDue
    {
        [Fact]
        public async Task CadenceOffNeverHandsOutALine()
        {
            // Given Station:Shows:PatterCadenceMinutes at its 0 (off) default...
            var h = await BuildAsync(MorningShow, cadenceMinutes: 0);

            // When the gate is asked, repeatedly, including after real elapsed time...
            Assert.Null(h.Gate.TryTakeDueShowLine());
            h.Time.Advance(TimeSpan.FromHours(1));
            Assert.Null(h.Gate.TryTakeDueShowLine());
        }

        [Fact]
        public async Task NoShowOnAirNeverHandsOutALine()
        {
            // Given a showless station (a music-only/unnamed block)...
            var h = await BuildAsync(show: null, cadenceMinutes: 10);

            Assert.Null(h.Gate.TryTakeDueShowLine());
        }

        [Fact]
        public async Task AShowWithNoFlavorTextNeverHandsOutALine()
        {
            // Given a show on the air that carries no flavor text at all...
            var h = await BuildAsync(FlavorlessShow, cadenceMinutes: 10);

            Assert.Null(h.Gate.TryTakeDueShowLine());
        }

        [Fact]
        public async Task ANotYetDueCallNeverConsumesTheWindow()
        {
            // Given a 10-minute cadence, and a call made only 3 minutes after the last one (not due
            // yet) — the gate-level mirror of "losing the slot never consumes the cadence": a call
            // that returns null must not push the window further out.
            var h = await BuildAsync(MorningShow, cadenceMinutes: 10);
            Assert.NotNull(h.Gate.TryTakeDueShowLine()); // Stamps t=0.

            h.Time.Advance(TimeSpan.FromMinutes(3));
            Assert.Null(h.Gate.TryTakeDueShowLine()); // Not due yet — and must not re-stamp to t=3.

            // When the ORIGINAL 10-minute window elapses (from t=0, not from the t=3 not-due poll)...
            h.Time.Advance(TimeSpan.FromMinutes(7)); // t=10 from the original stamp.

            // Then the line is due exactly on the original schedule — the not-due poll at t=3 never
            // reset the clock.
            Assert.NotNull(h.Gate.TryTakeDueShowLine());
        }
    }
}
