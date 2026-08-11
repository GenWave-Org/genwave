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

        // SPEC F117.2 (STORY-309, PLAN T250 review finding F3) — the show-branded variant: the
        // Orchestrator's drain arm stamps SegmentRequest.ShowName only when a show is on air and the
        // authored pool came up empty; this renderer is what turns that stamp into the literal spoken
        // text. GenWave.Orchestration.Tests/Specs/Story309_ShowIdentDrain.cs's own facts stop at
        // proving the Orchestrator stamped the right fields onto the request — this is where the
        // ACTUAL rendered text is pinned.

        [Fact]
        public void ShowNameStampsTheShowBrandedLine()
        {
            var req = new SegmentRequest(
                SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.Now, "test-station",
                ShowName: "The Morning Mix");
            var text = renderer.Expand(req);
            Assert.Equal("You're listening to The Morning Mix on GenWave.", text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankShowNameFallsBackToThePlainIdent(string? showName)
        {
            var req = new SegmentRequest(
                SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.Now, "test-station",
                ShowName: showName);
            var text = renderer.Expand(req);
            Assert.Equal("You're listening to GenWave.", text);
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
        // SPEC F110.3, PLAN T232: top-of-hour, o'clock phrasing — the hour spoken as a word (never
        // digits), read off SegmentRequest.LocalNow's hour component. Minutes never enter into it:
        // the ONE producer of this kind (ClockAnchoredImagingProducer) only ever arms a top-of-hour
        // due instant, so a 14:37 LocalNow below is a stand-in for "whatever minute the drain
        // happens to land on" — the template only ever reads the hour.
        readonly PatterTemplateRenderer renderer = new();

        [Fact]
        public void OutputSpeaksTheHourAsAWord()
        {
            var local = new DateTimeOffset(2026, 6, 9, 14, 37, 0, TimeSpan.FromHours(-4));
            var req = new SegmentRequest(SegmentKind.TimeDate, "af_heart", "GenWave", null, local, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("two o'clock", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OutputJoinsTheStationIdentButNeverDigits()
        {
            // gh-#453 (Dean, 2026-08-11, after the first live listen): the station name JOINS the
            // line — bare time copy "sounds strange, like we're a time signal". Overturns T232's
            // original no-station-name cut; digits stay banned (spoken words only). The forever-cache
            // re-keys by construction (the key is the rendered text), so the fleet self-heals on
            // deploy exactly like the F110.3 per-hour warm.
            var local = new DateTimeOffset(2026, 6, 9, 14, 37, 0, TimeSpan.FromHours(-4));
            var req = new SegmentRequest(SegmentKind.TimeDate, "af_heart", "GenWave", null, local, "test-station");
            var text = renderer.Expand(req);
            Assert.Contains("on GenWave", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\d", text);
        }

        [Fact]
        public void MidnightAndNoonBothSpeakTwelve()
        {
            var midnight = new DateTimeOffset(2026, 6, 9, 0, 5, 0, TimeSpan.FromHours(-4));
            var noon = new DateTimeOffset(2026, 6, 9, 12, 5, 0, TimeSpan.FromHours(-4));

            var midnightText = renderer.Expand(
                new SegmentRequest(SegmentKind.TimeDate, "af_heart", "GenWave", null, midnight, "test-station"));
            var noonText = renderer.Expand(
                new SegmentRequest(SegmentKind.TimeDate, "af_heart", "GenWave", null, noon, "test-station"));

            Assert.Contains("twelve o'clock", midnightText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("twelve o'clock", noonText, StringComparison.OrdinalIgnoreCase);
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

    // ---------------------------------------------------------------------
    // EXHAUSTIVENESS GUARD
    // ---------------------------------------------------------------------

    public sealed class ScenarioEveryDefinedKindHasCopy
    {
        // PLAN T223 review (F2): nothing drove every SegmentKind through Expand, so a new kind
        // (ContextSegment, SPEC F107.3) landed on the switch's uncovered default arm and threw
        // ArgumentOutOfRangeException — reachable in production via POST /api/personas/preview —
        // with the whole suite still green. This Theory walks Enum.GetValues<SegmentKind>() rather
        // than naming kinds by hand, so the NEXT new kind is caught here automatically instead of
        // relying on every future kind's author remembering to add its own scenario above.

        readonly PatterTemplateRenderer renderer = new();

        public static IEnumerable<object[]> AllKinds() =>
            Enum.GetValues<SegmentKind>().Select(kind => new object[] { kind });

        [Theory]
        [MemberData(nameof(AllKinds))]
        public void ExpandReturnsNonEmptyCopyAndNeverThrows(SegmentKind kind)
        {
            var req = new SegmentRequest(kind, "af_heart", "GenWave", Track: null, DateTimeOffset.Now, "test-station");

            string? text = null;
            var ex = Record.Exception(() => text = renderer.Expand(req));

            Assert.Null(ex);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }
}
