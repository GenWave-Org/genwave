// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Core.Tests.Specs;

public static class FeatureSnapshotRoundTrip
{
    // AC5 — a snapshot stored as JSON and read back
    public sealed class ScenarioASnapshotIsStoredAndReadBack
    {
        [Fact(Skip = "pending: T775")]
        public void ADjLineSnapshotEqualsTheOriginal() { }

        [Fact(Skip = "pending: T775")]
        public void AChatSnapshotEqualsTheOriginal() { }

        [Fact(Skip = "pending: T775")]
        public void AnAdSnapshotEqualsTheOriginal() { }

        [Fact(Skip = "pending: T775")]
        public void MemosSurviveTheRoundTrip() { }
    }
}
