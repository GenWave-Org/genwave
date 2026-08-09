// STORY-303 — The straddle handoff (F111, gh-#320, closes gh-#300)
//
// BDD specification — xUnit. F111.3's own half: the crossing-track back-announce line is a pure
// LlmPromptBuilder seam (prompt content only) — proven directly here, mirroring Story243's own
// GenWave.Tts.Tests split (this project has no ProjectReference to GenWave.Orchestration). The
// wiring facts (the straddle assembly itself, the hold-set, the captured title/artist actually
// reaching a SegmentRequest) depend on the T235 Orchestrator producer and live in
// GenWave.Orchestration.Tests/Specs/Story303_StraddleHandoff.cs instead.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;

public static class FeatureStraddleHandoff
{
    const string StationClockLine = "Current date/time (station-local): irrelevant";
    const string StationId = "test-station";

    static readonly DateTimeOffset FixedLocalNow = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    static SegmentRequest SignOnRequest(
        string? counterpartName, string? crossingTrackTitle, string? crossingTrackArtist) =>
        new(
            SegmentKind.SignOn, "af_heart", "GenWave", Track: null, FixedLocalNow, StationId,
            PersonaName: null, CounterpartName: counterpartName,
            CrossingTrackTitle: crossingTrackTitle, CrossingTrackArtist: crossingTrackArtist);

    public sealed class ScenarioSignOnBackAnnouncesTheCrossingTrack
    {
        [Fact]
        public void SignOnPromptNamesTheCrossingTrackWhenCaptured()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                SignOnRequest("Nite Owl", "Midnight Drive", "The Testers"), StationClockLine,
                previouslyVoicedTasteNotes: []);

            Assert.Contains("Midnight Drive", content);
            Assert.Contains("The Testers", content);
        }

        [Fact]
        public void SignOnPromptCanAlsoThankTheCounterpart()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                SignOnRequest("Nite Owl", "Midnight Drive", "The Testers"), StationClockLine,
                previouslyVoicedTasteNotes: []);

            Assert.Contains("Nite Owl", content);
            Assert.Contains("thank", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SignOnPromptNamesTheTrackEvenWithNoCounterpart()
        {
            // The one-sided straddle (F92.3's "into music-only" predecessor, no SignOff at all) —
            // the crossing-track line stands on its own with no counterpart to thank.
            var content = LlmPromptBuilder.BuildUserContent(
                SignOnRequest(counterpartName: null, "Midnight Drive", "The Testers"), StationClockLine,
                previouslyVoicedTasteNotes: []);

            Assert.Contains("Midnight Drive", content);
        }

        [Fact]
        public void SignOnPromptOmitsArtistWhenUntagged()
        {
            var content = LlmPromptBuilder.BuildUserContent(
                SignOnRequest("Nite Owl", "Midnight Drive", crossingTrackArtist: null), StationClockLine,
                previouslyVoicedTasteNotes: []);

            Assert.Contains("Midnight Drive", content);
            Assert.DoesNotContain(" by ", content, StringComparison.Ordinal);
        }

        [Fact]
        public void SignOnPromptOmitsTheStraddleLineWhenNotCaptured()
        {
            // Regression pin (mirrors Story243's own golden-string precedent): every pre-T235
            // SignOn request leaves both crossing-track fields null, and this line must stay
            // byte-identical for it — no "Straddle note" line appears at all.
            var content = LlmPromptBuilder.BuildUserContent(
                SignOnRequest("Daybreak Dana", crossingTrackTitle: null, crossingTrackArtist: null),
                StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.DoesNotContain("Straddle note", content, StringComparison.Ordinal);
        }

        [Fact]
        public void SignOffPromptNeverNamesACrossingTrack()
        {
            // F111.3 only ever enriches the HELD SignOn (Orchestrator.CaptureCrossingTrackForHeldSignOn) —
            // a SignOff request carries no crossing-track fields in production, and even if one
            // somehow did, the Kind gate below keeps the line SignOn-only.
            var signOffWithFields = new SegmentRequest(
                SegmentKind.SignOff, "af_heart", "GenWave", Track: null, FixedLocalNow, StationId,
                PersonaName: null, CounterpartName: "Nite Owl",
                CrossingTrackTitle: "Midnight Drive", CrossingTrackArtist: "The Testers");

            var content = LlmPromptBuilder.BuildUserContent(
                signOffWithFields, StationClockLine, previouslyVoicedTasteNotes: []);

            Assert.DoesNotContain("Midnight Drive", content);
            Assert.DoesNotContain("Straddle note", content, StringComparison.Ordinal);
        }
    }
}
