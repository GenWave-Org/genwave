// STORY-462 — Ads take the voice transition (gh-#699 · SPEC F195 · PLAN T541)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdstakethevoicetransition
{
    const string Pending = "pending: T541 — Ads take the voice transition (STORY-462)";

    public sealed class ScenarioAKindStampedAdItem
    {
        // Given: a PlayoutItem with a numeric MediaId, SegmentKind = AdSpot, DurationMs = 28000, through LiquidsoapAnnotationBuilder

        /// <summary>AC1 — gw_tts="true" is present</summary>
        [Fact(Skip = Pending)]
        public void MarksItAsVoice() => Assert.Fail(Pending);

        /// <summary>AC2 — liq_cross_duration derived from DurationMs</summary>
        [Fact(Skip = Pending)]
        public void StampsTheCrossDurationFromTheItem() => Assert.Fail(Pending);
    }

    public sealed class ScenarioATtsPrefixedItem
    {
        // Given: MediaId "tts:abc", no SegmentKind; the pre-change builder output captured as a literal

        /// <summary>AC3 — byte-identical annotation</summary>
        [Fact(Skip = Pending)]
        public void IsAnnotatedExactlyAsBefore() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAMusicItem
    {
        // Given: numeric MediaId, no SegmentKind

        /// <summary>AC4 — no gw_tts key</summary>
        [Fact(Skip = Pending)]
        public void IsNotMarkedAsVoice() => Assert.Fail(Pending);
    }

}
