// STORY-561 — Pace still works (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePaceStillWorks
{
    // AC1, AC2 — pace 1.1 on chatterbox (ffmpeg on PATH)
    public sealed class ScenarioStretched
    {
        [Fact(Skip = "pending: T769")]
        public void TheLengthIsAboutOneOverPace() { }

        [Fact(Skip = "pending: T769")]
        public void ThePitchMatchesTheUnstretchedRender() { }

        [Fact(Skip = "pending: T769")]
        public void TheStretchRunsBeforeTheLoudnessMeasure() { }
    }

    // Sad path
    // AC3–AC5
    public sealed class ScenarioNoStretch
    {
        [Fact(Skip = "pending: T769")]
        public void PaceOneSkipsTheStep() { }

        [Fact(Skip = "pending: T769")]
        public void PaceTwoIsClampedTo125() { }

        [Fact(Skip = "pending: T769")]
        public void PaceHalfIsClampedTo08() { }

        [Fact(Skip = "pending: T769")]
        public void KokoroNeverStretches() { }
    }
}
