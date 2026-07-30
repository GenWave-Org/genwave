// gh-#257 — HTML entities leak into display ("Paul &amp; Manuel" under Recent Plays / ON AIR)
//
// BDD specification — xUnit. Root cause: some export pipelines write entity-encoded tag frames
// (the tag literally holds "Paul &amp; Manuel"); TagLib returns the frame verbatim, the catalog
// stored it verbatim, and every display surface renders text nodes (never HTML) — so the entity
// showed on screen. The fix decodes exactly once at the single seam external tag text enters the
// system: TagText.Normalize inside the Enricher. Downstream (annotate → engine echo →
// now-playing/play-history → admin + spectator) is a pure pass-through — see the Host-side
// round-trip spec Gh257_AmpersandArtistRoundTrip.
//
// The Enricher scenario needs ffmpeg to author the tagged fixture file, so it carries
// [Trait("Category", "Integration")] — the same convention as Story016's ffmpeg scenarios.
// The TagText scenarios are pure and run everywhere.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTagEntityDecode
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the decode seam
    // ---------------------------------------------------------------------

    public sealed class ScenarioEntityEncodedTagDecodesOnce
    {
        [Fact]
        public void AmpersandEntityDecodesToTheLiteralAmpersand()
        {
            Assert.Equal("Paul & Manuel", TagText.Normalize("Paul &amp; Manuel"));
        }

        [Fact]
        public void NumericCharacterReferenceDecodes()
        {
            Assert.Equal("Beyoncé", TagText.Normalize("Beyonc&#233;"));
        }

        [Fact]
        public void DoubleEncodedInputIsDecodedExactlyOneStep()
        {
            // Decode-once, never decode-until-stable: a value that was genuinely double-encoded
            // upstream surfaces with one layer removed, not silently collapsed to the raw form.
            Assert.Equal("Paul &amp; Manuel", TagText.Normalize("Paul &amp;amp; Manuel"));
        }
    }

    public sealed class ScenarioPlainTextPassesThroughUntouched
    {
        [Fact]
        public void ALiteralAmpersandArtistIsUnchanged()
        {
            Assert.Equal("Paul & Manuel", TagText.Normalize("Paul & Manuel"));
        }

        [Fact]
        public void ABareAmpersandInsideAWordIsNotAnEntity()
        {
            Assert.Equal("R&B", TagText.Normalize("R&B"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — blank collapse keeps the honest-absence rule
    // ---------------------------------------------------------------------

    public sealed class ScenarioBlankCollapsesToNull
    {
        [Fact]
        public void NullStaysNull()
        {
            Assert.Null(TagText.Normalize(null));
        }

        [Fact]
        public void WhitespaceCollapsesToNull()
        {
            Assert.Null(TagText.Normalize("   "));
        }

        [Fact]
        public void AnEntityThatDecodesToPureWhitespaceCollapsesToNull()
        {
            // "&nbsp;" decodes to U+00A0 — whitespace-only after the decode must null out exactly
            // like a literal blank would have.
            Assert.Null(TagText.Normalize("&nbsp;"));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the seam is actually wired into enrichment
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioEnrichmentDecodesEntityEncodedTags
    {
        [Fact]
        public async Task AFileTaggedWithEntityEncodedArtistEnrichesToTheDecodedValue()
        {
            // Given a real file whose tag frames literally hold entity-encoded text (the gh-#257
            // demo-library shape), when the enricher reads its tags, then the catalog-bound result
            // carries the decoded artist — proven through EnrichAsync, not TagText in isolation.
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(
                    dir, "encoded.mp3",
                    title: "Rock &amp; Roll", artist: "Paul &amp; Manuel", genre: "R&amp;B");

                var enricher = new Enricher(
                    new FakeLoudnessAnalyzer(),
                    new FakeCueAnalyzer(),
                    new FakeEnergyAnalyzer(),
                    new FakeBpmAnalyzer(),
                    NullLogger<Enricher>.Instance);

                var result = await enricher.EnrichAsync(path, CancellationToken.None);

                Assert.Equal(("Rock & Roll", "Paul & Manuel", "R&B"),
                    (result.Title, result.Artist, result.Genre));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
