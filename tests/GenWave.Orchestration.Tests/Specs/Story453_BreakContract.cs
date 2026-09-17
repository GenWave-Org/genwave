// STORY-453 — The break contract has one fact per cell (gh-#401 · SPEC F186 · PLAN T517–T519)
//
// BDD specification — xUnit. One fact per cell of the SPEC F186 table, all against the unsplit code. Rows AC1–AC17 (T517), drops AC18–AC23
// (T518). AC24 (the two follow-up issues) is a manual check on PR-2's body. F186.4: back-announce on a
// ceremony-only unit STAYS (Dean, 2026-09-17); the other two cells are scripted as-built and filed as follow-ups.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBreakContract
{
    const string Pending = "pending: T517 — every F186 row and collision pinned through the builder (STORY-453)";
    const string Manual = "manual: two follow-up issues named in SPEC F186.4 — review evidence on PR-2 (STORY-453)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioBackAnnounceWithAPreviousTrack
    {
        // Given: cadence back-announce on, a previous track

        /// <summary>AC1 — the first spoken item names the previous track</summary>
        [Fact(Skip = Pending)]
        public void BuffersABackAnnounceFirst() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheFirstUnit
    {
        // Given: no previous track

        /// <summary>AC2 — nothing to announce</summary>
        [Fact(Skip = Pending)]
        public void BuffersNoBackAnnounce() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAVendableCrosstalk
    {
        // Given: a crosstalk vend, a next track, no drainAsOf, no ceremony due

        /// <summary>AC3 — the crosstalk file sits after the back-announce</summary>
        [Fact(Skip = Pending)]
        public void FollowsTheBackAnnounce() => Assert.Fail(Pending);
    }

    public sealed class ScenarioACrosstalkAndADueSignOff
    {
        // Given: a vendable crosstalk with a SignOff due now

        /// <summary>AC4 — a drained ceremony excludes crosstalk</summary>
        [Fact(Skip = Pending)]
        public void BuffersNoCrosstalk() => Assert.Fail(Pending);
    }

    public sealed class ScenarioThreePendingAnnouncements
    {
        // Given: three claimable announcements

        /// <summary>AC5 — the cap is two per break</summary>
        [Fact(Skip = Pending)]
        public void BuffersExactlyTwo() => Assert.Fail(Pending);
    }

    public sealed class ScenarioStationIdCadenceHit
    {
        // Given: StationIdEveryNUnits = 2, unit 2

        /// <summary>AC6 — the cadence enqueues a StationId</summary>
        [Fact(Skip = Pending)]
        public void BuffersAStationIdAfterTheAnnouncements() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAdCadenceHit
    {
        // Given: ad cadence every 2 units and a vendable spot, unit 2

        /// <summary>AC7 — the vended spot is buffered</summary>
        [Fact(Skip = Pending)]
        public void BuffersTheSpot() => Assert.Fail(Pending);
    }

    public sealed class ScenarioStationIdAndAdDueTogether
    {
        // Given: both cadences hit the same unit

        /// <summary>AC8 — the drain tiebreak</summary>
        [Fact(Skip = Pending)]
        public void StationIdPrecedesTheAd() => Assert.Fail(Pending);
    }

    public sealed class ScenarioContextAndTimeDateDueTogether
    {
        // Given: both deferrals due the same unit

        /// <summary>AC9 — the drain tiebreak</summary>
        [Fact(Skip = Pending)]
        public void ContextPrecedesTimeDate() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAPooledStationId
    {
        // Given: catalog with a ready station-id asset

        /// <summary>AC10 — the pool hit is used</summary>
        [Fact(Skip = Pending)]
        public void BuffersThePooledItem() => Assert.Fail(Pending);

        /// <summary>AC10 — no render when the pool hits</summary>
        [Fact(Skip = Pending)]
        public void SendsNoShowIdentRequest() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnEmptyStationIdPool
    {
        // Given: catalog without a station-id asset

        /// <summary>AC11 — the tts fake received the render</summary>
        [Fact(Skip = Pending)]
        public void SendsAShowIdentRequest() => Assert.Fail(Pending);
    }

    public sealed class ScenarioALateTimeDate
    {
        // Given: TimeDate due 10 s ago with a 90 s budget

        /// <summary>AC12 — the request says Late</summary>
        [Fact(Skip = Pending)]
        public void CarriesFreshnessLate() => Assert.Fail(Pending);
    }

    public sealed class ScenarioADueContextSegment
    {
        // Given: a Context deferral with provider key weather

        /// <summary>AC13 — the buffered item names the provider</summary>
        [Fact(Skip = Pending)]
        public void CarriesTheProviderKey() => Assert.Fail(Pending);
    }

    public sealed class ScenarioLeadInOn
    {
        // Given: cadence lead-in on

        /// <summary>AC14 — lead-in sits last</summary>
        [Fact(Skip = Pending)]
        public void IsTheLastSpokenItemBeforeTheTrack() => Assert.Fail(Pending);
    }

    public sealed class ScenarioACeremonyOnlyUnit
    {
        // Given: a SignOff below the music floor and a vendable crosstalk

        /// <summary>AC15 — </summary>
        [Fact(Skip = Pending)]
        public void BuffersNoCrosstalk() => Assert.Fail(Pending);

        /// <summary>AC15 — </summary>
        [Fact(Skip = Pending)]
        public void BuffersNoLeadIn() => Assert.Fail(Pending);

        /// <summary>AC15 — </summary>
        [Fact(Skip = Pending)]
        public void BuffersNoTrack() => Assert.Fail(Pending);
    }

    public sealed class ScenarioACeremonyOnlyUnitWithAPreviousTrack
    {
        // Given: the same unit with back-announce on (Dean: stays)

        /// <summary>AC16 — the outgoing DJ signs off the last track</summary>
        [Fact(Skip = Pending)]
        public void BuffersABackAnnounceBeforeTheSignOff() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAStraddlingTrackWithASignOffPending
    {
        // Given: the track crosses the boundary

        /// <summary>AC17 — NotBefore at or after now plus the tail</summary>
        [Fact(Skip = Pending)]
        public void HoldsTheSignOnPastTheQueuedTail() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnExpiredTimeDate
    {
        // Given: TimeDate due 120 s ago with a 90 s budget

        /// <summary>AC18 — </summary>
        [Fact(Skip = Pending)]
        public void BuffersNoTimeDate() => Assert.Fail(Pending);

        /// <summary>AC18 — </summary>
        [Fact(Skip = Pending)]
        public void LogsOneExpiryLine() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnAdVendThatThrows
    {
        // Given: the vend throws

        /// <summary>AC19 — </summary>
        [Fact(Skip = Pending)]
        public void AssemblesTheBreakWithoutAnAd() => Assert.Fail(Pending);

        /// <summary>AC19 — </summary>
        [Fact(Skip = Pending)]
        public void LogsOneWarnNamingTheVend() => Assert.Fail(Pending);
    }

    public sealed class ScenarioANullLeadInRender
    {
        // Given: the lead-in render returns null

        /// <summary>AC20 — the slot drops alone</summary>
        [Fact(Skip = Pending)]
        public void KeepsEveryOtherItemInPosition() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAnOverBudgetBackAnnounce
    {
        // Given: the back-announce render exceeds the budget

        /// <summary>AC21 — the slot drops alone</summary>
        [Fact(Skip = Pending)]
        public void KeepsEveryOtherItemInPosition() => Assert.Fail(Pending);
    }

    public sealed class ScenarioANullSignOffRender
    {
        // Given: the SignOff render returns null

        /// <summary>AC22 — </summary>
        [Fact(Skip = Pending)]
        public void PublishesOneHandoffPieceDropped() => Assert.Fail(Pending);
    }

    public sealed class ScenarioANullAnnouncementRender
    {
        // Given: an announcement render returns null

        /// <summary>AC23 — the claim is not released</summary>
        [Fact(Skip = Pending)]
        public void KeepsTheAnnouncementClaimed() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheFollowUpsOnPrTwo
    {
        // Given: PR-2's body (manual)

        /// <summary>AC24 — announcements on a ceremony-only unit; ad in a straddle break</summary>
        [Fact(Skip = Manual)]
        public void TwoIssuesExistInProjectThree() => Assert.Fail(Manual);
    }
}
