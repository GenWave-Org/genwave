// STORY-034 — Feeder stamps gw_intro_energy / gw_outro_energy (omit when NULL)
//
// BDD specification — xUnit. Drives LiquidsoapControl.PushAsync against a fake telnet
// server (FakeEngineServer) and inspects the annotate string the feeder sends.
// Mirrors Story020_FeederStampsLiqCueAnnotations.cs harness exactly.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Engine;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureFeederStampsEnergyAnnotations
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
            NullLogger<LiquidsoapControl>.Instance);

    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEnergyPresentIsStamped
    {
        static async Task<string> PushWithEnergyAndGetCommand(FakeEngineServer server)
        {
            var item = new MediaItem("id1", "/media/foo.mp3", "The Title", DefaultLoudness,
                IntroEnergy: 0.82, OutroEnergy: 0.31);
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: -2.0, CancellationToken.None);
            return Assert.Single(server.Commands);
        }

        [Fact]
        public async Task AnnotateIncludesGwIntroEnergy()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithEnergyAndGetCommand(server);
            Assert.Contains("gw_intro_energy=\"0.82\"", command);
        }

        [Fact]
        public async Task AnnotateIncludesGwOutroEnergy()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithEnergyAndGetCommand(server);
            Assert.Contains("gw_outro_energy=\"0.31\"", command);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEnergyNullIsOmitted
    {
        static async Task<string> PushWithNullEnergyAndGetCommand(FakeEngineServer server)
        {
            var item = new MediaItem("id2", "/media/foo.mp3", "The Title", DefaultLoudness,
                IntroEnergy: null, OutroEnergy: null);
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: 0.0, CancellationToken.None);
            return Assert.Single(server.Commands);
        }

        [Fact]
        public async Task NullEnergyOmitsGwIntroEnergyEntirely()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithNullEnergyAndGetCommand(server);
            Assert.DoesNotContain("gw_intro_energy", command);
        }

        [Fact]
        public async Task NullEnergyOmitsGwOutroEnergyEntirely()
        {
            await using var server = new FakeEngineServer(_ => "42");
            var command = await PushWithNullEnergyAndGetCommand(server);
            Assert.DoesNotContain("gw_outro_energy", command);
        }
    }

    public sealed class ScenarioValuesAreEscaped
    {
        [Fact]
        public async Task StampedEnergyValuesPassThroughEscape()
        {
            // Energy values run through the existing Escape() helper (CR/LF safety).
            // Double values cannot contain CR/LF in practice, so we verify the command
            // as a whole is single-line and gw_intro_energy is present with a valid value.
            await using var server = new FakeEngineServer(_ => "42");
            var item = new MediaItem("id3", "/media/baz.mp3", "Safe Title", DefaultLoudness,
                IntroEnergy: 0.5, OutroEnergy: 0.75);
            var ls = Control(server);
            await ls.PushAsync(item, gainDb: 0.0, CancellationToken.None);
            var command = Assert.Single(server.Commands);
            Assert.Contains("gw_intro_energy=\"0.5\"", command);
            Assert.Contains("gw_outro_energy=\"0.75\"", command);
            Assert.DoesNotContain("\r", command);
            Assert.DoesNotContain("\n", command);
        }
    }
}
