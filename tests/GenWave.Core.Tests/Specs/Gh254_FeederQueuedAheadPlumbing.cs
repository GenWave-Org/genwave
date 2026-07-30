// gh-#254 — Feeder half of boundary-fit selection: the queued-ahead drift measurement.
//
// BDD specification — xUnit. The feeder tells the selection seam how much runtime is already
// committed AHEAD of anything the current refill plans (PlayoutContext.QueuedAheadMs): the on-air
// item's remaining time plus any still-queued backlog — served entirely from state the feeder
// already holds (zero engine/DB calls, F16.6 discipline). The Orchestrator-side consumer lives in
// Orchestration.Tests/Specs/Gh254_BoundaryFitSelection.cs.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederQueuedAheadPlumbing
{
    static IRotationSettingsProvider DefaultRotation() => new FakeRotationSettingsProvider(new RotationSettings());

    static MediaItem MakeItem(string id, int? durationMs) => new(
        id, $"/media/{id}.mp3", $"Song {id}",
        new Loudness(-16.0, -1.0, Measurable: true), DurationMs: durationMs);

    public static class ScenarioOnAirRemainderRidesTheContext
    {
        [Fact]
        public static async Task A_freshly_started_on_air_track_reports_its_full_remaining_time()
        {
            // Given a feeder that pushed a 200s track which has just come on-air
            var item = MakeItem("m1", 200_000);
            var followUp = MakeItem("m2", 180_000);
            var ls = new FakeLiquidsoapControl(["safe", "m1"], new HashSet<string>(["m1", "m2"]));
            var provider = new FakeNextItemProvider(item, followUp);
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            // When the boot tick pushes it and the next tick observes it airing (which triggers the
            // chain-end refill that plans the FOLLOWING unit)
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            // Then the boot tick planned against zero (nothing of ours was committed yet) and the
            // follow-up plan saw the airing track's remaining time — the drift the boundary fit
            // corrects for. The observe and the refill run microseconds apart, so "remaining" is
            // within a whisker of the full duration.
            Assert.Equal(2, provider.Calls.Count);
            Assert.Equal(0, provider.Calls[0].QueuedAheadMs);
            Assert.NotNull(provider.Calls[1].QueuedAheadMs);
            Assert.InRange(provider.Calls[1].QueuedAheadMs ?? 0, 195_000, 200_000);
        }
    }

    public static class ScenarioUnknownDurationsContributeNothing
    {
        [Fact]
        public static async Task An_on_air_track_without_a_measured_duration_reports_zero_not_a_guess()
        {
            // Given the airing track carries no duration (unenriched — F50 null semantics)
            var item = MakeItem("m1", durationMs: null);
            var followUp = MakeItem("m2", 180_000);
            var ls = new FakeLiquidsoapControl(["safe", "m1"], new HashSet<string>(["m1", "m2"]));
            var provider = new FakeNextItemProvider(item, followUp);
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            // When it comes on-air and the next unit is planned
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            // Then the measurement stays an honest floor — zero, never a fabricated remainder
            // (measured-never-fabricated, the same F66.1 posture the TTS stamp follows).
            Assert.Equal(2, provider.Calls.Count);
            Assert.Equal(0, provider.Calls[1].QueuedAheadMs);
        }
    }
}
