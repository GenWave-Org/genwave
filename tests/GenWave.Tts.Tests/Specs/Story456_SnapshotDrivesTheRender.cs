// STORY-456 — The speaker travels with the plan — the render branch (gh-#772 · SPEC F189.3, F189.6 · PLAN T525, T526)
//
// BDD specification — xUnit. AC2–AC5 drive TtsSegmentSource with a recording synthesizer and ambient caches that throw on refresh; AC12 drives
// the Tts SpeakerSnapshotSource with an unknown id.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSnapshotDrivesTheRender
{
    const string Pending = "pending: T526 — TtsSegmentSource.RenderAsync branches on SegmentRequest.Speaker (STORY-456)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioARequestWithASnapshot
    {
        // Given: Speaker pace 1.2 and one pronunciation rule

        /// <summary>AC2 — the synthesizer received 1.2</summary>
        [Fact(Skip = Pending)]
        public void SendsTheSnapshotPace() => Assert.Fail(Pending);

        /// <summary>AC2 — the rule rewrote the copy</summary>
        [Fact(Skip = Pending)]
        public void AppliesTheSnapshotRule() => Assert.Fail(Pending);
    }

    public sealed class ScenarioARequestWithASnapshotAndThrowingCaches
    {
        // Given: ambient caches throw on refresh

        /// <summary>AC3 — the snapshot bypasses the caches</summary>
        [Fact(Skip = Pending)]
        public void RendersSuccessfully() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTwoSnapshotsSameText
    {
        // Given: identical text, ContentHash differs

        /// <summary>AC4 — the cache key carries the snapshot</summary>
        [Fact(Skip = Pending)]
        public void SynthesizesTwice() => Assert.Fail(Pending);
    }

    public sealed class ScenarioARequestWithoutASnapshot
    {
        // Given: Speaker null, ambient caches at pace 0.9

        /// <summary>AC5 — today's path unchanged</summary>
        [Fact(Skip = Pending)]
        public void SendsTheAmbientPace() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnknownPersonaId
    {
        // Given: the snapshot source resolves an id that does not exist

        /// <summary>AC12 — </summary>
        [Fact(Skip = Pending)]
        public void FallsBackToTheStationSnapshot() => Assert.Fail(Pending);

        /// <summary>AC12 — </summary>
        [Fact(Skip = Pending)]
        public void LogsOneWarnNamingTheId() => Assert.Fail(Pending);
    }
}
