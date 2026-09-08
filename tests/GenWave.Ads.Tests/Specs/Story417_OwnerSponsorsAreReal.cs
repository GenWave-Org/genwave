// STORY-417 — Owner sponsors are real; pack sponsors stay parody (SPEC F172.5 · PLAN T438)

namespace GenWave.Ads.Tests.Specs;

public static class FeatureOwnerSponsorsAreRealPackSponsorsStayParody
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOwnerSponsorsOwnPhonePasses
    {
        [Fact]
        public void AScriptSpeakingTheSponsorsExactPhonePasses()
            => Assert.Fail("pending: T438 validator skips the sponsor's phone literal — AC1");
    }

    public sealed class ScenarioOwnerSponsorsOwnNamePassesTheBlocklist
    {
        [Fact]
        public void AScriptNamingTheSponsorPasses()
            => Assert.Fail("pending: T438 blocklist skips the sponsor's name literal — AC4");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioStillRefusing
    {
        [Fact]
        public void ADifferentNon555NumberRefusesOnPhoneNotFictional()
            => Assert.Fail("pending: T438 555 rule for other numbers — AC2");

        [Fact]
        public void APackSponsorsNon555NumberRefuses()
            => Assert.Fail("pending: T438 pack posture unchanged — AC3");

        [Fact]
        public void ADifferentBlocklistedBrandRefusesOnBrandBlocklisted()
            => Assert.Fail("pending: T438 blocklist for other names — AC4");

        [Fact]
        public void ANearNameLikeSouthsideBakeryStillRefuses()
            => Assert.Fail("pending: T438 skip is literal, not fuzzy — AC5");
    }
}
