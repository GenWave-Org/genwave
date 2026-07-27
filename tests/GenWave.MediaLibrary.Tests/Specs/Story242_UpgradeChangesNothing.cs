// STORY-242 — Upgrading changes nothing on the air (SPEC F91.6, PLAN T118)
//
// BDD specification — xUnit, pending. db/27 seed-and-delete semantics, driven against a
// real Postgres via the migration-spec idiom (Story237 precedent). The allowlist-retirement
// half (AC3) lives in Story242_ActiveIdKeyRetired.cs (Host.Tests) — it drives PUT /api/settings.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureUpgradeChangesNothing
{
    public sealed class ScenarioSeedingFromActiveId
    {
        // Given Station:Persona:ActiveId > 0 referencing an existing persona, When db/27 runs.

        [Fact(Skip = "Pending (T118)")]
        public void SevenAllDayRowsExistForThatPersona() { }

        [Fact(Skip = "Pending (T118)")]
        public void SeededRowsCarryNullEnvelopeFields() { }

        [Fact(Skip = "Pending (T118)")]
        public void TheSettingsKeyRowIsDeleted() { }
    }

    public sealed class ScenarioEmptyWhenNoActiveDj
    {
        // Given Station:Persona:ActiveId absent or 0, When db/27 runs.

        [Fact(Skip = "Pending (T118)")]
        public void ScheduleTableIsEmpty() { }

        [Fact(Skip = "Pending (T118)")]
        public void TheSettingsKeyRowIsStillDeleted() { }
    }

    public sealed class ScenarioIdempotentReRun
    {
        // Sad path — migration house rule: a second run is a safe no-op.

        [Fact(Skip = "Pending (T118)")]
        public void SecondRunChangesNothingAndDoesNotError() { }
    }
}
