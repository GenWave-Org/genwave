// STORY-291 — The convention laws run red-on-violation (SPEC F105.1 · PLAN T212, T213)
namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the convention laws. L3 (HttpClient construction only at designated client
/// seams — the SSRF-surface enumeration), L4's immutability half (no mutable public state
/// in Abstractions), L6 (Abstractions never references Core). Pending until T212/T213.
/// </summary>
public sealed class FeatureConventionLaws
{
    public sealed class ScenarioL3HttpClientSeams
    {
        [Fact(Skip = "pending — T212 builds this (STORY-291 AC1)")]
        public void EveryHttpClientConstructionSiteIsOnTheDesignatedSeamList() => Assert.Fail("pending");

        [Fact(Skip = "pending — T212 builds this (STORY-291 AC1)")]
        public void TheSeamListIsANamedConstantInTheSuite() => Assert.Fail("pending");
    }

    public sealed class ScenarioL4Immutability
    {
        [Fact(Skip = "pending — T213 builds this (STORY-291 AC2)")]
        public void NoPublicTypeInAbstractionsCarriesMutablePublicState() => Assert.Fail("pending");
    }

    public sealed class ScenarioL6Direction
    {
        [Fact(Skip = "pending — T213 builds this (STORY-291 AC3)")]
        public void AbstractionsReferencesNoCoreType() => Assert.Fail("pending");
    }

    public sealed class ScenarioViolationsAreRedAndNamed
    {
        [Fact(Skip = "pending — T212/T213 build this (STORY-291 AC4, mutation-checked at review)")]
        public void AStrayClientAMutablePropertyOrACoreReferenceFailsExactlyItsLaw() => Assert.Fail("pending");
    }
}
