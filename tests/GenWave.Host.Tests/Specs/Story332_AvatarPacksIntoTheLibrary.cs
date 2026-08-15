// STORY-332 — Avatar packs into the library (SPEC F128.3/.4 · PLAN T293)
//
// BDD specification — xUnit. Backend install/uninstall only; the Wardrobe Avatars tab
// and transient shelf previews (AC3's UI half) live in admin-ui jest
// (wardrobe-avatar-packs.spec.tsx) + the T301 wire. Skip-pinned until T293 lands.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAvatarPacksIntoTheLibrary
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioInstallLandsThePack
    {
        [Fact(Skip = "Pending T293 — see docs/PLAN.md")]
        public void EveryItemIsStoredWithItsHashVerifiedBytes()
        {
            // POST /api/avatar-packs/{slug}/install → avatar_pack + one _item per PNG.
            Assert.Fail("pending T293");
        }

        [Fact(Skip = "Pending T293 — see docs/PLAN.md")]
        public void EveryStoredPngWasReValidatedServerSide()
        {
            // The T291 gates ran on each asset (magic/IHDR/size/acTL) — CI is never trusted:
            // a fetched asset failing any gate fails the install.
            Assert.Fail("pending T293");
        }

        [Fact(Skip = "Pending T293 — see docs/PLAN.md")]
        public void SuggestedPersonaHintsAreStoredVerbatim()
        {
            Assert.Fail("pending T293");
        }
    }

    public sealed class ScenarioReinstallUpserts
    {
        [Fact(Skip = "Pending T293 — see docs/PLAN.md")]
        public void ASecondInstallReplacesRowsWithoutDuplicates()
        {
            Assert.Fail("pending T293");
        }
    }

    public sealed class ScenarioUninstallIsGuardFree
    {
        [Fact(Skip = "Pending T293 — see docs/PLAN.md")]
        public void ThePackRowsAreGone()
        {
            Assert.Fail("pending T293");
        }

        [Fact(Skip = "Pending T293 — see docs/PLAN.md")]
        public void AWornCopyOfOneOfItsFacesSurvivesUntouched()
        {
            // The copy model's point: persona_avatar rows never reference the pack.
            Assert.Fail("pending T293");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAHostileAssetNeverLands
    {
        [Fact(Skip = "Pending T293 — see docs/PLAN.md")]
        public void AFailedGateWritesNothingAndAnswersQuietly()
        {
            // One bad PNG in the pack ⇒ zero rows written; ProblemDetails names no internals.
            Assert.Fail("pending T293");
        }
    }
}
