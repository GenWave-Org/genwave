// STORY-367 — The station remembers every airing (SPEC F149.1–F149.3 · PLAN T354, T355)
//
// BDD specification — xUnit. PENDING until T354/T355. Entry-point discipline: every fact drives
// the REAL production TrackAired path through the production binary (WebApplicationFactory<Program>,
// the Story345/Story366 factory idiom over an ephemeral station+library Postgres —
// tests/GenWave.Host.Tests/Support/EphemeralStationDatabase) — a real airing publishes TrackAired
// through the CompositeStationEventSink and this file reads library.media_rotation/library.media
// back off the same db. AC5/AC6 arrange differently: they seed station.booth_log rows directly on
// the ephemeral db, then run the db/41 migration's one-shot seed step over that fixture (T354,
// before T355's sink has ever written a row) and read the resulting library.media_rotation back.
namespace GenWave.Host.Tests.Specs;

public static class FeatureTheStationRemembersEveryAiring
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — every music airing lands in the ledger, nothing else does
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnAiringIncrementsTheLedger
    {
        // Given a ready music row with no media_rotation row, When a TrackAired event for it
        // reaches the station event sinks.
        [Fact(Skip = "pending T355 (STORY-367 AC1)")]
        public void TheLedgerRowExistsWithPlayCountOne() => Assert.Fail("pending T355");
    }

    public sealed class ScenarioFirstAndLastAiredStamps
    {
        // Given a row whose ledger says play_count 1, first_aired_at T1, When it airs again at T2.
        [Fact(Skip = "pending T355 (STORY-367 AC2)")]
        public void PlayCountIsTwo() => Assert.Fail("pending T355");

        [Fact(Skip = "pending T355 (STORY-367 AC2)")]
        public void FirstAiredAtIsStillTOne() => Assert.Fail("pending T355");

        [Fact(Skip = "pending T355 (STORY-367 AC2)")]
        public void LastAiredAtIsTTwo() => Assert.Fail("pending T355");
    }

    public sealed class ScenarioTheMediaRowsETagSurvivesAnAiring
    {
        // Given a media row with a known xmin, When it airs.
        [Fact(Skip = "pending T355 (STORY-367 AC3)")]
        public void TheMediaRowsXminIsUnchanged() => Assert.Fail("pending T355");
    }

    public sealed class ScenarioNonMusicNeverTouchesTheLedger
    {
        // Given a break of idents, patter, crosstalk, and an announcement, When every one of
        // them airs.
        [Fact(Skip = "pending T355 (STORY-367 AC4)")]
        public void MediaRotationIsByteIdenticalBeforeAndAfter() => Assert.Fail("pending T355");
    }

    public sealed class ScenarioTheLedgerIsSeededOnceFromTheSurvivingBoothLog
    {
        // Given a booth log with N track-started rows for media 42 (min T_first, max T_last) and
        // no ledger, When the migration runs.
        [Fact(Skip = "pending T354 (STORY-367 AC5)")]
        public void PlayCountIsN() => Assert.Fail("pending T354");

        [Fact(Skip = "pending T354 (STORY-367 AC5)")]
        public void FirstAiredAtIsTFirst() => Assert.Fail("pending T354");

        [Fact(Skip = "pending T354 (STORY-367 AC5)")]
        public void LastAiredAtIsTLast() => Assert.Fail("pending T354");
    }

    public sealed class ScenarioSeedingIsIdempotent
    {
        // Given a seeded ledger, When the migration runs again.
        [Fact(Skip = "pending T354 (STORY-367 AC6)")]
        public void EveryLedgerRowIsUnchanged() => Assert.Fail("pending T354");
    }

    public sealed class ScenarioTheLedgerNamesItsOwnEpoch
    {
        // Given a migrated station, When Gardener:RotationSince is read.
        [Fact(Skip = "pending T355 (STORY-367 AC7)")]
        public void ItIsTheMigrationTimestamp() => Assert.Fail("pending T355");

        [Fact(Skip = "pending T355 (STORY-367 AC7)")]
        public void EveryNeverAiredCountIsReturnedBesideIt() => Assert.Fail("pending T355");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a ledger failure never touches air
    // ---------------------------------------------------------------------

    public sealed class ScenarioALedgerWriteFailureNeverDelaysAir
    {
        // Given a ledger repository that throws, When a TrackAired event is published.
        [Fact(Skip = "pending T355 (STORY-367 AC8)")]
        public void TheFeedersPushTimingIsUnchanged() => Assert.Fail("pending T355");

        [Fact(Skip = "pending T355 (STORY-367 AC8)")]
        public void ExactlyOneWarnNamesTheLedger() => Assert.Fail("pending T355");
    }
}
