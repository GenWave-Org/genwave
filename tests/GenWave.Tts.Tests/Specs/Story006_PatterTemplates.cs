// STORY-006 — Patter templates (StationId, LeadIn, BackAnnounce, TimeDate)

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;

public static class FeaturePatterTemplates
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — one Scenario per kind, per AC
    // ---------------------------------------------------------------------

    public sealed class ScenarioStationIdTemplate
    {
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void OutputContainsStationName()
        {
            var req = new SegmentRequest(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("GenWave", text);
        }

        [Fact]
        public void OutputIsASingleSentenceEndingWithTerminalPunctuation()
        {
            var req = new SegmentRequest(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.Matches(@"[.!?]\s*$", text);
        }
    }

    public sealed class ScenarioLeadInTemplate
    {
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void OutputContainsTrackTitle()
        {
            var track = new MediaItem("m1", "/media/x.mp3", "Astral Plane", default);
            var req = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", track, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("Astral Plane", text);
        }

        [Fact]
        public void OutputContainsTrackArtistWhenPresent()
        {
            var track = new MediaItem("m2", "/media/y.mp3", "Astral Plane", default, "Valerie June");
            var req = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", track, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("Valerie June", text);
        }
    }

    public sealed class ScenarioBackAnnounceTemplate
    {
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void OutputContainsTrackTitle()
        {
            var track = new MediaItem("m1", "/media/x.mp3", "Astral Plane", default);
            var req = new SegmentRequest(SegmentKind.BackAnnounce, "af_heart", "GenWave", track, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("Astral Plane", text);
        }

        [Fact]
        public void OutputContainsTrackArtistWhenPresent()
        {
            var track = new MediaItem("m2", "/media/y.mp3", "Astral Plane", default, "Valerie June");
            var req = new SegmentRequest(SegmentKind.BackAnnounce, "af_heart", "GenWave", track, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("Valerie June", text);
        }
    }

    public sealed class ScenarioTimeDateTemplate
    {
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void OutputContainsClockLikeTimeString()
        {
            var local = new DateTimeOffset(2026, 6, 9, 14, 37, 0, TimeSpan.FromHours(-4));
            var req = new SegmentRequest(SegmentKind.TimeDate, "af_heart", "GenWave", null, local, "test-station");
            var text = renderer.Expand(req);
            Assert.Matches(@"\b\d{1,2}[:.]\d{2}\b", text);
        }

        [Fact]
        public void OutputContainsStationName()
        {
            var local = new DateTimeOffset(2026, 6, 9, 14, 37, 0, TimeSpan.FromHours(-4));
            var req = new SegmentRequest(SegmentKind.TimeDate, "af_heart", "GenWave", null, local, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("GenWave", text);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioLeadInWithNullTrackUsesSafeFallback
    {
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void OutputContainsNoLiteralNullToken()
        {
            var req = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", Track: null, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExpansionDoesNotThrowNullReferenceException()
        {
            var req = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", Track: null, DateTimeOffset.Now, "test-station");
            var ex = Record.Exception(() => renderer.Expand(req));
            Assert.IsNotType<NullReferenceException>(ex);
        }
    }

    public sealed class ScenarioMissingArtistFallsBackToTitleOnlyPhrasing
    {
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void OutputContainsTitle()
        {
            var track = new MediaItem("m3", "/media/z.mp3", "Untitled", default);
            var req = new SegmentRequest(SegmentKind.BackAnnounce, "af_heart", "GenWave", track, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("Untitled", text);
        }

        [Fact]
        public void OutputDoesNotContainPlaceholderToken()
        {
            var track = new MediaItem("m3", "/media/z.mp3", "Untitled", default);
            var req = new SegmentRequest(SegmentKind.BackAnnounce, "af_heart", "GenWave", track, DateTimeOffset.Now, "test-station");
            var text = renderer.Expand(req);
            Assert.DoesNotContain("{Track.Artist}", text);
        }
    }
}
