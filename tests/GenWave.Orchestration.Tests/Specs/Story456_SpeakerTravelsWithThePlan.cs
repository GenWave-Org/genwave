// STORY-456 — The speaker travels with the plan (gh-#772 · SPEC F189.1, F189.4, F189.5, F189.7 · PLAN T524, T527)
//
// BDD specification — xUnit. AC1 pins the additive contract; AC6–AC9 drive the planner and the ceremony arm with a counting snapshot source
// and a source that flips the active persona at render time. The render branch is Tts.Tests
// (Story456_SnapshotDrivesTheRender); card-by-id is Host.Tests (Story456_PersonaCardById); the seam index is
// Architecture.Tests (Story456_SeamIndex). AC13 is the dev-station wire (T529).
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureSpeakerTravelsWithThePlan
{
    const string Pending = "pending: T527 — the planner stamps speakers; HandoffContext.Speaker captured at arm (STORY-456)";
    const string Manual = "manual: dev-station wire, T529 — booth_log rows + stereo capture (STORY-456)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioASegmentRequestBuiltTheOldWay
    {
        // Given: SegmentRequest with today's arguments

        /// <summary>AC1 — Speaker is null</summary>
        [Fact(Skip = Pending)]
        public void HasNoSpeaker() => Assert.Fail(Pending);
    }

    public sealed class ScenarioASignOnArmedForBWhileAIsActive
    {
        // Given: the active persona flips to A at render time

        /// <summary>AC6 — the request's Speaker is B</summary>
        [Fact(Skip = Pending)]
        public void RendersWithBsSnapshot() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAContextSegmentPlannedUnderA
    {
        // Given: the active persona flips to B before the render

        /// <summary>AC7 — the request's Speaker is A</summary>
        [Fact(Skip = Pending)]
        public void RendersWithAsSnapshot() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnArmedCeremonyReArmed
    {
        // Given: re-arm with heldNotBefore

        /// <summary>AC8 — HandoffContext.Speaker survives</summary>
        [Fact(Skip = Pending)]
        public void KeepsTheOriginalSpeaker() => Assert.Fail(Pending);
    }

    public sealed class ScenarioACountingSnapshotSource
    {
        // Given: a break naming the station voice and two personas

        /// <summary>AC9 — </summary>
        [Fact(Skip = Pending)]
        public void ResolvesTheStationOnce() => Assert.Fail(Pending);

        /// <summary>AC9 — </summary>
        [Fact(Skip = Pending)]
        public void ResolvesEachPersonaOnce() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheDevStationWire
    {
        // Given: two personas of different pace, flip mid-break (manual, T529)

        /// <summary>AC13 — </summary>
        [Fact(Skip = Manual)]
        public void TheBoothLogNamesThePlannedPersona() => Assert.Fail(Manual);

        /// <summary>AC13 — </summary>
        [Fact(Skip = Manual)]
        public void TheAudioPaceMatchesThePlan() => Assert.Fail(Manual);
    }
}
