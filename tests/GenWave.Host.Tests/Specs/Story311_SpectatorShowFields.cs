// STORY-311 — The public face names the show (F116.4, F115.3)
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: the spectator DTO fields land at T251. Disclosure follows the F67.6 idiom —
// complete-property-set assertions so an unblessed field fails the build. The one hard
// law: flavor is prompt config and NEVER appears on a public surface.

namespace GenWave.Host.Tests.Specs;

using Xunit;

public static class FeatureSpectatorShowFields
{
    public sealed class ScenarioTheFieldsRide
    {
        [Fact(Skip = "Pending (T251)")]
        public void NowPlayingCarriesShowNameAndTagline()
        {
            // Given a show on the air
            // When  /spectator/api/now-playing is read via the public listener
            // Then  show { name, tagline } is present on the payload
        }

        [Fact(Skip = "Pending (T251)")]
        public void UpNextCarriesTheShowName()
        {
            // Given a named next segment
            // When  now-playing is read
            // Then  upNext.show carries the name (name only — F116.4)
        }

        [Fact(Skip = "Pending (T251)")]
        public void UnnamedBlocksReadNull()
        {
            // Given no show on the air and an unnamed next segment
            // When  now-playing is read
            // Then  show and upNext.show are null — the page renders exactly as today
        }
    }

    public sealed class ScenarioDisclosureHoldsTheLine
    {
        [Fact(Skip = "Pending (T251)")]
        public void FlavorIsStructurallyAbsentFromPublicPayloads()
        {
            // Given every spectator payload DTO
            // When  the disclosure-contract suite enumerates complete property sets
            // Then  flavor appears nowhere (F115.3 — the persona-soul precedent);
            //       name/tagline are the pinned public additions to the F67 inventory
        }
    }
}
