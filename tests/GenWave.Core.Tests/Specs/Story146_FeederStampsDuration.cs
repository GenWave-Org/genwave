// STORY-146 — Duration on the air surfaces (Epic X / SPEC F50, closes gitea-#218) — feeder half.
// The DTO wire half lives in Host.Tests/Specs/Story146_DurationOnAirDtos.cs; the card/history UI
// in admin-ui/__specs__/now-playing-duration.spec.tsx.
//
// BDD specification — xUnit.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederStampsDuration
{
    public sealed class ScenarioDurationRidesThePushTimeStamp
    {
        // Arrange: a MediaItem carrying a known DurationMs, pushed by the feeder then advanced onto —
        // exactly the F50.2 push-time stamp (the feeder already holds the MediaItem; zero DB reads
        // per poll, F16.6 stands).
        static IRotationSettingsProvider DefaultRotation() => new FakeRotationSettingsProvider(new RotationSettings());

        [Fact]
        public async Task AFeederPushedItemCarriesItsDurationInPushedMetadata()
        {
            var item = new MediaItem("m1", "/media/m1.mp3", "Song One",
                new Loudness(-16.0, -1.0, Measurable: true), DurationMs: 225_000);
            var ls = new FakeLiquidsoapControl(["safe", "m1"], new HashSet<string>(["m1"]));
            var provider = new FakeNextItemProvider(item);
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);   // drain → pushes m1 (stamps pushedMeta)
            await feeder.TickAsync(CancellationToken.None);   // m1 advances on-air

            Assert.Equal(225_000, feeder.CurrentOnAir?.DurationMs);
        }

        [Fact]
        public void MediaItemDurationDefaultsNullAndBreaksNoExistingConstruction()
        {
            // The F34 Album/Genre/Year precedent: a trailing optional param, appended last, so every
            // existing positional construction site keeps compiling unchanged.
            var item = new MediaItem("id", "/media/foo.mp3", "title", new Loudness(-23.0, -1.0, true));
            Assert.Null(item.DurationMs);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioEngineInitiatedPlaysStayHonest
    {
        static IRotationSettingsProvider DefaultRotation() => new FakeRotationSettingsProvider(new RotationSettings());

        [Fact]
        public async Task AnEngineOwnedAdvanceCarriesNoDuration()
        {
            // "engine-1" is on-air (carries a track_id) but the feeder never pushed it — a safe
            // rotation / restart play. Duration never rides the annotate line (F50.2), so it must
            // stay null even though the id is otherwise real.
            var ls = new FakeLiquidsoapControl(["safe", "engine-1"], new HashSet<string>(["engine-1"]));
            var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(), DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);   // boot: safe rotation
            await feeder.TickAsync(CancellationToken.None);   // advance to engine-initiated "engine-1"

            Assert.Null(feeder.CurrentOnAir?.DurationMs);
        }

        [Fact]
        public async Task ATtsSegmentCarriesNoDuration()
        {
            // tts:* patter items are never catalog rows — no DurationMs to stamp (F50.6).
            var item = new MediaItem("tts:seg", "/tts/seg.wav", "GenWave",
                new Loudness(-16.0, -1.0, Measurable: true));
            var ls = new FakeLiquidsoapControl(["safe", "tts:seg"], new HashSet<string>(["tts:seg"]));
            var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(item), DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);   // drain → pushes tts:seg
            await feeder.TickAsync(CancellationToken.None);   // tts:seg advances on-air

            Assert.Null(feeder.CurrentOnAir?.DurationMs);
        }
    }
}
