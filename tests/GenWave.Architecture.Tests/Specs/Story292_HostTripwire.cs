// STORY-292 — The Host tripwire is armed and seeded (SPEC F105.1, F105.4 · PLAN T214)
namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: law L5 — Host contains no namespace from the graduated/reserved subsystem
/// list, seeded with the F105.4 born-outside reservations (Context, Ads) so gh-#378 and
/// gh-#380 cannot quietly land in Host. Graduated list starts empty; each graduation
/// ruling extends the data. Pending until T214.
/// </summary>
public sealed class FeatureHostTripwire
{
    public sealed class ScenarioTheMechanismAndTheSeed
    {
        [Fact(Skip = "pending — T214 builds this (STORY-292 AC1)")]
        public void TodaysHostPassesWithTheSeededReservations() => Assert.Fail("pending");

        [Fact(Skip = "pending — T214 builds this (STORY-292 AC1)")]
        public void TheReservedListIsDataAOneLineRulingExtends() => Assert.Fail("pending");
    }

    public sealed class ScenarioAReservedNamespaceInHostIsRed
    {
        [Fact(Skip = "pending — T214 builds this (STORY-292 AC2, mutation-checked at review)")]
        public void ATypeUnderAReservedNamespaceFailsL5NamingTheNamespaceAndTheGraduationRule() => Assert.Fail("pending");
    }
}
