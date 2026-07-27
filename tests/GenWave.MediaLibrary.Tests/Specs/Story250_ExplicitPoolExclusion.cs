// STORY-250 — A station that knows its audience (gh-#174, SPEC F95.4, PLAN T114)
//
// BDD specification — xUnit, pending. The pool predicate is enforced by construction in
// the catalog candidate query — these facts drive the query seam against a real Postgres
// (Story-catalog idiom). The setting surface + F95.6 end-to-end pins live in
// Story250_AudiencePostureSetting.cs (Host.Tests).

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureExplicitPoolExclusion
{
    public sealed class ScenarioEveryoneExcludesAtThePool
    {
        // Given a track classified explicit and posture everyone (F95.4).

        [Fact(Skip = "Pending (T114)")]
        public void RotationCandidateQueryNeverReturnsIt() { }

        [Fact(Skip = "Pending (T114)")]
        public void RequestMatcherNeverMatchesIt() { }

        [Fact(Skip = "Pending (T114)")]
        public void BoundaryBiasSamplingNeverSeesIt() { }
    }

    public sealed class ScenarioMaturePlaysEverything
    {
        // Given posture mature (F95.4).

        [Fact(Skip = "Pending (T114)")]
        public void TheSameTrackIsEligibleUnmasked() { }
    }

    public sealed class ScenarioUnknownPlays
    {
        // Given explicit = NULL on posture everyone (unknown-is-explicit was declined).

        [Fact(Skip = "Pending (T114)")]
        public void UnclassifiedTracksRemainInThePool() { }
    }
}
