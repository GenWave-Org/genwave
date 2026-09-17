// STORY-456 — The speaker travels with the plan — a card by id (gh-#772 · SPEC F189.2 · PLAN T525)
//
// BDD specification — xUnit. AC10 drives the Host IPersonaCardByIdSource implementation against the Postgres fixture: a persona row whose
// card carries one pronunciation rule.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Host.Tests.Specs;

public static class FeaturePersonaCardById
{
    const string Pending = "pending: T525 — the Host card-by-id store reads a persona card by id (STORY-456)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAPersonaRowWithACard
    {
        // Given: the card carries one pronunciation rule

        /// <summary>AC10 — </summary>
        [Fact(Skip = Pending)]
        public void ResolvesTheCardById() => Assert.Fail(Pending);

        /// <summary>AC10 — </summary>
        [Fact(Skip = Pending)]
        public void TheSnapshotCarriesTheRule() => Assert.Fail(Pending);
    }
}
