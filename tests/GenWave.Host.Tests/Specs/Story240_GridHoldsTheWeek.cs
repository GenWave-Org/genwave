// STORY-240 — The grid holds the week (SPEC F91.1, F91.8, PLAN T118/T122)
//
// BDD specification — xUnit, pending. Entry-point discipline: every T122 scenario drives
// GET/PUT /api/schedule through WebApplicationFactory<Program> with real cookie auth —
// never the week repository directly (T118's own facts may cover the store seam, but the
// wire contract is the feature).

namespace GenWave.Host.Tests.Specs;

public static class FeatureGridHoldsTheWeek
{
    public sealed class ScenarioStoringAValidWeek
    {
        // Given segment rows on 30-minute boundaries — some NULL persona (music-only),
        // some NULL envelope fields (station default), one midnight-spanning show as
        // two per-day rows — When the week is PUT and then GET.

        [Fact(Skip = "Pending (T122)")]
        public void RoundTripReturnsTheIdenticalWeekDocument() { }

        [Fact(Skip = "Pending (T122)")]
        public void MusicOnlySegmentsCarryNullPersona() { }

        [Fact(Skip = "Pending (T122)")]
        public void MidnightSpanningShowIsTwoPerDayRows() { }
    }

    public sealed class ScenarioAtomicReplace
    {
        // Given an existing stored week and a new valid week document, When PUT succeeds.

        [Fact(Skip = "Pending (T122)")]
        public void StoreHoldsExactlyTheNewWeek() { }

        [Fact(Skip = "Pending (T122)")]
        public void OldRowsAreGoneInTheSameTransaction() { }
    }

    public sealed class ScenarioRejectingInvalidWeeks
    {
        // Sad path — F91.1 constraints surface as per-cell 400s; the stored week never changes.

        [Fact(Skip = "Pending (T122)")]
        public void OverlappingSegmentsReturnPerCellErrorNamingDayAndRange() { }

        [Fact(Skip = "Pending (T122)")]
        public void OffGridStartMinuteIsRejected() { }

        [Fact(Skip = "Pending (T122)")]
        public void UnknownPersonaIdIsRejected() { }

        [Fact(Skip = "Pending (T122)")]
        public void RejectionLeavesTheStoredWeekUnchanged() { }

        [Fact(Skip = "Pending (T122)")]
        public void UnauthenticatedCallsMatchSettingsEndpointPosture() { }
    }
}
