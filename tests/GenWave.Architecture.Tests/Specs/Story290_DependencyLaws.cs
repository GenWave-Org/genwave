// STORY-290 — The dependency laws run red-on-violation (SPEC F105.1, F105.2 · PLAN T211)
namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the dependency-direction laws as fitness tests. L1 (framework-free inner
/// projects), L2 (Postgres confinement), and L4's reference half (Abstractions = BCL-only)
/// run inside the normal <c>dotnet test</c> gate. Pending until T211 adopts the analysis
/// library and builds the named+dated exemption mechanism every later law reuses.
/// </summary>
public sealed class FeatureDependencyLaws
{
    public sealed class ScenarioTheSuiteRunsInTheNormalGate
    {
        [Fact(Skip = "pending — T211 builds this (STORY-290 AC1)")]
        public void TheArchitectureSuiteExecutesAlongsideTheFiveTestProjects() => Assert.Fail("pending");
    }

    public sealed class ScenarioL1FrameworkFreeInnerProjects
    {
        [Fact(Skip = "pending — T211 builds this (STORY-290 AC2)")]
        public void CoreOrchestrationTtsAndLoudnessReferenceNoAspNetNpgsqlOrDapper() => Assert.Fail("pending");
    }

    public sealed class ScenarioL2PostgresConfinement
    {
        [Fact(Skip = "pending — T211 builds this (STORY-290 AC3)")]
        public void NpgsqlAndDapperAppearOnlyInTheRepositoryLayer() => Assert.Fail("pending");

        [Fact(Skip = "pending — T211 builds this (STORY-290 AC3)")]
        public void TheCompositionRootsDataSourceConstructionIsTheOneNamedExemption() => Assert.Fail("pending");
    }

    public sealed class ScenarioL4ReferenceHygiene
    {
        [Fact(Skip = "pending — T211 builds this (STORY-290 AC4)")]
        public void AbstractionsReferencesNothingBeyondTheBcl() => Assert.Fail("pending");
    }

    public sealed class ScenarioViolationsAreRedAndNamed
    {
        [Fact(Skip = "pending — T211 builds this (STORY-290 AC5, mutation-checked at review)")]
        public void ADeliberateViolationFailsExactlyItsLawNamingTypeAndLawId() => Assert.Fail("pending");
    }

    public sealed class ScenarioExemptionsAreNamedDatedAndFailOnNew
    {
        [Fact(Skip = "pending — T211 builds this (STORY-290 AC6)")]
        public void ABaselinedViolationIsNamedAndDatedInTheTestItself() => Assert.Fail("pending");

        [Fact(Skip = "pending — T211 builds this (STORY-290 AC6)")]
        public void ANewViolationFailsDespiteTheBaseline() => Assert.Fail("pending");
    }
}
