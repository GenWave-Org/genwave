// STORY-391 — Spots render into the authored library (assembly half: AC1/AC2/AC3/AC5 · F161.2/.3 · pending T401)
// The worker half (AC4/AC6) lives in GenWave.Ads.Tests/Specs/Story391_AdSpotWorker.cs.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureAdRenderAssembly
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCastIsVoicesNotPersonas
    {
        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void EachLineRendersWithItsOwnVoiceSpec()
        {
            // Two-VoiceSpec voice_plan, no persona cards: per-line synth calls carry each
            //   spec's VoiceId/Pace (F161.2 — the widened assembler request).
            Assert.Fail("pending T401");
        }

        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void EveryLinePassesTheNormalizationChokepoint()
        {
            // F68 holds for ads — NormalizingTtsSynthesizer is the hand-off, pinned.
            Assert.Fail("pending T401");
        }

        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void ASingleVoiceSpotAssembles()
        {
            // The 1-line/1-voice announcer-only spot is legal (the >=2 crosstalk rule is
            //   deliberately relaxed on the widened request).
            Assert.Fail("pending T401");
        }
    }

    public sealed class ScenarioTheAuthoredTailLandsIt
    {
        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void OneMeasuredMediaRowLandsInTheAdsLibraryAsAdKind()
        {
            // Real ffmpeg: artifact under /authored/ads/, loudness-measured, imaging_kind='ad',
            //   title = spot title, artist = station name (embedded in the file).
            Assert.Fail("pending T401");
        }

        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void MediaIdAndReadyStampInOneTransaction()
        {
            Assert.Fail("pending T401");
        }

        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void AnOptionalBedMixesDuckedUnderTheVoices()
        {
            // The AudioMixRequest bed path (safe-segments precedent) — cue-trimmed, ducked.
            Assert.Fail("pending T401");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — all-or-nothing
    // ---------------------------------------------------------------------

    public sealed class ScenarioOverCeilingDiscards
    {
        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void AnOverLongArtifactIsDeletedAndTheSpotFailed()
        {
            // spot_seconds × (1 + tolerance) exceeded: delete, state=failed, never trimmed;
            //   the ceiling is per-request — the global crosstalk knob is never consulted.
            Assert.Fail("pending T401");
        }
    }

    public sealed class ScenarioMidPipelineFailureLeavesNothing
    {
        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void ASynthesisMixOrMeasureFailureLeavesNoOrphanFiles()
        {
            Assert.Fail("pending T401");
        }

        [Fact(Skip = "Pending T401 — see docs/PLAN.md")]
        public void TheSpotFailsWithATypedReason()
        {
            Assert.Fail("pending T401");
        }
    }
}
