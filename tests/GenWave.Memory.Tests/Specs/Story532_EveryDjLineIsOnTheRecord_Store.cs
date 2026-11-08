// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218 · PLAN T682)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files; see tests/GenWave.Tts.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Copy.cs for the map.

namespace GenWave.Memory.Tests.Specs;

public static class FeatureEveryDjLineIsOnTheRecordStore
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC5 — unscheduled hours have no show
    // entry point: IUtteranceStore.RecordAsync (via LlmCopyWriter.WriteAsync)
    public sealed class ScenarioUnscheduledHoursHaveNoShow
    {
        // Given no schedule segment is on air
        // When  a copy is stored

        [Fact(Skip = "pending: T682 — see docs/PLAN.md")]
        public void ShowIdIsNullAndShiftKeyIsThePersonaIdPlusTheStationLocalDate() => Assert.Fail("pending: T682 — show_id is null and shift_key is the persona id plus the station-local date");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — old rows go
    public sealed class ScenarioOldRowsGo
    {
        // Given a row written 29 days ago
        // When  a new utterance is inserted

        [Fact(Skip = "pending: T682 — see docs/PLAN.md")]
        public void The29DayOldRowIsGone() => Assert.Fail("pending: T682 — the 29-day-old row is gone");
    }
}
