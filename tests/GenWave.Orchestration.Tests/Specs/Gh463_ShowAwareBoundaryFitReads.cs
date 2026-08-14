// gh-#463 — Boundary-fit reads the show-aware ident duration, not the show-blind one
//
// BDD specification — xUnit. BuildBoundaryFit (Orchestrator.cs) reads the on-air show off its
// scheduleResolver — the SAME CachingScheduleResolver.TryGetCurrent() snapshot the F117.2 drain-side
// StationId arm already trusts — and passes it into the estimator's 4-arg Estimate overload, so a
// mixed-show templated-ident memo can never be misread as the plain ident's Exact duration while a
// show is on the air (T250 re-keyed the WRITE side; this closes the READ side). Uses
// ProductionChainHarness (the T120 idiom) for a real CachingScheduleResolver, and
// CapturingPatterDurationEstimator (Fakes/) to observe exactly what the fit hands the estimator,
// without caring what it returns.

namespace GenWave.Orchestration.Tests.Specs;

using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

public static class FeatureShowAwareBoundaryFitReads
{
    static readonly DayOfWeek Monday = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).DayOfWeek;
    static readonly DateTimeOffset MidMorning = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    static readonly ShowSummary MorningShow =
        new(Id: 5, Name: "The Morning Mix", Tagline: "Wake up with us", Flavor: "bright, upbeat");

    static readonly ScheduleWeekSnapshot NoShows = new([]);

    static readonly CadenceConfig BackAnnounceOnly = new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = true,
        StationIdEveryNUnits = 0,
    };

    /// <summary>One all-day, music-only (PersonaId null) block naming <paramref name="show"/> — just
    /// enough schedule for CachingScheduleResolver.TryGetCurrent() to answer with a Show; nothing else
    /// this file's facts need (no persona, no boundary within the run).</summary>
    static ScheduleWeekSnapshot AllDayShow(ShowSummary show) => new(
    [
        new ScheduleSegment(
            Id: 1, Day: Monday, StartMinute: 0, EndMinute: 1440, PersonaId: null,
            Genres: null, EnergyMin: null, EnergyMax: null, Show: show, ShowId: show.Id),
    ]);

    public sealed class ScenarioShowOnAir
    {
        [Fact]
        public async Task TheFitPassesTheOnAirShowNameToTheEstimator()
        {
            // Given a show on the air and an imminent StationId boundary
            var estimator = new CapturingPatterDurationEstimator();
            var chain = ProductionChainHarness.BuildProductionChain(
                new FakePersonaStore(), AllDayShow(MorningShow), MidMorning, TimeSpan.FromMinutes(10),
                cadence: BackAnnounceOnly, patterEstimator: estimator);
            chain.Queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 4 minutes",
                chain.Time.GetUtcNow() + TimeSpan.FromMinutes(4));

            // When the next unit is planned (the fit builds ahead of the drain)
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then every estimate the fit made carries the on-air show's own name.
            Assert.NotEmpty(estimator.Calls);
            Assert.All(estimator.Calls, call => Assert.Equal(MorningShow.Name, call.ShowName));
        }
    }

    public sealed class ScenarioNoShowOnAir
    {
        [Fact]
        public async Task TheFitPassesNoShowNameWhenTheGridIsAGap()
        {
            // Given no show on the air (an empty grid — F91.4's own gap) and an imminent StationId
            // boundary
            var estimator = new CapturingPatterDurationEstimator();
            var chain = ProductionChainHarness.BuildProductionChain(
                new FakePersonaStore(), NoShows, MidMorning, TimeSpan.FromMinutes(10),
                cadence: BackAnnounceOnly, patterEstimator: estimator);
            chain.Queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 4 minutes",
                chain.Time.GetUtcNow() + TimeSpan.FromMinutes(4));

            // When the next unit is planned
            await chain.Orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Then the fit never fabricates a show name for a showless airing.
            Assert.NotEmpty(estimator.Calls);
            Assert.All(estimator.Calls, call => Assert.Null(call.ShowName));
        }
    }
}
