// STORY-069 — Distinguish empty-scope vs empty-catalog at /internal/safe-track
//
// BDD specification — xUnit. SPEC F25.3: the two 204 branches of
// InternalEndpoints.HandleSafeTrackAsync must emit distinct structured WARN log
// lines — "SafeScope empty (F4.4 degraded mode)" for the config branch,
// "SafeScope has libraries but no ready+measurable+eligible rows" for the data
// branch. The 200 (annotate) path stays silent. The 204 body and
// Cache-Control: no-store contract to the engine is unchanged (regression
// guard against the K2b/K6 shape).
//
// The distinct-log facts are Skip-pinned Integration until the endpoint
// handler is un-pinned against a captured ILogger sink — the un-pinning is
// the N2 work. The 200/204 shape facts stay in Story062/K6's home; this
// story covers the log-branch differentiation only.

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafeTrackDistinctWarnLogs
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — distinct WARN branches (needs a captured log sink)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTwoOhFourBranchesLogDistinctMessages
    {
        const string Skip = "Handler + captured ILogger sink: N2 un-pins these against InternalEndpoints.HandleSafeTrackAsync with a test logger asserting the two WARN messages.";

        [Fact(Skip = Skip)]
        public void AnEmptyScopeCallLogsAWarnNamingF44DegradedMode() { }

        [Fact(Skip = Skip)]
        public void ANonEmptyScopeWithNoReadyRowsLogsAWarnNamingTheDataDepletionCase() { }

        [Fact(Skip = Skip)]
        public void TheTwoWarnMessagesAreTextuallyDistinct() { }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — 200 path stays silent
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAnnotationPathEmitsNoWarn
    {
        const string Skip = "Handler + captured ILogger sink: on the 200 (annotate:.../path) branch no WARN is emitted.";

        [Fact(Skip = Skip)]
        public void AReadyRowPathReturnsAnnotateAndLogsNoWarn() { }
    }

    // ---------------------------------------------------------------------
    // SAD PATH / REGRESSION — 204 shape unchanged
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTwoOhFourResponseContractIsUnchanged
    {
        const string Skip = "Handler contract regression: 204 body is empty and Cache-Control: no-store is stamped on either branch — K2b/K6 shape must survive the log split.";

        [Fact(Skip = Skip)]
        public void EmptyScopeReturns204WithEmptyBody() { }

        [Fact(Skip = Skip)]
        public void EmptyCatalogReturns204WithEmptyBody() { }

        [Fact(Skip = Skip)]
        public void BothTwoOhFourBranchesStampCacheControlNoStore() { }
    }
}
