// STORY-250 — A station that knows its audience: the setting + the end-to-end guarantee
// (gh-#174, SPEC F95.1, F95.6, PLAN T111/T114)
//
// BDD specification — xUnit, pending. Entry-point discipline: the setting facts drive the
// real settings API; the F95.6 pins ride a real playout run — nothing explicit airs on an
// everyone station, therefore nothing explicit reaches ICY, spectator, or the DJ's mouth.
// Companion to Story250_ExplicitPoolExclusion.cs (MediaLibrary.Tests).

namespace GenWave.Host.Tests.Specs;

public static class FeatureAudiencePostureSetting
{
    public sealed class ScenarioTheSettingExists
    {
        // Given a fresh station (F95.1).

        [Fact(Skip = "Pending (T111)")]
        public void DefaultIsEveryone() { }

        [Fact(Skip = "Pending (T111)")]
        public void KeyIsAllowlistedAndLiveApply() { }

        [Fact(Skip = "Pending (T111)")]
        public void OnlyEveryoneOrMatureAreAccepted() { }
    }

    public sealed class ScenarioNothingExplicitReachesAnySurface
    {
        // Given a stamped-explicit track on an everyone station, When a real playout run
        // completes (F95.6) — enforcement upstream of every display surface.

        [Fact(Skip = "Pending (T114)")]
        public void TheTrackNeverEntersAPick() { }

        [Fact(Skip = "Pending (T114)")]
        public void FlippedToMatureLiveTheTrackBecomesEligible() { }
    }
}
