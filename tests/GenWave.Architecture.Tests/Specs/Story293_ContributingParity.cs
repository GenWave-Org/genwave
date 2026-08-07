// STORY-293 — The laws are front-and-center for contributors (SPEC F105.3, F105.5 · PLAN T215)
namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the shipped-doc half. CONTRIBUTING.md carries the six-law table + the seam
/// criterion before any workflow detail, and a parity test ties doc to suite so neither
/// drifts. Pending until T215 (which waits for all law ids to settle).
/// </summary>
public sealed class FeatureContributingParity
{
    public sealed class ScenarioFrontAndCenter
    {
        [Fact(Skip = "pending — T215 builds this (STORY-293 AC1)")]
        public void TheLawsTableAppearsBeforeAnyWorkflowDetail() => Assert.Fail("pending");

        [Fact(Skip = "pending — T215 builds this (STORY-293 AC1)")]
        public void TheSeamCriterionAppearsWithTheTable() => Assert.Fail("pending");
    }

    public sealed class ScenarioSuiteDocParity
    {
        [Fact(Skip = "pending — T215 builds this (STORY-293 AC2)")]
        public void EveryLawIdInTheSuiteAppearsInContributingAndViceVersa() => Assert.Fail("pending");
    }

    public sealed class ScenarioDriftIsRed
    {
        [Fact(Skip = "pending — T215 builds this (STORY-293 AC3, mutation-checked at review)")]
        public void ALawRowRemovedFromTheTableFailsParityNamingTheMissingId() => Assert.Fail("pending");
    }
}
