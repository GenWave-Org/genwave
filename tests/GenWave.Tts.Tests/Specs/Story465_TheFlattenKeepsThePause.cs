// STORY-465 — The flatten keeps the pause (gh-#703 · SPEC F198 · PLAN T546 T547)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTheflattenkeepsthepause
{
    const string Pending = "pending: T546 — The flatten keeps the pause (STORY-465)";

    public sealed class ScenarioCopyWithCommas
    {
        // Given: "Tonight, on GenWave, the hits"

        /// <summary>AC1 — commas survive</summary>
        [Fact(Skip = Pending)]
        public void KeepsTheCommas() => Assert.Fail(Pending);
    }

    public sealed class ScenarioLooseMarks
    {
        // Given: "Coming up: the news" · "Rock — and roll – tonight - late" · "Wait for it… now" · "Well, — well"

        /// <summary>AC2 — colon → comma</summary>
        [Fact(Skip = Pending)]
        public void FoldsAColonToAComma() => Assert.Fail(Pending);

        /// <summary>AC3 — every dash → comma</summary>
        [Fact(Skip = Pending)]
        public void FoldsDashesToCommas() => Assert.Fail(Pending);

        /// <summary>AC4 — ellipsis → comma</summary>
        [Fact(Skip = Pending)]
        public void FoldsAnEllipsisToAComma() => Assert.Fail(Pending);

        /// <summary>AC5 — a run → one comma</summary>
        [Fact(Skip = Pending)]
        public void CollapsesARun() => Assert.Fail(Pending);
    }

    public sealed class ScenarioInWordHyphens
    {
        // Given: "re-render the drive-time show"

        /// <summary>AC6 — untouched</summary>
        [Fact(Skip = Pending)]
        public void KeepsInWordHyphens() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheCacheEpoch
    {
        // Given: TtsSegmentSource.MergePolicyVersion

        /// <summary>AC7 — ends with "+gh703"</summary>
        [Fact(Skip = Pending)]
        public void MovesTheEpoch() => Assert.Fail(Pending);
    }

    public sealed class ScenarioThreeConsecutiveAnnouncerLines
    {
        // Given: a two-voice script through CrosstalkTimeline with a seeded sampler (T547)

        /// <summary>AC8 — same-speaker gaps within [0.20, 0.35] s</summary>
        [Fact(Skip = Pending)]
        public void ClampsSameSpeakerGaps() => Assert.Fail(Pending);

        /// <summary>AC9 — the ANNOUNCER→VOICE gap within [0.20, 0.80] s</summary>
        [Fact(Skip = Pending)]
        public void LeavesCrossSpeakerGapsAlone() => Assert.Fail(Pending);
    }

}
