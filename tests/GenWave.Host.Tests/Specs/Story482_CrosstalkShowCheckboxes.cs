// STORY-482 — Crosstalk shows as checkboxes (gh-#778 · SPEC F205.7 amended 2026-09-24 · PLAN T585)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Entry point: GET/PUT /api/settings through WebApplicationFactory with a fake IShowStore.

namespace GenWave.Host.Tests.Specs;

public static class FeatureCrosstalkShowCheckboxes
{
    const string Pending = "pending: T585 — shows catalog + MultiChoice (STORY-482)";

    public sealed class ScenarioThreeShowRows
    {
        // Given: fake IShowStore → morning-drive "Morning Drive", late-late "Late Late", jazz-hour "Jazz Hour";
        //        GET /api/settings

        /// <summary>AC1 — choices are the three (slug, name) pairs</summary>
        [Fact(Skip = Pending)]
        public void ListsTheShows() => Assert.Fail(Pending);

        /// <summary>AC2 — kind "multi-choice"</summary>
        [Fact(Skip = Pending)]
        public void IsAMultiChoice() => Assert.Fail(Pending);
    }

    public sealed class ScenarioASavedShowThatWasDeleted
    {
        // Given: Crosstalk:Shows = ["gone-show"]; fake IShowStore without it; GET /api/settings

        /// <summary>AC3 — choices include ("gone-show", "gone-show (not found)")</summary>
        [Fact(Skip = Pending)]
        public void AppendsTheSavedSlug() => Assert.Fail(Pending);
    }

    public sealed class ScenarioSavingASlugArray
    {
        // Given: PUT Crosstalk:Shows = ["morning-drive"]; the settings row read back

        /// <summary>AC6 — the stored value is the JSON array ["morning-drive"]</summary>
        [Fact(Skip = Pending)]
        public void StoresTheSameShape() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheShowStoreIsDown
    {
        // Given: fake IShowStore.GetAllAsync throws; GET /api/settings

        /// <summary>AC7 — Crosstalk:Shows has choicesFailed</summary>
        [Fact(Skip = Pending)]
        public void FlagsTheFailure() => Assert.Fail(Pending);

        /// <summary>AC7 — every other key still returns (200, full key count)</summary>
        [Fact(Skip = Pending)]
        public void ServesTheRestOfThePage() => Assert.Fail(Pending);
    }
}
