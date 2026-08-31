// STORY-371 — The nudge in the ranker (SPEC F151.1–F151.4 · PLAN T359, T370)
//
// BDD specification — xUnit. PENDING until T359 (AC4, the pool projection's carrier half) and
// T370 (AC5–AC9, PersonaRanker.Score's additive term). Arrange sketch: pure in-memory
// arrangement of PersonaRankCandidate/PersonaRanker (Story213_PersonaRanker.cs's own idiom — no
// I/O, a seeded IRandomSource) plus, for AC7/AC8, the F84.2/Envelope PRD simulation idiom: N
// picks (500 per STORY-371 AC7/AC8) run in-memory against a fixed candidate pool with nudges set
// per-track, tallying the winner-share distribution — the same seeded-RNG/iterate/tally/assert-a-
// bound shape as Story213_PersonaRanker.cs's own exploration-rate simulation.

using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureTheNudgeInTheRanker
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the carrier, the term, and its bounds
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCandidateCarriesTheNudge
    {
        static readonly MediaReference Track = new(
            MediaId: "nudge-carrier",
            Locator: "/media/nudge-carrier.mp3",
            Title: "Nudge Carrier",
            Loudness: new Loudness(-23.0, -1.0, true),
            DurationMs: null,
            SampleRate: null,
            Channels: null,
            BitrateKbps: null,
            Artist: "Artist",
            Album: null,
            Genre: "Rock",
            Year: null);

        // Given a pool row with nudge 0.6 and play_count 3 (SPEC F151.1's carrier half, PLAN T359),
        // projected through RankerPersonaPickProvider.ToRankCandidate — the ONE production mapping
        // from EnvelopeCandidateRow onto PersonaRankCandidate.
        readonly PersonaRankCandidate candidate = RankerPersonaPickProvider.ToRankCandidate(
            new EnvelopeCandidateRow(Track, Energy: 0.5, Moods: [], RepeatedRecent: false, RepeatedArtist: false)
            {
                Nudge = 0.6,
                PlayCount = 3,
            });

        [Fact]
        public void ThePersonaRankCandidateHasNudgeZeroPointSix() =>
            Assert.Equal(0.6, candidate.Nudge);

        [Fact]
        public void ThePersonaRankCandidateHasPlayCountFromTheLedger() =>
            Assert.Equal(3, candidate.PlayCount);
    }

    public sealed class ScenarioTheRankerTerm
    {
        // Given two identical candidates except nudge 0.6 vs 0, NudgeGain 0.5, When they are
        // scored.
        [Fact(Skip = "pending T370 (STORY-371 AC5)")]
        public void TheScoresDifferByExactlyZeroPointThree() => Assert.Fail("pending T370");
    }

    public sealed class ScenarioRungZeroOnly
    {
        // Given the persona layer disabled, When 1,000 picks run with thumbs present.
        [Fact(Skip = "pending T370 (STORY-371 AC6)")]
        public void ThePickDistributionMatchesAnEmptyThumbTable() => Assert.Fail("pending T370");
    }

    public sealed class ScenarioTheBoundSimulated
    {
        // Given every track but one at nudge -1 and that one at +1, When 500 picks run.
        [Fact(Skip = "pending T370 (STORY-371 AC7)")]
        public void TheFavouredTracksShareStaysAtOrBelowTheExplorationAdjustedCap() =>
            Assert.Fail("pending T370");

        [Fact(Skip = "pending T370 (STORY-371 AC7)")]
        public void ExplorationPicksAreAtLeastFivePercent() => Assert.Fail("pending T370");
    }

    public sealed class ScenarioAUniformNudgeChangesNothing
    {
        // Given every track at +1, When 500 picks run.
        [Fact(Skip = "pending T370 (STORY-371 AC8)")]
        public void TheDistributionMatchesEveryTrackAtZero() => Assert.Fail("pending T370");
    }

    public sealed class ScenarioObservability
    {
        // Given a pick whose winner had nudge 0.6, When the per-pick log line and the
        // booth-log chips are read.
        [Fact(Skip = "pending T370 (STORY-371 AC9)")]
        public void TheLogLineCarriesTheTopThreeNudges() => Assert.Fail("pending T370");

        [Fact(Skip = "pending T370 (STORY-371 AC9)")]
        public void TheChipsIncludeARotationChip() => Assert.Fail("pending T370");
    }
}
