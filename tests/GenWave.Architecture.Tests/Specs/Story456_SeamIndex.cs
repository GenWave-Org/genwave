// STORY-456 — The speaker travels with the plan — the seam index (gh-#772 · SPEC F189.7 · PLAN T528)
//
// BDD specification — xUnit. AC11 runs tools/SeamIndexGenerator in check mode and reads the committed SEAMS.md.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureSeamIndexListsTheSpeakerSource
{
    const string Pending = "pending: T528 — SEAMS.md regenerated with ISpeakerSnapshotSource and IPersonaCardByIdSource (STORY-456)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCommittedSeamIndex
    {
        // Given: SEAMS.md at the repo root

        /// <summary>AC11 — </summary>
        [Fact(Skip = Pending)]
        public void ListsISpeakerSnapshotSource() => Assert.Fail(Pending);

        /// <summary>AC11 — </summary>
        [Fact(Skip = Pending)]
        public void ListsIPersonaCardByIdSource() => Assert.Fail(Pending);

        /// <summary>AC11 — </summary>
        [Fact(Skip = Pending)]
        public void IsByteIdenticalToTheGenerator() => Assert.Fail(Pending);
    }
}
