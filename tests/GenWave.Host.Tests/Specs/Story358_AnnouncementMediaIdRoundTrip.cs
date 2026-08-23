// STORY-358 — the wrapped MediaId survives the whole engine round trip (SPEC F144.1 · PLAN T341,
// T341 review finding F2)
//
// BDD specification — xUnit. AnnouncementMediaId.Wrap prefixes a rendered verbatim segment's own
// tts:{hash} id with the claimed announcement row's own id (SPEC F144.1's carry requirement,
// Orchestrator.EnqueuePatterAsync's own RenderAnnouncementAsync stamps it once, right after render).
// This fact proves that wrapped id is INERT cargo to every layer between push and pull — mirrors
// Gh257_AmpersandArtistRoundTrip.cs's own template one seam over: the annotation builder stamps it
// into track_id verbatim, the engine echoes it back unchanged, and AnnouncementMediaId.TryUnwrap
// recovers the original row id on the far side, with no layer in between needing to know the id is
// even an announcement's.

using GenWave.Core.Domain;
using GenWave.Host.Engine;

using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementMediaIdRoundTrip
{
    public sealed class ScenarioTheWrappedIdSurvivesPushToPull
    {
        // A rendered verbatim segment's own tts:{hash} id ("tts:abc"), wrapped with the claimed
        // announcement row's id (555) — exactly the shape RenderAnnouncementAsync stamps.
        static readonly string WrappedMediaId = AnnouncementMediaId.Wrap(555, "tts:abc");

        static readonly string Annotation = LiquidsoapAnnotationBuilder.Build(
            new MediaItem(
                WrappedMediaId, "/tts/blurbs/abc.wav", "GenWave",
                new CoreLoudness(-23.0, -1.0, Measurable: true)),
            gainDb: 0.0, stationId: "station-1", stationName: "GenWave");

        [Fact]
        public void TheAnnotationStampsTheWrappedIdVerbatim()
        {
            Assert.Contains($"track_id=\"{WrappedMediaId}\"", Annotation, StringComparison.Ordinal);
        }

        [Fact]
        public void TheAnnouncementIdSurvivesTheEngineEchoAndUnwraps()
        {
            // The engine's output.icecast.metadata reply frame for the push above — the SAME
            // key="value" shape LiquidsoapControl.ParseCurrentFrame receives off the telnet socket.
            var reply = $"--- 1 ---\ntrack_id=\"{WrappedMediaId}\"\ntitle=\"GenWave\"";
            var metadata = new EngineMetadata(LiquidsoapControl.ParseCurrentFrame(reply));

            Assert.True(metadata.TryGetMediaId(out var mediaId));
            Assert.True(AnnouncementMediaId.TryUnwrap(mediaId, out var announcementId));
            Assert.Equal(555L, announcementId);
        }
    }
}
