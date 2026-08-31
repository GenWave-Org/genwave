// STORY-383 — Duplicate clusters never split (SPEC F153.9 rider 2026-08-31 · PLAN T385 · gh-#657)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (the Story376 posture: these facts
// drive the store's SQL against a live database, never a mock — the T362 loop law says every new
// SQL read gets a Postgres-backed fact). Specs are Skip-pinned until T385 wires the group-paged
// read; /build-loop fills the bodies and removes the Skip.
//
// Under spec: the kind-scoped joined read on IRotFindingStore/RotFindingRepository returns
// (rows, total) where, for kind=near_duplicate, limit/offset count DISTINCT group_keys (ordered
// asc — stable across pages) and the row set carries every member of each selected group; for
// every other kind limit/offset/total count rows exactly as T377 built them. The kind-LESS read
// keeps its current shape verbatim (regression pin). STORY-382 AC6 (total is exact) is pinned
// here at the store; its wire half lives in Host.Tests Story382_KindScopedPagingOnTheWire.cs.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureDuplicateClustersNeverSplit
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    /// <summary>STORY-383 AC1 — 30 open near_duplicate groups of 2–4 members each; the store is
    /// asked for the first page of 25 groups.</summary>
    public sealed class ScenarioLimitCountsGroups
    {
        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void ReturnsExactlyTwentyFiveDistinctGroupKeys()
        {
            // var (rows, _) = await store.ListWithMediaAsync(RotKind.NearDuplicate, RotState.Open, limit: 25, offset: 0, ct);
            // Assert.Equal(25, rows.Select(r => r.Finding.GroupKey).Distinct().Count());
            Assert.Fail("pending T385");
        }

        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void EveryReturnedGroupIsWhole()
        {
            // For each returned group_key: the page's member count for it == the table's open member count for it.
            Assert.Fail("pending T385");
        }
    }

    /// <summary>STORY-383 AC2 — the same 30 groups, second page (offset 25 groups).</summary>
    public sealed class ScenarioOffsetCountsGroups
    {
        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void ReturnsTheRemainingFiveWholeGroups()
        {
            Assert.Fail("pending T385");
        }

        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void SharesNoGroupKeyWithPageOne()
        {
            // group_key asc ordering is stable: page1 keys ∩ page2 keys == ∅.
            Assert.Fail("pending T385");
        }
    }

    /// <summary>STORY-383 AC3 + STORY-382 AC6 — the total that rides beside the rows.</summary>
    public sealed class ScenarioTotalCountsGroups
    {
        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void TotalIsThirtyOnEveryPage()
        {
            // Same total from the offset:0 and offset:25 calls.
            Assert.Fail("pending T385");
        }

        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void TotalCountsOnlyOpenGroups()
        {
            // Dismissing every member of one group drops total to 29 on the next read.
            Assert.Fail("pending T385");
        }
    }

    /// <summary>STORY-383 AC5 + STORY-382 AC6 — a flat kind (dead_file) under the same read.</summary>
    public sealed class ScenarioFlatKindsCountRows
    {
        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void LimitAndOffsetCountRowsExactlyAsBefore()
        {
            // 60 open dead_file rows, limit 25 offset 25 → rows 26–50 in opened_at desc, id order.
            Assert.Fail("pending T385");
        }

        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void TotalIsTheExactMatchingRowCount()
        {
            Assert.Fail("pending T385");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH / regression pins
    // ---------------------------------------------------------------------

    /// <summary>STORY-382 AC8's store half — the kind-LESS read is byte-compatible with T377.</summary>
    public sealed class ScenarioTheKindlessReadIsUnchanged
    {
        [Fact(Skip = "Pending T385 — see docs/PLAN.md")]
        public void PagesFlatAcrossKindsInTheT377Order()
        {
            // No kind filter → kind, group_key nulls last, opened_at desc, id — a near_duplicate
            // group MAY split at the page boundary here; that is the pinned old contract.
            Assert.Fail("pending T385");
        }
    }
}
