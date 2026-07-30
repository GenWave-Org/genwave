// gh-#257 — HTML entities leak into display ("Paul &amp; Manuel" under Recent Plays / ON AIR)
//
// BDD specification — xUnit. The fix decodes entity-encoded tag text exactly once at catalog
// ingest (GenWave.MediaLibrary's TagText.Normalize — see Gh257_TagEntityDecode in that test
// project). These facts pin the OTHER half of the contract: everything downstream of the catalog
// is a pure pass-through, so a decoded ampersand artist survives the full engine round trip —
// annotate push line → engine output-metadata echo → ParseCurrentFrame → ExtractAnnotations —
// with no layer re-encoding (or re-decoding) it. Also root-caused and ruled out here: the
// icecast status-json.xsl escape gotcha does not apply to this codebase — now-playing text never
// touches icecast's status JSON (the feeder reads output.icecast.metadata over the engine telnet
// socket; the only icecast poll anywhere is IcecastListenerStatsSource's listener count from
// admin/stats.xml, XML-parsed).

using GenWave.Core.Domain;
using GenWave.Host.Engine;

using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAmpersandArtistRoundTrip
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — an ampersand artist survives push → echo → extraction
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnnotationCarriesTheLiteralAmpersand
    {
        static readonly string Annotation = LiquidsoapAnnotationBuilder.Build(
            new MediaItem(
                "42", "/media/duet.mp3", "Two Robots",
                new CoreLoudness(-16.0, -1.0, Measurable: true),
                Artist: "Paul & Manuel"),
            gainDb: -2.5, stationId: "station-1", stationName: "GenWave");

        [Fact]
        public void TheArtistFieldIsStampedVerbatim()
        {
            Assert.Contains("artist=\"Paul & Manuel\"", Annotation, StringComparison.Ordinal);
        }

        [Fact]
        public void NoHtmlEntityIsEverIntroducedOnThePushPath()
        {
            Assert.DoesNotContain("&amp;", Annotation, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioEngineEchoRoundTripsTheAmpersand
    {
        // The engine's output.icecast.metadata reply frame for the push above — key="value"
        // lines exactly as LiquidsoapControl.ParseCurrentFrame receives them off the telnet
        // socket (frame 1 = current/newest).
        const string EngineReply =
            "--- 1 ---\n" +
            "track_id=\"42\"\n" +
            "artist=\"Paul & Manuel\"\n" +
            "title=\"Two Robots\"\n" +
            "replay_gain=\"-2.50 dB\"";

        static readonly EngineMetadata Metadata = new(LiquidsoapControl.ParseCurrentFrame(EngineReply));

        [Fact]
        public void TheExtractedArtistIsTheLiteralAmpersandValue()
        {
            Assert.Equal("Paul & Manuel", Metadata.ExtractAnnotations().Artist);
        }

        [Fact]
        public void TheExtractedTitleIsUntouched()
        {
            Assert.Equal("Two Robots", Metadata.ExtractAnnotations().Title);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a value that reaches the engine already encoded is NOT
    // decoded here: the ingest seam is the only decoder (single source of
    // truth), so a pre-fix catalog row surfaces as stored until re-enriched.
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheEchoPathNeverDecodes
    {
        const string EngineReply =
            "--- 1 ---\n" +
            "track_id=\"43\"\n" +
            "artist=\"Paul &amp; Manuel\"\n" +
            "title=\"Stale Row\"";

        [Fact]
        public void AnEncodedEchoPassesThroughVerbatim()
        {
            var metadata = new EngineMetadata(LiquidsoapControl.ParseCurrentFrame(EngineReply));

            Assert.Equal("Paul &amp; Manuel", metadata.ExtractAnnotations().Artist);
        }
    }
}
