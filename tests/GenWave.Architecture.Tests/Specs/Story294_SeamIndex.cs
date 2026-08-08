// STORY-294 — SEAMS.md: the generated map with a drift gate (SPEC F105.6 · PLAN T216, T217)
namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the generated seam index. A deterministic generator over the composition
/// root's DI registrations produces the committed root SEAMS.md (port → default adapter →
/// binding site → decorators); CI rebuilds and byte-diffs it (the catalog index.json
/// convention — its red-on-stale half is T217's CI wire, exercised at that task's
/// acceptance rather than as a unit fact). Pending until T216/T217.
/// </summary>
public sealed class FeatureSeamIndex
{
    public sealed class ScenarioDeterministicGeneration
    {
        [Fact(Skip = "pending — T216 builds this (STORY-294 AC1)")]
        public void TwoRunsOverTheSameTreeProduceByteIdenticalOutput() => Assert.Fail("pending");

        [Fact(Skip = "pending — T216 builds this (STORY-294 AC1)")]
        public void EveryGenWaveSeamListsPortDefaultAdapterAndBindingSite() => Assert.Fail("pending");
    }

    public sealed class ScenarioCommittedAndCurrent
    {
        [Fact(Skip = "pending — T216 builds this (STORY-294 AC2)")]
        public void TheCommittedSeamsFileMatchesAFreshGenerationByteForByte() => Assert.Fail("pending");
    }

    public sealed class ScenarioTheCheckFirstLine
    {
        [Fact(Skip = "pending — T217 builds this (STORY-294 AC3)")]
        public void ContributingInstructsCheckingSeamsBeforeAddingASeam() => Assert.Fail("pending");
    }
}
