// STORY-366 — the L9 fence names exactly two carriers (SPEC F145.6 · PLAN T351)
//
// BDD specification — xUnit. PENDING until T351. Story360_AnnounceSchemeFence.cs pins today's
// single carrier (AnnouncementsController); T351 moves the now-playing read onto its own
// non-admin-surface controller, so the fence must widen to exactly that second name — and
// keep redding on a third. These facts replace Story360's single-carrier expectation when
// T351 unskips them (the Story360 file's own facts get the second designated name then).
namespace GenWave.Architecture.Tests.Specs;

public sealed class FeatureAnnounceSchemeFenceTwoCarriers
{
    public sealed class ScenarioTheRealHostPassesWithTwoCarriers
    {
        // When AnnounceSchemeFence.FindViolations runs with both designated names.
        [Fact(Skip = "pending T351 (STORY-366 AC5)")]
        public void OnlyTheTwoNamedControllersNameTheScheme() => Assert.Fail("pending T351");
    }

    public sealed class ScenarioAThirdCarrierFailsTheLaw
    {
        // Given a synthetic third type carrying the scheme in an [Authorize] attribute.
        [Fact(Skip = "pending T351 (STORY-366 AC5)")]
        public void TheFenceReportsIt() => Assert.Fail("pending T351");
    }
}
