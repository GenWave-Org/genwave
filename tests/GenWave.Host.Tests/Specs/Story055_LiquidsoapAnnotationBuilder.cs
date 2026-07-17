// STORY-055 — Extract shared annotation builder (BuildAnnotation)
//
// BDD specification — xUnit. Extract the annotate:...:/path construction from
// LiquidsoapControl.PushAsync into a testable helper (LiquidsoapAnnotationBuilder.Build).
// STORY-056 reuses it so the safe-track endpoint produces byte-identical annotations to
// main-rotation pushes (SPEC F21.4). Zero behavior change on PushAsync callers.
// See docs/PLAN.md Epic K.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Engine;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureLiquidsoapAnnotationBuilder
{
    static readonly GenWave.Core.Domain.Loudness DefaultLoudness =
        new(-16.0, -1.0, Measurable: true);

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

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioBuilderProducesTheAnnotationString
    {
        [Fact]
        public void HelperExists()
        {
            // AC1 — LiquidsoapAnnotationBuilder.Build exists as a callable static helper returning
            //        string, accepting (MediaItem item, double gainDb, string stationId, string stationName).
            var item = new MediaItem("1", "/media/1.mp3", "Title", DefaultLoudness);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.NotNull(result);
            Assert.IsType<string>(result);
        }

        [Fact]
        public async Task PushAsyncCallsTheHelper()
        {
            // AC2 — LiquidsoapControl.PushAsync sends an annotate string produced by the helper,
            //       not by inline construction (verified by asserting equality against Build(...)).
            await using var server = new FakeEngineServer(_ => "42");
            var control = Control(server);
            var item = new MediaItem("99", "/media/99.mp3", "Track", DefaultLoudness);

            await control.PushAsync(item, -1.50, CancellationToken.None);

            var expected = LiquidsoapAnnotationBuilder.Build(item, -1.50, "st-01", "GenWave");
            var command = Assert.Single(server.Commands);
            // The push command is "<queue>.push <annotation>"; the annotation suffix is what we verify.
            Assert.EndsWith(expected, command, StringComparison.Ordinal);
        }

        [Fact]
        public void AnnotationCarriesTrackIdReplayGainAndTitle()
        {
            // AC3 — Build with MediaId="42", Title="Song", gainDb=-3.20 returns a string
            //       containing track_id="42", replay_gain="-3.20 dB", and title="Song" in one
            //       comma-separated annotate:...:/path clause.
            var item = new MediaItem("42", "/media/42.mp3", "Song", DefaultLoudness);

            var result = LiquidsoapAnnotationBuilder.Build(item, -3.20, string.Empty, string.Empty);

            Assert.Contains("track_id=\"42\"", result, StringComparison.Ordinal);
            Assert.Contains("replay_gain=\"-3.20 dB\"", result, StringComparison.Ordinal);
            Assert.Contains("title=\"Song\"", result, StringComparison.Ordinal);
            Assert.StartsWith("annotate:", result, StringComparison.Ordinal);
            Assert.EndsWith("/media/42.mp3", result, StringComparison.Ordinal);
        }

        [Fact]
        public void AnnotationStampsCuePointsWhenPresent()
        {
            // AC4 — Build with Cue = new CuePoints(3.45, 187.20) yields
            //       liq_cue_in="3.45",liq_cue_out="187.20" in the string.
            var item = new MediaItem("10", "/media/10.mp3", "Track", DefaultLoudness,
                Cue: new CuePoints(3.45, 187.20));

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);

            Assert.Contains("liq_cue_in=\"3.45\"", result, StringComparison.Ordinal);
            Assert.Contains("liq_cue_out=\"187.20\"", result, StringComparison.Ordinal);
        }

        [Fact]
        public void AnnotationStampsEnergyWhenPresent()
        {
            // AC6 — Build with IntroEnergy=0.34, OutroEnergy=0.28 yields
            //       gw_intro_energy="0.34",gw_outro_energy="0.28" in the string.
            var item = new MediaItem("11", "/media/11.mp3", "Track", DefaultLoudness,
                IntroEnergy: 0.34,
                OutroEnergy: 0.28);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);

            Assert.Contains("gw_intro_energy=\"0.34\"", result, StringComparison.Ordinal);
            Assert.Contains("gw_outro_energy=\"0.28\"", result, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — omission + escape
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnnotationOmitsNullFieldsAndEscapesUnsafeInput
    {
        [Fact]
        public void CuePointsAreOmittedEntirelyWhenNull()
        {
            // AC5 — with Cue=null, the produced string contains no liq_cue_in and no liq_cue_out
            //       (never "0", never empty strings — SPEC F13.4).
            var item = new MediaItem("20", "/media/20.mp3", "Track", DefaultLoudness);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);

            Assert.DoesNotContain("liq_cue_in", result, StringComparison.Ordinal);
            Assert.DoesNotContain("liq_cue_out", result, StringComparison.Ordinal);
        }

        [Fact]
        public void EnergyIsOmittedEntirelyWhenNull()
        {
            // AC7 — with IntroEnergy=null or OutroEnergy=null, the produced string contains no
            //       gw_intro_energy and no gw_outro_energy (SPEC F17.5).
            var item = new MediaItem("21", "/media/21.mp3", "Track", DefaultLoudness);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);

            Assert.DoesNotContain("gw_intro_energy", result, StringComparison.Ordinal);
            Assert.DoesNotContain("gw_outro_energy", result, StringComparison.Ordinal);
        }

        [Fact]
        public void CrLfInStringValuesIsStrippedOrEscaped()
        {
            // AC8 — a Title containing '\r' or '\n' is passed through the shipped Escape() so the
            //       annotate line is one line (telnet protocol constraint).
            var item = new MediaItem("30", "/media/30.mp3", "Line1\nLine2\rEnd", DefaultLoudness);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);

            Assert.DoesNotContain('\n', result);
            Assert.DoesNotContain('\r', result);
        }
    }

    public sealed class ScenarioZeroBehaviorChangeOnPushAsync
    {
        [Fact]
        public void ExistingLiquidsoapControlTestsStayGreen()
        {
            // AC9 — the annotation produced by Build for a typical item carries the same substrings
            //       that LiquidsoapControlTests asserts: station_id, station_name, gw_tts.
            var item = new MediaItem("3", "/media/3.mp3", "Title", DefaultLoudness);

            var result = LiquidsoapAnnotationBuilder.Build(item, 0.0, "st-01", "GenWave");

            Assert.Contains("station_id=\"st-01\"", result, StringComparison.Ordinal);
            Assert.Contains("station_name=\"GenWave\"", result, StringComparison.Ordinal);
            Assert.Contains("gw_tts=\"false\"", result, StringComparison.Ordinal);
        }
    }
}
