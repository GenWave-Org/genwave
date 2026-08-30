// STORY-370 — I can thumb from the booth (SPEC F150.1, F150.8 · PLAN T367)
//
// BDD specification — xUnit. PENDING until T367. Entry-point discipline: every fact drives the
// REAL production binary (WebApplicationFactory<Program>, the Story345/Story366 factory idiom
// over an ephemeral station+library Postgres — tests/GenWave.Host.Tests/Support/
// EphemeralStationDatabase) seeded with a real booth-log track-started row, driven through
// POST /api/booth-log/{id}/station-thumb with a real Curation-authorized session (or none, for
// AC6). AC4 (the two distinct on-screen controls) is a Jest todo in admin-ui, not this suite.
namespace GenWave.Host.Tests.Specs;

public static class FeatureThumbFromTheBooth
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the station's own thumb lands, and nothing else it touches moves
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnOperatorStationThumbIsRecorded
    {
        // Given a booth-log track-started row 7 for media 42, When
        // POST /api/booth-log/7/station-thumb {direction: "up"} is called with a Curation session.
        [Fact(Skip = "pending T367 (STORY-370 AC1)")]
        public void MediaThumbHoldsTheOperatorRowForMedia42AtRow7sStartedAt() => Assert.Fail("pending T367");

        [Fact(Skip = "pending T367 (STORY-370 AC1)")]
        public void MediaRotation42ThumbsUpIsOne() => Assert.Fail("pending T367");
    }

    public sealed class ScenarioThePersonaTasteThumbIsUntouched
    {
        // Given the same thumb, When station.persona_taste and station.persona_taste_thumb are read.
        [Fact(Skip = "pending T367 (STORY-370 AC2)")]
        public void PersonaTasteIsByteIdenticalToBefore() => Assert.Fail("pending T367");

        [Fact(Skip = "pending T367 (STORY-370 AC2)")]
        public void PersonaTasteThumbIsByteIdenticalToBefore() => Assert.Fail("pending T367");
    }

    public sealed class ScenarioTheCurationLedgerIsUntouched
    {
        // Given the same thumb, When library.media_rating is read.
        [Fact(Skip = "pending T367 (STORY-370 AC3)")]
        public void ItIsByteIdenticalToBefore() => Assert.Fail("pending T367");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — non-music rows and unauthenticated callers get nothing
    // ---------------------------------------------------------------------

    public sealed class ScenarioNonMusicRowsAreNotThumbable
    {
        // Given a booth-log patter-aired row, When station-thumb is posted for it.
        [Fact(Skip = "pending T367 (STORY-370 AC5)")]
        public void TheResponseIsFourHundredNamingTheKind() => Assert.Fail("pending T367");
    }

    public sealed class ScenarioTheSurfaceIsAdminOnly
    {
        // Given no session, When station-thumb is posted.
        [Fact(Skip = "pending T367 (STORY-370 AC6)")]
        public void TheResponseIsFourOhOne() => Assert.Fail("pending T367");
    }
}
