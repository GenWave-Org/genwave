// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureEveryAttemptIsRecordedAndReplayed
{
    // AC3 — a stored row replayed with unchanged rules
    public sealed class ScenarioAStoredRowIsReplayed
    {
        [Fact(Skip = "pending: T791")]
        public void NoVerdictChanges() { }
    }

    // AC4 — POST /api/talk-rules/replay {days: 7} through the running host
    public sealed class ScenarioTheReplayIsRequested
    {
        [Fact(Skip = "pending: T791")]
        public void EachRuleHasAFlagCount() { }

        [Fact(Skip = "pending: T791")]
        public void EachRuleListsItsChangedAttempts() { }

        [Fact(Skip = "pending: T791")]
        public void NoLlmCallIsMade() { }

        [Fact(Skip = "pending: T791")]
        public void NoRowIsWritten() { }
    }

    // Sad path
    // AC7 — the record queue is full
    public sealed class ScenarioTheRecordQueueIsFull
    {
        [Fact(Skip = "pending: T779")]
        public void TheRowIsDropped() { }

        [Fact(Skip = "pending: T779")]
        public void TheTalkStillAirs() { }
    }

    // Sad path
    // AC8 — no admin cookie
    public sealed class ScenarioNoAdminCookie
    {
        [Fact(Skip = "pending: T791")]
        public void TheReplayReturns401() { }
    }

    // Sad path
    // AC9 — retention out of range
    public sealed class ScenarioRetentionOutOfRange
    {
        [Fact(Skip = "pending: T779")]
        public void ZeroDaysIsRejectedWith400() { }

        [Fact(Skip = "pending: T779")]
        public void NinetyOneDaysIsRejectedWith400() { }
    }
}
