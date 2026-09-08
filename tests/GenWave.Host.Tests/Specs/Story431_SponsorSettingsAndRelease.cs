// STORY-431 — Settings, options, laws, and the release (SPEC F176.1–F176.4 · PLAN T433)

namespace GenWave.Host.Tests.Specs;

public static class FeatureSponsorSettingsOptionsLawsAndTheRelease
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAntiRepeatWindowHelpTextChanged
    {
        [Fact]
        public void TheHelpTextSaysSponsor()
            => Assert.Fail("pending: T433 Station:Ads:AntiRepeatWindow help — AC1");

        [Fact]
        public void TheF56HelpKeyParitySpecStillPasses()
            => Assert.Fail("pending: T433 help-key parity — AC1");
    }

    public sealed class ScenarioEnvKnobsBind
    {
        [Fact]
        public void PreviewRetentionDaysBindsFromEnv()
            => Assert.Fail("pending: T433 Ads__PreviewRetentionDays — AC2");

        [Fact]
        public void JobQueueCapacityBindsFromEnv()
            => Assert.Fail("pending: T433 Ads__JobQueueCapacity — AC2");
    }

    public sealed class ScenarioDeploymentListsTheTwoKnobsExactlyOnce
    {
        [Fact]
        public void PreviewRetentionDaysAppearsOnce()
            => Assert.Fail("pending: T433 DEPLOYMENT fence-drift — AC7");

        [Fact]
        public void JobQueueCapacityAppearsOnce()
            => Assert.Fail("pending: T433 DEPLOYMENT fence-drift — AC7");
    }

    public sealed class ScenarioAbstractionsUnchanged
    {
        [Fact]
        public void ThePackageSurfaceDiffVs570IsEmpty()
            => Assert.Fail("pending: T433 T453 package diff — AC6");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingBadValues
    {
        [Fact]
        public void PreviewRetentionDaysZeroFailsStartupNamingTheProperty()
            => Assert.Fail("pending: T433 DataAnnotation range — AC3");

        [Fact]
        public void ALiveSettingsPutForAnEnvKnobIs400()
            => Assert.Fail("pending: T433 allowlist refuses Ads:PreviewRetentionDays — AC4");
    }
}
