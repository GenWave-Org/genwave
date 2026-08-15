// STORY-335 — The face on the public surface (SPEC F129.1/.2/.3 · PLAN T298 route + T299 payload)
//
// BDD specification — xUnit. The spectator DJ card itself (AC4) is the static page —
// browser acceptance at the T301 wire per the T92 precedent (no JS test rig by design).

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheFaceOnThePublicSurface
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the route (T298)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAWornFaceServesAnonymouslyAndImmutably
    {
        [Fact(Skip = "Pending T298 — see docs/PLAN.md")]
        public void TheCurrentTokenReturnsTheFaceBytes()
        {
            Assert.Fail("pending T298");
        }

        [Fact(Skip = "Pending T298 — see docs/PLAN.md")]
        public void TheResponseCarriesTheImmutableYearCache()
        {
            // Cache-Control: public, max-age=31536000, immutable — safe because rotation re-URLs.
            Assert.Fail("pending T298");
        }

        [Fact(Skip = "Pending T298 — see docs/PLAN.md")]
        public void TheRouteExistsOnlyOnTheSpectatorSurface()
        {
            // The dedicated public listener serves it; the admin listener's surface set is untouched.
            Assert.Fail("pending T298");
        }
    }

    public sealed class ScenarioRotationRevokes
    {
        [Fact(Skip = "Pending T298 — see docs/PLAN.md")]
        public void TheOldTokenServesTheStationImageBytesWith200()
        {
            Assert.Fail("pending T298");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the payload (T299)
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePayloadNamesTheFace
    {
        [Fact(Skip = "Pending T299 — see docs/PLAN.md")]
        public void DjAvatarUrlCarriesTheOnAirPersonasTokenUrl()
        {
            Assert.Fail("pending T299");
        }

        [Fact(Skip = "Pending T299 — see docs/PLAN.md")]
        public void DjAvatarUrlIsNullWhenTheOnAirPersonaIsFaceless()
        {
            Assert.Fail("pending T299");
        }

        [Fact(Skip = "Pending T299 — see docs/PLAN.md")]
        public void TheDisclosureContractPinsTheCompletePropertySet()
        {
            // F93.5/F67.5 amendment: the suite's complete-set assertion includes djAvatarUrl,
            // so an unblessed field still fails the build.
            Assert.Fail("pending T299");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no oracle
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownTokensAreNotAProbe
    {
        [Fact(Skip = "Pending T298 — see docs/PLAN.md")]
        public void ARandomTokenServesTheStationImageBytesWith200()
        {
            // Indistinguishable from a stale token — the F88.3 idiom.
            Assert.Fail("pending T298");
        }
    }
}
