// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkCheckRecords
{
    // AC1 — one attempt recorded (Integration: real Postgres)
    public sealed class ScenarioAnAttemptIsRecorded
    {
        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsTheKind() { }

        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsTheDj() { }

        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsTheAttempt() { }

        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsTheRawReply() { }

        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsTheCleanedText() { }

        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsTheSnapshot() { }

        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsEveryRulesVerdict() { }

        [Fact(Skip = "pending: T779")]
        public void TheRowHoldsTheOutcome() { }
    }

    // AC2 — a rule switched off
    public sealed class ScenarioARuleIsSwitchedOff
    {
        [Fact(Skip = "pending: T779")]
        public void ItsVerdictIsStoredMarkedOff() { }
    }

    // AC6 — retention 28 days, a row 29 days old, one insert
    public sealed class ScenarioAnOldRowAndAnInsert
    {
        [Fact(Skip = "pending: T779")]
        public void TheOldRowIsGone() { }

        [Fact(Skip = "pending: T779")]
        public void ARowTwentySevenDaysOldIsKept() { }
    }
}
