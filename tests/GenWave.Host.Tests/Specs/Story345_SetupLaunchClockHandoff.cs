// STORY-345 — Launch, the clock, the handoff (F132.7–.8)
//
// BDD specification — xUnit. Drives the REAL ./setup.sh via Process; launch.sh and
// the mount probe are scratch-PATH stubs that record their argv and script their
// outputs (the Gh019 idiom) — the wizard's orchestration is under spec here, not
// compose. Specs Skip-pinned until T318 lands.

namespace GenWave.Host.Tests.Specs;

public static class FeatureSetupLaunchClockHandoff
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — wrapped, never re-implemented (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheWizardWrapsLaunch
    {
        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void TheWizardInvokesLaunchShWithTheStagedShape()
        {
            // The launch.sh stub records argv: staged mode + the chosen preset flags.
            Assert.Fail("pending T318");
        }

        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void NoComposeInvocationOriginatesFromSetupSh()
        {
            // The docker stub proves compose is only ever called by launch.sh, not the wizard.
            Assert.Fail("pending T318");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the clock instrument (AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheClockInstrument
    {
        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void FirstAudioPrintsOnAirInMinutesSeconds()
        {
            // The mount probe stub serves audio bytes on the Nth poll → "🎙️ On air in M:SS".
            Assert.Fail("pending T318");
        }

        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void TheTimingLineIsAppendedToTheSetupLog()
        {
            Assert.Fail("pending T318");
        }

        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void TheClockStartsAtTheFirstPromptNotAtLaunch()
        {
            Assert.Fail("pending T318");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the handoff screen (AC3)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheHandoffScreen
    {
        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void TheHandoffShowsTheAdminUrl()
        {
            Assert.Fail("pending T318");
        }

        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void TheGeneratedAdminPasswordAppearsExactlyOnce()
        {
            Assert.Fail("pending T318");
        }

        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void ThePersonaShelfDeepLinkAppears()
        {
            Assert.Fail("pending T318");
        }

        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void WhatIsStillArrivingIsNamed()
        {
            // Heavyweight pulls / model downloads / enrichment — the honest background list.
            Assert.Fail("pending T318");
        }

        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void TheExactNextRunCommandsAreShown()
        {
            Assert.Fail("pending T318");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the mount never serves
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheMountNeverServes
    {
        [Fact(Skip = "Pending T318 — see docs/PLAN.md")]
        public void APollTimeoutReportsHonestlyInsteadOfClaimingAir()
        {
            // No "On air" line; the wizard points at diagnostics and exits nonzero.
            Assert.Fail("pending T318");
        }
    }
}
