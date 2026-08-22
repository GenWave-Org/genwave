// STORY-357 — An accepted announcement never vanishes (SPEC F143 · PLAN T337)
using Xunit;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAnnouncementStoreLifecycle
{
    public sealed class ScenarioAcceptedRowsAreDurable
    {
        [Fact(Skip = "pending T337 (STORY-357 AC1)")]
        public void AnInsertedAnnouncementIsPendingWithItsExpiryStamped() { }

        [Fact(Skip = "pending T337 (STORY-357 AC1)")]
        public void TheDefaultTtlIsFifteenMinutes() { }

        [Fact(Skip = "pending T337 (STORY-357 AC5)")]
        public void AFreshRepositoryInstanceReadsTheSamePendingRows() { }
    }

    public sealed class ScenarioExpiryIsVisibleNeverSilent
    {
        [Fact(Skip = "pending T337 (STORY-357 AC2)")]
        public void APendingRowPastItsTtlTransitionsToExpired() { }

        [Fact(Skip = "pending T337 (STORY-357 AC2)")]
        public void TheExpiryStampsStateChangedAt() { }

        [Fact(Skip = "pending T337 (STORY-357 AC2)")]
        public void AnExpiredRowStillAppearsInTheHistoryRead() { }
    }

    public sealed class ScenarioIdenticalTextCollapses
    {
        [Fact(Skip = "pending T337 (STORY-357 AC4)")]
        public void ACaseFoldedDuplicateCreatesNoNewRow() { }

        [Fact(Skip = "pending T337 (STORY-357 AC4)")]
        public void TheExistingRowsCollapseCountIncrements() { }

        [Fact(Skip = "pending T337 (STORY-357 AC4)")]
        public void TheExistingRowsTtlIsUntouchedByTheCollapse() { }
    }

    public sealed class ScenarioTransitionsAreTotalAndNothingIsDeleted
    {
        [Fact(Skip = "pending T337 (STORY-357 AC2)")]
        public void ADeclineStampsItsReason() { }

        [Fact(Skip = "pending T337 (STORY-357 AC2)")]
        public void NoLifecycleTransitionEverDeletesARow() { }
    }
}
