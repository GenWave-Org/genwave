// STORY-382 — I page through a big kind at my own pace (SPEC F153.9 rider 2026-08-31 · PLAN T386 · gh-#657)
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program>
// against a real ephemeral Postgres — the Story374/Story378 arc idiom): these facts drive
// GET /api/gardener/findings over HTTP with an authed admin session, never the repository
// directly. Specs are Skip-pinned until T386 wires the controller; /build-loop fills the
// bodies and removes the Skip.
//
// Under spec: a kind=-scoped response gains `total` (groups for near_duplicate, rows otherwise);
// the near-duplicate path routes through T385's group-paged read; a call WITHOUT kind= stays
// byte-compatible with the T377 shape (flat page, grouped response, NO total property). The
// 400/clamp posture is T377's and is not re-pinned here. STORY-383 AC1–AC3's wire half lives
// here; their store half is MediaLibrary.Tests Story383_DuplicateClustersNeverSplit.cs.

namespace GenWave.Host.Tests.Specs;

public static class FeatureKindScopedPagingOnTheWire
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    /// <summary>STORY-382 AC6 — a flat kind, scoped: 60 open dead_file findings seeded.</summary>
    public sealed class ScenarioKindScopedResponseCarriesTotal
    {
        [Fact(Skip = "Pending T386 — see docs/PLAN.md")]
        public void TotalIsTheExactOpenRowCountForTheKind()
        {
            // GET /api/gardener/findings?kind=dead_file&state=open&limit=25 → body.total == 60.
            Assert.Fail("pending T386");
        }

        [Fact(Skip = "Pending T386 — see docs/PLAN.md")]
        public void ThePageCarriesLimitRowsOfThatKindOnly()
        {
            // 25 findings, every group.kind == dead_file.
            Assert.Fail("pending T386");
        }
    }

    /// <summary>STORY-383 AC1–AC3 on the wire — 30 seeded duplicate groups of 2–4 members.</summary>
    public sealed class ScenarioNearDuplicatesPageByGroupOnTheWire
    {
        [Fact(Skip = "Pending T386 — see docs/PLAN.md")]
        public void LimitSelectsWholeGroupsNeverPartialOnes()
        {
            // ?kind=near_duplicate&limit=25 → 25 duplicateGroups, each with ALL its members.
            Assert.Fail("pending T386");
        }

        [Fact(Skip = "Pending T386 — see docs/PLAN.md")]
        public void OffsetContinuesAtTheNextGroup()
        {
            // ?offset=25 → the remaining 5 groups, disjoint from page one's groupKeys.
            Assert.Fail("pending T386");
        }

        [Fact(Skip = "Pending T386 — see docs/PLAN.md")]
        public void TotalCountsGroupsNotRows()
        {
            // body.total == 30 while /api/status.gardener.open.nearDuplicate keeps the ROW count.
            Assert.Fail("pending T386");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH / regression pins
    // ---------------------------------------------------------------------

    /// <summary>STORY-382 AC8 — the T377 contract for un-scoped callers stands verbatim.</summary>
    public sealed class ScenarioTheUnscopedCallKeepsTheT377Shape
    {
        [Fact(Skip = "Pending T386 — see docs/PLAN.md")]
        public void CarriesNoTotalProperty()
        {
            // GET /api/gardener/findings?state=open → the JSON body has no "total" member at all.
            Assert.Fail("pending T386");
        }

        [Fact(Skip = "Pending T386 — see docs/PLAN.md")]
        public void PagesFlatAcrossKinds()
        {
            // Seed enough dead_file rows to fill the page: the near_duplicate group is absent —
            // the gh-#654 behavior, correct for THIS un-scoped shape and pinned as such.
            Assert.Fail("pending T386");
        }
    }
}
