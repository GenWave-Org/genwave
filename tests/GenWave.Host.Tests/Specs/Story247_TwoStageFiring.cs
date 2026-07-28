// STORY-247 — Two-stage firing with a parachute (SPEC F94.2, F91.9, PLAN T121/T128)
//
// BDD specification — xUnit, pending. The Fire modal (export offer before Delete enables,
// cancel = no-op) is T128 browser acceptance per the T92 precedent. These facts pin the
// server contracts: bench derivation, the FK guard, and benched-delete. Entry-point
// discipline: real DELETE /api/personas/{id} and PUT /api/schedule through
// WebApplicationFactory<Program>.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTwoStageFiring
{
    public sealed class ScenarioBenchingByUnpainting
    {
        // Given a DJ scheduled in one slot, When that slot is removed via PUT /api/schedule.

        [Fact(Skip = "Pending (T121)")]
        public void PersonaRecordIsUntouched() { }

        [Fact(Skip = "Pending (T121)")]
        public void PersonaNoLongerAppearsInAnyScheduleRow() { }
    }

    public sealed class ScenarioDeletingFromTheBench
    {
        // Given a benched persona (zero schedule rows), When DELETE /api/personas/{id}.

        [Fact(Skip = "Pending (T121)")]
        public void BenchedDeleteProceeds() { }

        [Fact(Skip = "Pending (T121)")]
        public void ExportRemainsAvailableUntilTheDelete() { }
    }

    public sealed class ScenarioScheduledPersonasAreUndeletable
    {
        // Sad path — F91.9: the FK guard replaces delete-clears-active (F35.5).

        [Fact(Skip = "Pending (T121)")]
        public void DeleteReturns409NamingTheSlots() { }

        [Fact(Skip = "Pending (T121)")]
        public void NothingIsDeletedOn409() { }

        [Fact(Skip = "Pending (T128): browser acceptance — export offered before Delete enables; cancel is a no-op")]
        public void FireModalFlowIsBrowserAcceptance() { }
    }
}
