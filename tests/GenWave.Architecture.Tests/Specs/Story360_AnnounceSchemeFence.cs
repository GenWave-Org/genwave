// STORY-360 — The AnnounceToken scheme fence (SPEC F145.3/.4 · PLAN T340 carry-forward, built T343)
// Widened to two designated carriers at PLAN T351 (SPEC F145.6, STORY-366) — this file keeps the
// detector's own mechanism-sanity facts (proving BOTH real controllers' attributes are genuinely
// seen); Story366_AnnounceSchemeFenceTwoCarriers.cs owns the real-host "no violations" fact and the
// "a third reds" fact. Both files share ONE DesignatedCarriers constant
// (AnnounceSchemeFence.DesignatedCarriers, Support/AnnounceSchemeFence.cs) — never two lists that
// could quietly drift apart.
using GenWave.Architecture.Tests.Support;
using GenWave.Host.Auth;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: law L9 — outside <c>GenWave.Host.Api.AnnouncementsController</c> and
/// <c>GenWave.Host.Api.AnnouncementNowPlayingController</c>, no production type names
/// <c>AnnounceTokenAuthenticationDefaults.SchemeName</c> inside an <c>AuthorizeAttribute</c>'s own
/// <c>AuthenticationSchemes</c>. Built at T343 to close the T340 review's own mutation-proven gap: a
/// widened schemes list elsewhere would silently promote the HA announce token to full admin
/// authority with every OTHER test still green — this suite is the only thing that would catch it.
/// </summary>
public sealed class FeatureAnnounceSchemeFence
{
    public sealed class ScenarioTheRealHostPasses
    {
        // The detector's own sanity check (round-2-review shape every other law's suite carries): if
        // this went green merely because the scan never actually SEES a carrier's own real attribute
        // (a typo'd property name, a wrong BindingFlags mask), Story366's real-host fact would pass
        // for the WRONG reason. Running the exact same scan with NO exclusion at all, against ONLY
        // the real production type, proves the mechanism genuinely reaches the real attribute — done
        // for BOTH designated carriers (T351), not only the original one. (The real-host "no
        // violations at all" fact itself lives once, in Story366_AnnounceSchemeFenceTwoCarriers.cs —
        // never duplicated here.)
        [Fact]
        public void TheMechanismGenuinelySeesAnnouncementsControllersOwnAttribute()
        {
            var announcementsController = ProductionAssemblies.Host.GetType("GenWave.Host.Api.AnnouncementsController");
            Assert.NotNull(announcementsController);

            var violations = AnnounceSchemeFence.FindViolations(
                [announcementsController],
                AnnounceTokenAuthenticationDefaults.SchemeName,
                designatedTypeFullName: "some.other.type.entirely");

            var violation = Assert.Single(violations);
            Assert.Equal(LawId.L9, violation.LawId);
            Assert.Equal("GenWave.Host.Api.AnnouncementsController", violation.Member);
        }

        [Fact]
        public void TheMechanismGenuinelySeesAnnouncementNowPlayingControllersOwnAttribute()
        {
            var nowPlayingController = ProductionAssemblies.Host.GetType("GenWave.Host.Api.AnnouncementNowPlayingController");
            Assert.NotNull(nowPlayingController);

            var violations = AnnounceSchemeFence.FindViolations(
                [nowPlayingController],
                AnnounceTokenAuthenticationDefaults.SchemeName,
                designatedTypeFullName: "some.other.type.entirely");

            var violation = Assert.Single(violations);
            Assert.Equal(LawId.L9, violation.LawId);
            Assert.Equal("GenWave.Host.Api.AnnouncementNowPlayingController", violation.Member);
        }
    }

    public sealed class ScenarioAWidenedSchemesListIsRed
    {
        static readonly IReadOnlyList<Type> ProbeSubjects =
        [
            typeof(Fixtures.L9Probe.ViolatesFence),
            typeof(Fixtures.L9Probe.ViolatesFenceAtMethodLevel),
            typeof(Fixtures.L9Probe.DesignatedException),
            typeof(Fixtures.L9Probe.StaysClean),
        ];

        readonly IReadOnlyList<LawViolation> violations = AnnounceSchemeFence.FindViolations(
            ProbeSubjects,
            AnnounceTokenAuthenticationDefaults.SchemeName,
            designatedTypeFullName: typeof(Fixtures.L9Probe.DesignatedException).FullName!);

        [Fact]
        public void AClassLevelWidenedSchemesListFailsL9NamingTheOffendingType()
        {
            var violation = Assert.Single(
                violations, v => v.Member.EndsWith("L9Probe.ViolatesFence", StringComparison.Ordinal));
            Assert.Equal(LawId.L9, violation.LawId);
            Assert.Contains("AnnounceToken", DependencyLawAssert.Format(violation), StringComparison.Ordinal);
        }

        [Fact]
        public void AMethodLevelWidenedSchemesListIsCaughtTooNotOnlyTheClassLevelShape() =>
            Assert.Contains(violations, v => v.Member.EndsWith("L9Probe.ViolatesFenceAtMethodLevel", StringComparison.Ordinal));

        [Fact]
        public void TheDesignatedExceptionIsNeverFlagged() =>
            Assert.DoesNotContain(violations, v => v.Member.EndsWith("L9Probe.DesignatedException", StringComparison.Ordinal));

        [Fact]
        public void APolicyOnlyAuthorizeAndALookalikeSchemeNameStayClean() =>
            Assert.DoesNotContain(violations, v => v.Member.EndsWith("L9Probe.StaysClean", StringComparison.Ordinal));

        [Fact]
        public void ExactlyTheTwoGenuineHazardsAreFlagged() => Assert.Equal(2, violations.Count);
    }
}
