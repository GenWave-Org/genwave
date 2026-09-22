// STORY-462 — Ads take the voice transition (gh-#699 · SPEC F195 · PLAN T541)
//
// BDD specification — xUnit. GREEN: LiquidsoapAnnotationBuilder marks an item as voice when its
// MediaId starts with "tts:" OR its SegmentKind is non-null, so a vended ad spot (a numeric-
// MediaId library row stamped SegmentKind.Ad) enters gw_transition's music->voice duck arm the
// same way a tts: render does.

using GenWave.Core.Domain;
using GenWave.Host.Engine;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdstakethevoicetransition
{
    // Fully qualified: the bare name collides with the GenWave.Loudness namespace inside Host.Tests.
    static readonly GenWave.Core.Domain.Loudness DefaultLoudness = new(-16.0, -1.0, Measurable: true);

    public sealed class ScenarioAKindStampedAdItem
    {
        readonly string annotation;

        public ScenarioAKindStampedAdItem()
        {
            // Given: a PlayoutItem (MediaItem) with a numeric MediaId, SegmentKind = Ad, DurationMs = 28000
            var item = new MediaItem("501", "/media/501.mp3", "Spot", DefaultLoudness,
                DurationMs: 28_000) { SegmentKind = SegmentKind.Ad };

            // When: LiquidsoapAnnotationBuilder annotates it
            annotation = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);
        }

        /// <summary>AC1 — gw_tts="true" is present</summary>
        [Fact]
        public void MarksItAsVoice() =>
            Assert.Contains("gw_tts=\"true\"", annotation, StringComparison.Ordinal);

        /// <summary>
        /// AC2 — liq_cross_duration derived from DurationMs, via the builder's existing half-
        /// duration-clamped-to-[0.2, 3.0] formula (gh-#80). 28000ms / 1000 * 0.5 = 14.0, clamped
        /// to the 3.0s ceiling — the same clamp path the pre-change tts: item already exercised.
        /// </summary>
        [Fact]
        public void StampsTheCrossDurationFromTheItem() =>
            Assert.Contains("liq_cross_duration=\"3.00\"", annotation, StringComparison.Ordinal);
    }

    public sealed class ScenarioATtsPrefixedItem
    {
        // Pre-change builder output for MediaId "tts:abc", Locator "/tts/abc.wav", Title "Blurb",
        // DurationMs 2400, DefaultLoudness, no station id/name/artwork — captured byte-for-byte
        // from LiquidsoapAnnotationBuilder.Build BEFORE the isVoice OR predicate was added (PLAN
        // T541), by the same construction as Story055's MeasuredTtsItemStampsHalfDurationClampedCrossOverride.
        const string PreChangeAnnotation =
            "annotate:track_id=\"tts:abc\",station_id=\"\",station_name=\"\",gw_tts=\"true\"," +
            "replay_gain=\"0.00 dB\",artist=\"\",liq_cross_duration=\"1.20\",title=\"Blurb\":/tts/abc.wav";

        readonly string annotation;

        public ScenarioATtsPrefixedItem()
        {
            // Given: MediaId "tts:abc", no SegmentKind
            var item = new MediaItem("tts:abc", "/tts/abc.wav", "Blurb", DefaultLoudness,
                DurationMs: 2400);

            annotation = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);
        }

        /// <summary>AC3 — byte-identical annotation</summary>
        [Fact]
        public void IsAnnotatedExactlyAsBefore() =>
            Assert.Equal(PreChangeAnnotation, annotation);
    }

    public sealed class ScenarioAMusicItem
    {
        readonly string annotation;

        public ScenarioAMusicItem()
        {
            // Given: numeric MediaId, no SegmentKind
            var item = new MediaItem("77", "/media/77.mp3", "Track", DefaultLoudness);

            annotation = LiquidsoapAnnotationBuilder.Build(item, 0.0, string.Empty, string.Empty);
        }

        /// <summary>AC4 — gw_tts="true" is absent (music stays out of the voice arm)</summary>
        [Fact]
        public void IsNotMarkedAsVoice() =>
            Assert.DoesNotContain("gw_tts=\"true\"", annotation, StringComparison.Ordinal);

        /// <summary>AC4 — gw_tts="false" is still present, byte-identical to today's music output (F195.3)</summary>
        [Fact]
        public void StillStampsGwTtsFalse() =>
            Assert.Contains("gw_tts=\"false\"", annotation, StringComparison.Ordinal);
    }
}
