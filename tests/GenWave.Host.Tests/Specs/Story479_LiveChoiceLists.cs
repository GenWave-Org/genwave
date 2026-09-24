// STORY-479 — Live model and voice lists (gh-#778 · SPEC F205.7 amended 2026-09-24 · PLAN T579, T580)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending…)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Cache-level scenarios (T579) drive ProbedChoiceCache with a fake IChoiceProbe + FakeTimeProvider; entry-point
// scenarios (T580) drive GET/PUT /api/settings through WebApplicationFactory with a fake HttpMessageHandler.

namespace GenWave.Host.Tests.Specs;

public static class FeatureLiveModelAndVoiceLists
{
    const string PendingCache = "pending: T579 — probe cache (STORY-479)";
    const string PendingApi = "pending: T580 — probes + resolver + DTO (STORY-479)";

    public sealed class ScenarioAnLlmEndpointWithTwoModels
    {
        // Given: fake /v1/models → [llama3, phi4]; GET /api/settings

        /// <summary>AC1 — choices is [llama3, phi4]</summary>
        [Fact(Skip = PendingApi)]
        public void ListsTheModels() => Assert.Fail(PendingApi);

        /// <summary>AC3 — Llm:Model is kind "choice"</summary>
        [Fact(Skip = PendingApi)]
        public void ModelIsAChoice() => Assert.Fail(PendingApi);
    }

    public sealed class ScenarioATtsServerWithTwoVoices
    {
        // Given: fake ITtsVoiceLister → [af_bella, am_adam]; GET /api/settings

        /// <summary>AC2 — choices is [af_bella, am_adam]</summary>
        [Fact(Skip = PendingApi)]
        public void ListsTheVoices() => Assert.Fail(PendingApi);

        /// <summary>AC3 — Station:Voice is kind "choice"</summary>
        [Fact(Skip = PendingApi)]
        public void VoiceIsAChoice() => Assert.Fail(PendingApi);
    }

    public sealed class ScenarioTwoResolvesWithinAMinute
    {
        // Given: a succeeding fake probe, resolved twice with the fake clock advanced 30 s

        /// <summary>AC4 — the probe was called once</summary>
        [Fact(Skip = PendingCache)]
        public void CallsTheProbeOnce() => Assert.Fail(PendingCache);
    }

    public sealed class ScenarioAProbeThatDiesAfterOneSuccess
    {
        // Given: success [llama3, phi4], clock +61 s, failure, same ScopeKey

        /// <summary>AC5 — the last list</summary>
        [Fact(Skip = PendingCache)]
        public void ServesTheLastKnownList() => Assert.Fail(PendingCache);

        /// <summary>AC6 — choicesStale = true</summary>
        [Fact(Skip = PendingCache)]
        public void FlagsItStale() => Assert.Fail(PendingCache);
    }

    public sealed class ScenarioASavedModelMissingFromTheList
    {
        // Given: Llm:Model = "mistral"; fake /v1/models → [llama3, phi4]; GET /api/settings

        /// <summary>AC7 — the last choice is ("mistral", "mistral (not found)")</summary>
        [Fact(Skip = PendingApi)]
        public void AppendsTheSavedValue() => Assert.Fail(PendingApi);
    }

    public sealed class ScenarioSavingAModelNotInTheList
    {
        // Given: fake /v1/models → [llama3, phi4]; PUT Llm:Model = "mistral"

        /// <summary>AC8 — 200</summary>
        [Fact(Skip = PendingApi)]
        public void AcceptsTheSave() => Assert.Fail(PendingApi);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAProbeThatNeverSucceeded
    {
        // Given: a failing fake probe, no prior success

        /// <summary>AC11 — choices []</summary>
        [Fact(Skip = PendingCache)]
        public void ServesNoChoices() => Assert.Fail(PendingCache);

        /// <summary>AC12 — choicesFailed = true</summary>
        [Fact(Skip = PendingCache)]
        public void FlagsTheFailure() => Assert.Fail(PendingCache);
    }

    public sealed class ScenarioAFailureResolvedTwiceWithinAMinute
    {
        // Given: a failing fake probe, resolved twice with the clock advanced 30 s

        /// <summary>AC13 — the probe was called once</summary>
        [Fact(Skip = PendingCache)]
        public void CachesTheFailure() => Assert.Fail(PendingCache);
    }

    public sealed class ScenarioAHungProbe
    {
        // Given: a fake probe that awaits its token forever; the fake clock advanced 2 s after the call starts

        /// <summary>AC14 — choicesFailed after the 2 s timeout, no wall-clock wait</summary>
        [Fact(Skip = PendingCache)]
        public void TimesOutAtTwoSeconds() => Assert.Fail(PendingCache);
    }

    public sealed class ScenarioTheEndpointChangedToAFailingOne
    {
        // Given: success on ScopeKey "A", clock +61 s, ScopeKey now "B" and failing

        /// <summary>AC15 — choicesFailed = true</summary>
        [Fact(Skip = PendingCache)]
        public void FlagsTheFailure() => Assert.Fail(PendingCache);

        /// <summary>AC15 — choicesStale = false (A's list is not served)</summary>
        [Fact(Skip = PendingCache)]
        public void DoesNotServeTheOldList() => Assert.Fail(PendingCache);
    }

    public sealed class ScenarioLlmDisabled
    {
        // Given: Llm:Endpoint = ""; a counting fake HttpMessageHandler; GET /api/settings

        /// <summary>AC16 — choicesFailed = true</summary>
        [Fact(Skip = PendingApi)]
        public void FlagsTheFailure() => Assert.Fail(PendingApi);

        /// <summary>AC16 — no HTTP call was made</summary>
        [Fact(Skip = PendingApi)]
        public void MakesNoCall() => Assert.Fail(PendingApi);
    }

    public sealed class ScenarioAMisnamedChoiceSource
    {
        // Given: an allowlist override carrying Probe("nope"); the host composed

        /// <summary>AC20 — startup throws naming "nope"</summary>
        [Fact(Skip = PendingApi)]
        public void FailsBoot() => Assert.Fail(PendingApi);
    }
}
