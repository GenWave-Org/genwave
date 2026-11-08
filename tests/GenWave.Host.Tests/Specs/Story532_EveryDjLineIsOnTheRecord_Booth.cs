// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218 · PLAN T684 T685)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files; see tests/GenWave.Tts.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Copy.cs for the map.

namespace GenWave.Host.Tests.Specs;

public static class FeatureEveryDjLineIsOnTheRecordBooth
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC3 — the booth feed quotes it
    // entry point: BoothLogWriter via the event bus (SegmentGenerated)
    public sealed class ScenarioTheBoothFeedQuotesIt
    {
        // Given a SegmentGenerated event carrying PersonaId, ShowId and UtteranceId
        // When  BoothLogWriter publishes it

        [Fact(Skip = "pending: T684 — see docs/PLAN.md")]
        public void ThePatterAiredRowStampsPersonaIdAndShowId() => Assert.Fail("pending: T684 — the patter-aired row stamps persona_id and show_id");

        [Fact(Skip = "pending: T684 — see docs/PLAN.md")]
        public void ItsSummaryEndsWithTheCopySFirst80CharactersInQuotes() => Assert.Fail("pending: T684 — its summary ends with the copy's first 80 characters in quotes");
    }

    // AC4 — air time is marked
    // entry point: engine metadata callback (gw_tts="true" file started)
    public sealed class ScenarioAirTimeIsMarked
    {
        // Given a stored utterance whose file the engine then starts
        // When  the engine reports a gw_tts="true" file started

        [Fact(Skip = "pending: T685 — see docs/PLAN.md")]
        public void ThatUtteranceSAiredAtIsSet() => Assert.Fail("pending: T685 — that utterance's aired_at is set");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — dropped renders never air
    public sealed class ScenarioDroppedRendersNeverAir
    {
        // Given a stored utterance whose render is later dropped
        // When  no engine event arrives

        [Fact(Skip = "pending: T685 — see docs/PLAN.md")]
        public void AiredAtStaysNull() => Assert.Fail("pending: T685 — aired_at stays null");
    }
}
