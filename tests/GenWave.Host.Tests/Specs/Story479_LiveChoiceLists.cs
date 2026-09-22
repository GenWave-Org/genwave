// STORY-479 — Live choice lists (gh-#778 · SPEC F205.7 · PLAN T579)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Host.Tests.Specs;

public static class FeatureLivechoicelists
{
    const string Pending = "pending: T579 — Live choice lists (STORY-479)";

    public sealed class ScenarioTwoInstalledVoicePacks
    {
        // Given: GET /api/settings, Tts:Voice, catalog-backed

        /// <summary>AC1 — both packs' voices listed</summary>
        [Fact(Skip = Pending)]
        public void ListsInstalledVoices() => Assert.Fail(Pending);
    }

    public sealed class ScenarioThreeShowRows
    {
        // Given: the schedule show key

        /// <summary>AC2 — three choices</summary>
        [Fact(Skip = Pending)]
        public void ListsTheShows() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnOllamaWithTwoModels
    {
        // Given: fake tags endpoint [llama3, phi4], Llm:Model

        /// <summary>AC3 — [llama3, phi4]</summary>
        [Fact(Skip = Pending)]
        public void ProbesOllama() => Assert.Fail(Pending);

        /// <summary>AC4 — called once within 60 s</summary>
        [Fact(Skip = Pending)]
        public void CachesForAMinute() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnOllamaThatDiesAfterOneProbe
    {
        // Given: success, then failure after 60 s

        /// <summary>AC5 — the last list</summary>
        [Fact(Skip = Pending)]
        public void ServesTheLastKnownList() => Assert.Fail(Pending);

        /// <summary>AC5 — choicesStale = true</summary>
        [Fact(Skip = Pending)]
        public void FlagsItStale() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnOllamaThatNeverAnswered
    {
        // Given: failing, no prior success

        /// <summary>AC7 — choices []</summary>
        [Fact(Skip = Pending)]
        public void ServesNoChoices() => Assert.Fail(Pending);

        /// <summary>AC7 — probeFailed = true</summary>
        [Fact(Skip = Pending)]
        public void FlagsTheFailure() => Assert.Fail(Pending);
    }

}
