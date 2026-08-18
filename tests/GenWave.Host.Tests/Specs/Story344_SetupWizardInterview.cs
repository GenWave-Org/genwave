// STORY-344 — The wizard interview (F132.1–.6)
//
// BDD specification — xUnit. Drives the REAL ./setup.sh via Process with scripted
// stdin answers — the Gh019 idiom (scratch-PATH stubs for docker/dotnet/free/nproc,
// GW_ENV_FILE seam, scratch checkout dir), no daemon, safe anywhere.
// Specs Skip-pinned until T317 lands.

namespace GenWave.Host.Tests.Specs;

public static class FeatureSetupWizardInterview
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the four questions, and only those (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheInterviewAsksExactlyFourQuestions
    {
        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void AVirginRunAsksImagesMusicTopologyProfilesInOrder()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void BuildYourOwnIsOfferedOnlyWhenADotnet10SdkIsDetected()
        {
            // dotnet stub absent → the images question shows pinned-only copy.
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void TheTopologyRecommendationFollowsDetectedRamAndArch()
        {
            // free/uname stubs report a 4 GiB arm64 box → piper-only recommended;
            // the owner's override answer is honored.
            Assert.Fail("pending T317");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — secrets generated, placeholders extinct (AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSecretsAreGenerated
    {
        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void AllSixInternalSecretsAreGeneratedAtLeast32Chars()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void AdminPasswordIsGeneratedForTheOnceOnlyDisplay()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void NoChangeMePlaceholderSurvivesInTheWrittenEnv()
        {
            Assert.Fail("pending T317");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — derive, don't record (AC3) + atomicity (AC4)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDeriveDontRecord
    {
        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void ARerunOverACompleteEnvVerifiesAndSkipsTheInterview()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void NoWizardStateFileExistsAfterAnyRun()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void EnvIsWrittenAtomicallyViaTempAndMove()
        {
            // Kill the wizard between answer-collection and write: the target .env is
            // either absent or complete — never partial.
            Assert.Fail("pending T317");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the no-music lane (AC5)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheNoMusicLane
    {
        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void ZeroAudioFilesPrintsTheCuratedCcSourceList()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void TheLanePrintsTheLicensingResponsibilityNote()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void TheRecheckLoopProceedsOnceAudioFilesAppear()
        {
            Assert.Fail("pending T317");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — existing boxes route to adoption (AC6)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioExistingBoxesRouteToAdoption
    {
        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void AnExistingEnvRoutesToVerifyRepairInsteadOfTheInterview()
        {
            Assert.Fail("pending T317");
        }

        [Fact(Skip = "Pending T317 — see docs/PLAN.md")]
        public void TheInterviewNeverOverwritesAnExistingEnv()
        {
            Assert.Fail("pending T317");
        }
    }
}
