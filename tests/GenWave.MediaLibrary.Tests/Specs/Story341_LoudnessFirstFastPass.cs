// STORY-341 — Loudness-first fast pass (F135)
//
// BDD specification — xUnit. Integration: real Postgres via DatabaseCollection + real
// ffmpeg (the suite's standing tools). Specs Skip-pinned until T314 lands.
//
// The contract under spec: first-pass enrichment slims to TagLib (tags + duration) +
// loudness only → the existing atomic write flips state='ready' with cue/energy/BPM
// NULL; the second-tier backfill lanes sweep the rest; the failure contract and the
// engine-facing row shape (F135.2) are unchanged.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureLoudnessFirstFastPass
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the slim first pass (AC1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFastPassMakesARowReadyWithLoudnessOnly(DatabaseFixture db)
    {
        // Arrange: insert a discovered row for a real generated WAV; run first-pass
        // enrichment (Enricher.EnrichAsync via the service path).

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void RowReachesStateReady()
        {
            _ = db;
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void IntegratedLufsIsMeasured()
        {
            _ = db;
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void DurationMsIsSetFromTagRead()
        {
            _ = db;
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void CueColumnsRemainNull()
        {
            // cue_in_sec, cue_out_sec, cue_analyzed_at all NULL after the fast pass —
            // the backfill predicate (state='ready' AND cue_analyzed_at IS NULL) finds it.
            _ = db;
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void EnergyColumnsRemainNull()
        {
            _ = db;
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void BpmRemainsNull()
        {
            _ = db;
            Assert.Fail("pending T314");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the backfill sweeps the rest (AC2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBackfillLanesSweepFastPassRows(DatabaseFixture db)
    {
        // Arrange: a fast-pass-ready row; run the second-tier backfill lanes
        // (cue → energy → bpm) via the existing EnrichmentService paths.

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void CueBackfillFillsCueColumns()
        {
            _ = db;
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void EnergyBackfillFillsIntroAndOutroEnergy()
        {
            _ = db;
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void BpmBackfillFillsBpm()
        {
            _ = db;
            Assert.Fail("pending T314");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — air is safe the whole time (AC3: the row shape F135.2 leans on)
    // ---------------------------------------------------------------------

    [Trait("Category", "Unit")]
    public sealed class ScenarioAFastPassRowAnnotatesSafely
    {
        // Arrange: a MediaRow shaped exactly like a fast-pass product — real LUFS,
        // measurable, cue/energy NULL.

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void ResolveCueReturnsNullSoCueKeysAreOmitted()
        {
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void ResolveEnergyReturnsNullsSoTheFixedCrossfadeApplies()
        {
            Assert.Fail("pending T314");
        }

        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void ToReferenceCarriesTheMeasuredLoudness()
        {
            // replay_gain is real from the first airing — loudness matching never sacrificed.
            Assert.Fail("pending T314");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — fairness on a big drop (AC4 / F135.4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDiscoveryKeepsPriorityOverBackfill(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void NewDiscoveredRowsReachReadyBeforeBackfillDrainsItsQueue()
        {
            // Seed a deep backfill queue + fresh discovered rows; run the service tick;
            // assert the discovered rows flip ready ahead of backfill completion.
            _ = db;
            Assert.Fail("pending T314");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — loudness failure still fails (AC5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLoudnessFailureStillMarksFailed(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T314 — see docs/PLAN.md")]
        public void ARowWhoseLoudnessAnalysisThrowsGoesStateFailed()
        {
            // A non-audio file behind an audio extension: fast pass runs, loudness
            // throws, MarkFailedAsync fires — the failure contract is untouched.
            _ = db;
            Assert.Fail("pending T314");
        }
    }
}
