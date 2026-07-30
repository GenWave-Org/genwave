// gh-#259 — feeder-side DJ attribution plumbing
//
// The spectator dj field is attributed from the item actually on air, not the schedule's live
// answer. That only works if the MediaItem's plan-time DjName stamp survives the feeder's
// pushedMeta round trip into OnAirState — and if an engine-initiated play (safe rotation, never
// feeder-planned) honestly reports no DJ rather than borrowing one.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederDjNamePlumbing
{
    static MediaItem Item(string id, string? djName) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true),
            DjName: djName);

    static IRotationSettingsProvider DefaultRotation() => new FakeRotationSettingsProvider(new RotationSettings());

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioPlannedItemCarriesItsDj
    {
        [Fact]
        public async Task APushedItemsDjNameCarriesIntoOnAirState()
        {
            // The plan-time stamp must survive into pushedMeta and, once the pushed id airs,
            // into OnAirState.DjName — the exact same push-time capture Title/Artist ride.
            var ls = new FakeLiquidsoapControl(["safe", "m1"], new HashSet<string> { "m1" });
            var feeder = new PlayoutFeeder(
                ls, new FakeNextItemProvider(Item("m1", djName: "Nova")), DefaultRotation());

            await feeder.TickAsync(CancellationToken.None); // drain → push m1
            await feeder.TickAsync(CancellationToken.None); // m1 airs

            Assert.Equal("Nova", feeder.CurrentOnAir?.DjName);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioUnplannedAiringsHaveNoDj
    {
        [Fact]
        public async Task AnEngineInitiatedPlayCarriesNullDjName()
        {
            // Safe rotation / a foreign advance was never feeder-planned: no show attribution
            // exists, and none is ever fabricated from whatever the schedule currently says.
            var ls = new FakeLiquidsoapControl(["100"], new HashSet<string> { "100" });
            var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(), DefaultRotation());

            await feeder.TickAsync(CancellationToken.None); // boot: "100" airs, engine-initiated

            Assert.NotNull(feeder.CurrentOnAir);
            Assert.Null(feeder.CurrentOnAir?.DjName);
        }

        [Fact]
        public async Task AnItemPlannedWithNoDjStaysNullOnAir()
        {
            // A gap/music-only plan stamps null — the feeder must carry that null through rather
            // than defaulting it to anything.
            var ls = new FakeLiquidsoapControl(["safe", "m1"], new HashSet<string> { "m1" });
            var feeder = new PlayoutFeeder(
                ls, new FakeNextItemProvider(Item("m1", djName: null)), DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            Assert.Equal("m1", feeder.CurrentOnAir?.MediaId);
            Assert.Null(feeder.CurrentOnAir?.DjName);
        }
    }
}
