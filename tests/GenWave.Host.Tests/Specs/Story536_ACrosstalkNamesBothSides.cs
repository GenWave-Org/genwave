// STORY-536 AC2 — a handoff is remembered on both sides (SPEC F218.15 · PLAN T702)
//
// BDD specification — xUnit. The stock worker stamps who spoke each side on the script it stocks, so the accrual
// consumer can write an episode for each persona from TrackAired.CrosstalkScript alone.

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.Host.Crosstalk;
using GenWave.Host.Tests.Support;
using GenWave.Orchestration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureACrosstalkNamesBothSides
{
    static PersonaCard Card(string name) =>
        new(1, name, "", $"{name}'s soul.", [], new VoiceSpec("kokoro", "af_heart", 1.0, "en"),
            EnergyDisposition: 0, [], []);

    public sealed class ScenarioTheStockedScriptNamesTheCast
    {
        readonly CrosstalkAiredScript stamped = CrosstalkStockWorker.NameTheCast(
            new CrosstalkAiredScript([new CrosstalkAiredLine(CrosstalkSpeaker.Host, "Hello there.", false)]),
            new CrosstalkCastResult(new CrosstalkCast(10, 20), Card("Dusty"), Card("Flip")));

        [Fact]
        public void TheHostSideIsThePersonaWhoSpokeTheHostLines() =>
            Assert.Equal(new CrosstalkParticipant(10, "Dusty"), stamped.Host);

        [Fact]
        public void TheNeighborSideIsThePersonaWhoSpokeTheNeighborLines() =>
            Assert.Equal(new CrosstalkParticipant(20, "Flip"), stamped.Neighbor);
    }

    // The call site in the worker: the exchange it STOCKS carries the cast (a revert to Script: assembled.Script goes red).
    public sealed class ScenarioTheWorkerStocksAScriptNamingTheCast : IAsyncLifetime
    {
        const string ShowSlug = "morning-drive";

        CrosstalkAiredScript? stocked;

        public async Task InitializeAsync()
        {
            var now = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero); // a Monday noon
            CrosstalkPlanner? planner = null;
            var (worker, _, _, _, _, _) = await CrosstalkWorkerHarness.BuildAsync(
                now, ShowSlug, "Morning Drive",
                stockingSynthesizer: new SilentWavSynthesizer(), capturePlanner: captured => planner = captured);

            await worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

            var previous = new ScheduleSegment(1, DayOfWeek.Monday, 0, 480, PersonaId: 30, Genres: null, EnergyMin: null, EnergyMax: null);
            var host = new ScheduleSegment(
                2, DayOfWeek.Monday, 480, 960, PersonaId: 10, Genres: null, EnergyMin: null, EnergyMax: null,
                Show: new ShowSummary(1, "Morning Drive", null, null) { Slug = ShowSlug });
            var next = new ScheduleSegment(3, DayOfWeek.Monday, 960, 1440, PersonaId: 20, Genres: null, EnergyMin: null, EnergyMax: null);
            stocked = planner?.TryVend(ShowSlug, host, new ScheduleWeekSnapshot([previous, host, next]))?.Script;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheStockedExchangeExists() => Assert.NotNull(stocked);

        [Fact]
        public void ItsHostSideIsTheHostBlockPersona() => Assert.Equal(10, stocked?.Host?.PersonaId);

        [Fact]
        public void ItsNeighborSideIsTheNextBlockPersona() => Assert.Equal(20, stocked?.Neighbor?.PersonaId);
    }
}
