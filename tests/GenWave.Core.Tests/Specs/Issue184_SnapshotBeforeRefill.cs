// gh-#184 — playout: now-playing froze 30-60s at every track start. The tick that first observed
// a music advance was the same tick that refilled the next patter block, awaiting its LLM+TTS
// renders BEFORE CurrentOnAir was assigned — so the API kept serving the already-finished patter
// snapshot for the whole render window. Measured live (demo, 2026-07-28): ~40s and ~38s of stale
// "DJ break" per transition while ICY metadata had flipped within a second of actual air.
//
// The fix splits the tick at the refill boundary: ObserveAsync reconciles engine truth and
// publishes CurrentOnAir in engine-socket time; RefillAsync alone owns the render-bound work
// (and the departed-id release, which must follow the chain writes). TickAsync remains the
// composition of the two, so every pre-split spec drives identical behavior.

using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureSnapshotBeforeRefill
{
    static MediaItem Item(string id) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true));

    static IReadOnlySet<string> Real(params string[] ids) => new HashSet<string>(ids);

    static FakeRotationSettingsProvider Rotation() =>
        new(new RotationSettings { RecentWindow = 10, ArtistSeparation = 0 });

    public sealed class ScenarioObservePublishesBeforeRefillBlocks
    {
        [Fact]
        public async Task The_advance_is_published_while_the_refill_is_still_blocked_on_the_seam()
        {
            // Given a feeder whose selection seam blocks (the live Orchestrator awaiting patter
            // renders), and an engine reporting a real advance onto a track this feeder never
            // pushed — the same shape as a track landing on air and arming the next refill
            var provider = new BlockingNextItemProvider(Item("m2"));
            var ls = new FakeLiquidsoapControl(["m1"], Real("m1"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation());

            // When the observe phase completes
            var observed = await feeder.ObserveAsync(CancellationToken.None);

            // Then the on-air state is already published — before RefillAsync has even started
            Assert.True(observed);
            var onAir = feeder.CurrentOnAir;
            Assert.NotNull(onAir);
            Assert.Equal("m1", onAir.MediaId);

            // When the refill runs, it blocks on the seam — and the published state is unharmed
            var refill = feeder.RefillAsync(CancellationToken.None);
            await provider.Entered;
            Assert.False(refill.IsCompleted,
                "the refill must be the phase that blocks — never the observe/publish path");
            Assert.Equal("m1", feeder.CurrentOnAir?.MediaId);

            // Then releasing the "renders" lets the refill finish and push the prepared chain
            provider.Release();
            await refill;
            Assert.Contains(ls.Pushed, item => item.MediaId == "m2");
        }
    }

    public sealed class ScenarioTickComposesBothPhases
    {
        [Fact]
        public async Task TickAsync_still_performs_observe_and_refill_in_one_call()
        {
            // Given the same arrangement with the seam released up front (no blocking)
            var provider = new BlockingNextItemProvider(Item("m2"));
            provider.Release();
            var ls = new FakeLiquidsoapControl(["m1"], Real("m1"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation());

            // When one composed tick runs
            await feeder.TickAsync(CancellationToken.None);

            // Then it both published the on-air state and refilled the chain
            Assert.Equal("m1", feeder.CurrentOnAir?.MediaId);
            Assert.Contains(ls.Pushed, item => item.MediaId == "m2");
        }
    }

    public sealed class ScenarioColdStartPublishesNothing
    {
        [Fact]
        public async Task An_unresolved_engine_read_leaves_CurrentOnAir_null_and_skips_refill()
        {
            // Given an engine that has not resolved anything yet (cold start)
            var provider = new BlockingNextItemProvider(Item("m2"));
            var ls = new FakeLiquidsoapControl([null], Real("m1"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation());

            // When the observe phase runs
            var observed = await feeder.ObserveAsync(CancellationToken.None);

            // Then there is nothing to publish and nothing to refill against
            Assert.False(observed);
            Assert.Null(feeder.CurrentOnAir);
        }
    }
}
