// STORY-020 — Feeder stamps liq_cue_in / liq_cue_out (omit when NULL)
//
// BDD specification — xUnit. Drives LiquidsoapControl.PushAsync against a fake telnet
// server (FakeEngineServer) and inspects the annotate string the feeder sends.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Engine;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureFeederStampsLiqCueAnnotations
{
    static LiquidsoapControl Control(FakeEngineServer server) =>
        new(new LiquidsoapOptions
            {
                Host = "127.0.0.1",
                Port = server.Port,
                OutputMetadataCommand = "output.icecast.metadata",
            },
            stationId: "st-01",
            new FakeStationIdentityProvider(new StationIdentity("st-01", "GenWave", "af_heart")),
            new ArtworkUrlResolver(
                new FakeOptionsMonitor<StationOptions>(new StationOptions()), new FakeArtworkTokenStore()),
            NullLogger<LiquidsoapControl>.Instance);

    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnnotationIncludesCueFieldsWhenCueIsNonNull
    {
        // Arrange: item with Cue = new CuePoints(3.45, 187.20) is pushed; server returns a valid RID.
        static async Task<string> PushWithCueAndGetCommand(FakeEngineServer server)
        {
            var item = new MediaItem("id1", "/media/foo.mp3", "The Title", DefaultLoudness,
                Cue: new CuePoints(3.45, 187.20));
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: -2.0, CancellationToken.None);
            return Assert.Single(server.Commands);
        }

        [Fact]
        public async Task AnnotateStringContainsLiqCueIn()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithCueAndGetCommand(server);
            Assert.Contains("liq_cue_in=\"3.45\"", command);
        }

        [Fact]
        public async Task AnnotateStringContainsLiqCueOut()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithCueAndGetCommand(server);
            Assert.Contains("liq_cue_out=\"187.20\"", command);
        }

        [Fact]
        public async Task CueValuesUseBareFloatFormatWithTwoOrMoreDecimalPlaces()
        {
            // Reject bare integer or scientific notation; matches Liquidsoap 2.4 expectation.
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithCueAndGetCommand(server);
            Assert.Matches(@"liq_cue_in=""-?\d+\.\d{2,}""", command);
        }
    }

    public sealed class ScenarioAnnotationOmitsCueFieldsWhenCueIsNull
    {
        static async Task<string> PushWithNullCueAndGetCommand(FakeEngineServer server)
        {
            var item = new MediaItem("id2", "/media/foo.mp3", "The Title", DefaultLoudness, Cue: null);
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: 0.0, CancellationToken.None);
            return Assert.Single(server.Commands);
        }

        [Fact]
        public async Task AnnotateStringDoesNotContainLiqCueIn()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithNullCueAndGetCommand(server);
            Assert.DoesNotContain("liq_cue_in", command);
        }

        [Fact]
        public async Task AnnotateStringDoesNotContainLiqCueOut()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithNullCueAndGetCommand(server);
            Assert.DoesNotContain("liq_cue_out", command);
        }

        [Fact]
        public async Task EmptyStringIsNeverSubstituted()
        {
            // Empty liq_cue_in="" / liq_cue_out="" would break Liquidsoap cue resolution.
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithNullCueAndGetCommand(server);
            Assert.DoesNotContain("liq_cue_in=\"\"", command);
        }

        [Fact]
        public async Task ZeroIsNeverSubstituted()
        {
            // A literal "0" is not "no cue" — it's "start from second 0," which the engine handles
            // anyway. We omit, not substitute, to preserve the omit-vs-explicit-zero distinction
            // for the engine maintainers' future-proofing.
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithNullCueAndGetCommand(server);
            Assert.DoesNotContain("liq_cue_in=\"0\"", command);
        }
    }

    public sealed class ScenarioPreExistingAnnotationsAreUnaffected
    {
        static async Task<string> PushWithCueAndGetCommand(FakeEngineServer server)
        {
            var item = new MediaItem("id3", "/media/bar.mp3", "Some Track", DefaultLoudness,
                Cue: new CuePoints(1.0, 200.0));
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: -1.5, CancellationToken.None);
            return Assert.Single(server.Commands);
        }

        [Fact]
        public async Task TrackIdAnnotationStillPresent()
        {
            // Phase 1 regression — track_id="<id>" must remain.
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithCueAndGetCommand(server);
            Assert.Contains("track_id=", command);
        }

        [Fact]
        public async Task ReplayGainAnnotationStillPresent()
        {
            // Regression — replay_gain="X.XX dB" must remain in its existing format.
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithCueAndGetCommand(server);
            Assert.Matches(@"replay_gain=""-?\d+(\.\d+)? dB""", command);
        }

        [Fact]
        public async Task TitleAnnotationStillPresent()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithCueAndGetCommand(server);
            Assert.Contains("title=", command);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioCueValuesContainingCrLfAreStripped
    {
        [Fact]
        public async Task CrLfInCueValueIsStrippedOrEscapedByExistingEscapeHelper()
        {
            // Defensive — double values cannot contain CR/LF, but the command as a whole must not
            // contain raw CR/LF (the existing Escape() helper applies to all annotation values,
            // and the telnet framing must remain a single line).
            await using var server = new FakeEngineServer(_ => "42");
            var item = new MediaItem("id4", "/media/baz.mp3", "Safe Title", DefaultLoudness,
                Cue: new CuePoints(2.0, 180.0));
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: 0.0, CancellationToken.None);
            var command = Assert.Single(server.Commands);
            Assert.DoesNotContain("\r", command);
            Assert.DoesNotContain("\n", command);
        }
    }

    public sealed class ScenarioNegativeCueValuesAreStampedAsIs
    {
        [Fact]
        public async Task NegativeCueInSecAppearsInAnnotation()
        {
            // Edge case: Liquidsoap honors negative cues by clamping to 0 server-side. We do NOT
            // pre-clamp — the value must appear verbatim with its sign.
            await using var server = new FakeEngineServer(_ => "42");
            var item = new MediaItem("id5", "/media/neg.mp3", "Neg Cue", DefaultLoudness,
                Cue: new CuePoints(-0.10, 187.20));
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: 0.0, CancellationToken.None);
            var command = Assert.Single(server.Commands);
            Assert.Contains("liq_cue_in=\"-0.10\"", command);
        }
    }
}
