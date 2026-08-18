// STORY-346 — Adopt the existing box (F137)
//
// BDD specification — xUnit. Drives the REAL ./setup.sh via Process against scratch
// checkouts seeded with specific drift (the Gh019 idiom: scratch PATH, scripted
// docker stubs reporting container/image state, GW_ENV_FILE seam). The do-no-harm
// clause (AC4) is additionally proven on the live Pi 4 at T321 — the wire, not here.
// Specs Skip-pinned until T319 lands.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdoptionVerifyRepair
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the drift probes (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioVerifyReportsEachDriftClass
    {
        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void AMissingEnvKeyVsEnvExampleIsReported()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void ASurvivingPlaceholderIsReported()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void AnUnappliedMigrationIsReportedAgainstTheRepoDbMax()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void StaleBuiltImagesReportTheGh351AgeSkew()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void AnOrphanedProfileContainerIsReported()
        {
            // The de-selected piper/kokoro leftover the compose orphan pass misses.
            Assert.Fail("pending T319");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — repair confirms per item (AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioRepairConfirmsPerItem
    {
        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void EachFindingPrintsTheExactCommandBeforeTheConfirm()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void ADeclinedItemIsSkippedAndTheNextIsOffered()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void DashDashYesAppliesAllFindingsWithoutPrompts()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void AContainerRestartingRepairSaysSoBeforeTheConfirm()
        {
            Assert.Fail("pending T319");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — deliberate divergence is not drift (AC3)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDeliberateDivergenceIsInfo
    {
        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void ADbSettingsOverrideReportsAsInfoNeverAsAFix()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void AnOperatorComposeOverrideReportsAsInfoNeverAsAFix()
        {
            Assert.Fail("pending T319");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the do-no-harm gate (AC4)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioGreenBoxZeroChanges
    {
        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void AHealthyBoxReportsGreenAndExitsZero()
        {
            Assert.Fail("pending T319");
        }

        [Fact(Skip = "Pending T319 — see docs/PLAN.md")]
        public void VerifyModeMakesZeroWritesToTheBox()
        {
            // The scratch checkout's full tree + the stub journals prove no mutation:
            // verify is read-only by construction.
            Assert.Fail("pending T319");
        }
    }
}
