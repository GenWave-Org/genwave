// STORY-366 — the L9 fence names exactly two carriers (SPEC F145.6 · PLAN T351)
//
// BDD specification — xUnit. WIRED T351. Story360_AnnounceSchemeFence.cs pinned the single carrier
// (AnnouncementsController); T351 moved the now-playing read onto its own non-admin-surface
// controller (AnnouncementNowPlayingController), so the fence widens to exactly those two names —
// and keeps redding on a third. These facts prove BOTH halves against the real, deployed Host
// assembly (never a synthetic fixture for the "real host passes" half) plus a synthetic third
// carrier (a throwaway type in THIS assembly) for the "a third reds" half.
using GenWave.Architecture.Tests.Support;
using GenWave.Host.Auth;

namespace GenWave.Architecture.Tests.Specs;

public sealed class FeatureAnnounceSchemeFenceTwoCarriers
{
    public sealed class ScenarioTheRealHostPassesWithTwoCarriers
    {
        // When AnnounceSchemeFence.FindViolations runs with both designated names
        // (AnnounceSchemeFence.DesignatedCarriers — the ONE copy Story360's own mechanism-sanity
        // facts share this set with, Support/AnnounceSchemeFence.cs).
        [Fact]
        public void OnlyTheTwoNamedControllersNameTheScheme()
        {
            var violations = AnnounceSchemeFence.FindViolations(
                ProductionAssemblies.Host.GetTypes(),
                AnnounceTokenAuthenticationDefaults.SchemeName,
                AnnounceSchemeFence.DesignatedCarriers);

            DependencyLawAssert.AssertNone(violations, ExemptionBaseline.Entries);
        }
    }

    public sealed class ScenarioAThirdCarrierFailsTheLaw
    {
        // Given a synthetic third type carrying the scheme in an [Authorize] attribute.
        [Fact]
        public void TheFenceReportsIt()
        {
            var violations = AnnounceSchemeFence.FindViolations(
                [typeof(Fixtures.L9Probe.ViolatesFence)],
                AnnounceTokenAuthenticationDefaults.SchemeName,
                AnnounceSchemeFence.DesignatedCarriers);

            var violation = Assert.Single(violations);
            Assert.Equal(LawId.L9, violation.LawId);
            Assert.EndsWith("L9Probe.ViolatesFence", violation.Member, StringComparison.Ordinal);
        }
    }
}
