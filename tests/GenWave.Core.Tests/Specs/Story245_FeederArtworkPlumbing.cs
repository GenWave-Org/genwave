// STORY-245 — feeder-side artwork plumbing (SPEC F88.4/F88.5, F93.3, PLAN T125 review F2/F4)
//
// Zero coverage before this file (review finding F4): PushAsync's own EnginePushResult.ArtworkUrl
// and an engine-initiated advance's echoed `url` field both had no fact proving they reach
// OnAirState.ArtworkUrl at all — nor that F2's fail-closed echo gate actually blocks a hostile one.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederArtworkPlumbing
{
    const string TrustedBase = "https://demo.example/spectator/api/artwork/";

    static MediaItem Item(string id) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true));

    static IRotationSettingsProvider DefaultRotation() => new FakeRotationSettingsProvider(new RotationSettings());

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioFeederPushedArtworkUrl
    {
        [Fact]
        public async Task PushResultArtworkUrlCarriesIntoOnAirState()
        {
            // The push's own EnginePushResult.ArtworkUrl (what a real ArtworkUrlResolver.ResolveAsync
            // would have returned) must survive into pushedMeta and, once the pushed id actually
            // airs, into OnAirState.ArtworkUrl — no re-derivation, no second lookup.
            const string token = TrustedBase + "0123456789abcdef0123456789abcdef";
            var ls = new FakeLiquidsoapControl(["safe", "m1"], new HashSet<string> { "m1" }, pushArtworkUrl: token);
            var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(Item("m1")), DefaultRotation());

            await feeder.TickAsync(CancellationToken.None); // drain → push m1
            await feeder.TickAsync(CancellationToken.None); // m1 airs

            Assert.Equal(token, feeder.CurrentOnAir?.ArtworkUrl);
        }
    }

    public sealed class ScenarioEngineInitiatedArtworkEcho
    {
        [Fact]
        public async Task ATrustedEchoedUrlCarriesIntoOnAirState()
        {
            // A legitimate echo (this station's own configured base + artwork path) validates and
            // reaches OnAirState.ArtworkUrl exactly like title/artist/gainDb do for the same play.
            const string legitimate = TrustedBase + "0123456789abcdef0123456789abcdef";
            var ls = new FakeLiquidsoapControl(
                ["100"], new HashSet<string> { "100" },
                urlById: new Dictionary<string, string> { ["100"] = legitimate });
            var feeder = new PlayoutFeeder(
                ls, new FakeNextItemProvider(), DefaultRotation(),
                artworkUrlEchoValidator: FakeArtworkUrlEchoValidator.TrustingPrefix(TrustedBase));

            await feeder.TickAsync(CancellationToken.None); // boot: "100" airs, engine-initiated

            Assert.Equal(legitimate, feeder.CurrentOnAir?.ArtworkUrl);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioHostileEchoIsNeverTrusted
    {
        [Fact]
        public async Task AHostileFileTagUrlNeverReachesOnAirState()
        {
            // PLAN T125 review F2: a safe-rotation play's own file tags (Vorbis URL=, ID3 W-frames)
            // can plant a third-party url that genwave.liq echoes back indistinguishably from our
            // own stamped annotation — the validator must reject it, and OnAirState.ArtworkUrl must
            // stay null rather than carry it through.
            const string hostile = "https://evil.example/tracking-pixel.gif";
            var ls = new FakeLiquidsoapControl(
                ["100"], new HashSet<string> { "100" },
                urlById: new Dictionary<string, string> { ["100"] = hostile });
            var feeder = new PlayoutFeeder(
                ls, new FakeNextItemProvider(), DefaultRotation(),
                artworkUrlEchoValidator: FakeArtworkUrlEchoValidator.TrustingPrefix(TrustedBase));

            await feeder.TickAsync(CancellationToken.None);

            Assert.Null(feeder.CurrentOnAir?.ArtworkUrl);
        }

        [Fact]
        public async Task NoValidatorWiredFailsClosedNeverFabricated()
        {
            // The DI default (no IArtworkUrlEchoValidator wired) must fail CLOSED, not open — an
            // echoed url is never trusted just because nothing said otherwise.
            const string url = TrustedBase + "0123456789abcdef0123456789abcdef";
            var ls = new FakeLiquidsoapControl(
                ["100"], new HashSet<string> { "100" },
                urlById: new Dictionary<string, string> { ["100"] = url });
            var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(), DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);

            Assert.Null(feeder.CurrentOnAir?.ArtworkUrl);
        }
    }
}
