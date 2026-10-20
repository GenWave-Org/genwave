// gh-#889 — playout: a cold start with an empty safe scope aired silence forever. ObserveAsync
// returned false whenever the engine's output carried no metadata at all, so RefillAsync never
// ran; with no safe rotation to produce the drain token, nothing ever changed. Found by the
// nightly fresh leg in gh-#888: a failed seed render left the safe scope empty and 4 ready tracks
// never aired. The fix treats "nothing on air" as an empty engine and fills it (SPEC F1.3, F7.5).

using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureColdStartRefill
{
    static MediaItem Item(string id) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true));

    static IReadOnlySet<string> Real(params string[] ids) => new HashSet<string>(ids);

    static FakeRotationSettingsProvider Rotation() =>
        new(new RotationSettings { RecentWindow = 10, ArtistSeparation = 0 });

    public sealed class ScenarioSilentEngineGetsFilled
    {
        [Fact]
        public async Task The_first_tick_pushes_ready_music_into_a_silent_engine()
        {
            // Given an engine that has aired nothing (empty safe scope, no metadata) and ready music
            var ls = new FakeLiquidsoapControl([null], Real());
            var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(Item("m1")), Rotation());

            // When the feeder ticks
            await feeder.TickAsync(CancellationToken.None);

            // Then the music is pushed
            var pushed = Assert.Single(ls.Pushed);
            Assert.Equal("m1", pushed.MediaId);
        }

        [Fact]
        public async Task An_empty_selection_is_retried_on_the_next_tick()
        {
            // Given a silent engine and a selection seam that has nothing on the first pull
            var ls = new FakeLiquidsoapControl([null, null], Real());
            var provider = new FakeNextItemProvider(null, Item("m1"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation());

            // When the feeder ticks twice
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            // Then the second tick pulls again and pushes
            Assert.Equal(2, provider.Calls.Count);
            Assert.Equal("m1", Assert.Single(ls.Pushed).MediaId);
        }
    }

    public sealed class ScenarioAPushStillResolvingIsNotDuplicated
    {
        [Fact]
        public async Task The_refill_waits_out_the_grace_then_retries_a_lost_push()
        {
            // Given a silent engine that never airs the pushed track
            var ls = new FakeLiquidsoapControl([null], Real());
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation());

            // When the feeder ticks once to push, then through the grace window
            await feeder.TickAsync(CancellationToken.None);
            for (var i = 1; i < PlayoutFeeder.SafeCoverTicksBeforePushLossSignal; i++)
                await feeder.TickAsync(CancellationToken.None);

            // Then nothing is pushed again while the first push may still be resolving
            Assert.Single(ls.Pushed);

            // When the grace runs out
            await feeder.TickAsync(CancellationToken.None);

            // Then the refill retries
            Assert.Equal(["m1", "m2"], ls.Pushed.Select(p => p.MediaId));
        }
    }

    public sealed class ScenarioTheColdPushReachingAirIsANormalAdvance
    {
        [Fact]
        public async Task A_mid_chain_segment_airing_does_not_queue_a_second_chain()
        {
            // Given a cold start that pushed a patter segment followed by music
            var ls = new FakeLiquidsoapControl([null, "tts:x"], Real("tts:x"));
            var provider = new FakeNextItemProvider(Item("tts:x"), Item("m1"), Item("m2"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation());
            await feeder.TickAsync(CancellationToken.None);

            // When the segment reaches air
            await feeder.TickAsync(CancellationToken.None);

            // Then the feeder recognises its own chain and does not push another one
            Assert.Equal(["tts:x", "m1"], ls.Pushed.Select(p => p.MediaId));
        }
    }

    public sealed class ScenarioAnEngineRestartMidRunIsRefilled
    {
        [Fact]
        public async Task Metadata_vanishing_after_real_airplay_triggers_a_refill()
        {
            // Given a feeder that has been airing real music, then an engine restart wipes the output
            var ls = new FakeLiquidsoapControl(["m1", null], Real("m1"));
            var provider = new FakeNextItemProvider(Item("m2"), Item("m3"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation());
            await feeder.TickAsync(CancellationToken.None);

            // When the next tick sees nothing on air
            await feeder.TickAsync(CancellationToken.None);

            // Then the empty engine is refilled immediately
            Assert.Equal(["m2", "m3"], ls.Pushed.Select(p => p.MediaId));
        }
    }
}
