// STORY-567 — The shadow week (SPEC F220.19 · PLAN T790)
//
// The shadow check and the validator build their request through the same call,
// AdScriptRequests.ValidationRequestFor. These facts pin that Build -> ValidationRequestFor carries the sponsor's
// phone, name and not-pack-owned flag through.

using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureTheShadowRulesJudgeWhatTheValidatorJudges
{
    static readonly DateTime Stamp = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);

    // An owner sponsor: a phone on file, not pack-owned.
    public sealed class ScenarioAnOwnerSponsorsAdIsBuilt
    {
        readonly AdScriptValidationRequest request;

        public ScenarioAnOwnerSponsorsAdIsBuilt()
        {
            var sponsor = new Sponsor(
                1, "Cravin's Diner", null, null, null, "(406) 222-0100", null, null, null, false, null, Stamp, Stamp, "v1");
            var (write, _) = AdScriptRequests.Build(
                sponsor, null, null, 30, AudiencePosture.Everyone, 450, 0.4, new Fakes.FakePatterDurationEstimator());
            request = AdScriptRequests.ValidationRequestFor(write);
        }

        [Fact]
        public void TheRulesSeeTheSponsorsPhone() => Assert.Equal("(406) 222-0100", request.SponsorPhone);

        [Fact]
        public void TheRulesSeeTheSponsorAsNotPackOwned() => Assert.False(request.IsPackOwned);

        [Fact]
        public void TheRulesSeeTheSponsorsName() => Assert.Equal("Cravin's Diner", request.SponsorName);
    }
}
