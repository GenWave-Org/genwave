// STORY-464 — Phone numbers are spoken as digits (gh-#700 · SPEC F197 · PLAN T545)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePhonenumbersarespokenasdigits
{
    const string Pending = "pending: T545 — Phone numbers are spoken as digits (STORY-464)";

    public sealed class ScenarioASevenDigitNumber
    {
        // Given: "Call 555-0142 today" through SpeechText.FlattenForSpeech

        /// <summary>AC3 — "call five five five, zero one four two today"</summary>
        [Fact(Skip = Pending)]
        public void SpeaksEachDigit() => Assert.Fail(Pending);
    }

    public sealed class ScenarioATenDigitNumber
    {
        // Given: "Call 812-555-0199"

        /// <summary>AC4 — three comma-separated groups</summary>
        [Fact(Skip = Pending)]
        public void SpeaksThreeGroups() => Assert.Fail(Pending);
    }

    public sealed class ScenarioADjPatterRender
    {
        // Given: TtsSegmentSource with a recording synthesizer, copy carrying "555-0142"

        /// <summary>AC5 — the synthesizer receives the spoken form</summary>
        [Fact(Skip = Pending)]
        public void ReachesEveryVoice() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNumbersThatAreNotPhones
    {
        // Given: "Back in 1994 and 2026", "at 7:30 tonight", "over 1000 tracks"

        /// <summary>AC6 — years unchanged</summary>
        [Fact(Skip = Pending)]
        public void LeavesYearsAlone() => Assert.Fail(Pending);

        /// <summary>AC7 — times unchanged</summary>
        [Fact(Skip = Pending)]
        public void LeavesTimesAlone() => Assert.Fail(Pending);

        /// <summary>AC8 — counts unchanged</summary>
        [Fact(Skip = Pending)]
        public void LeavesCountsAlone() => Assert.Fail(Pending);
    }

}
