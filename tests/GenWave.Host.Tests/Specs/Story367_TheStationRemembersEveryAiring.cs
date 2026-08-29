// STORY-367 — The station remembers every airing (SPEC F149.1–F149.3 · PLAN T354, T355)
//
// BDD specification — xUnit. AC1-AC4, AC7, AC8 remain PENDING until T355 (they drive the REAL
// TrackAired path through the production binary, which does not exist until then). AC5/AC6 (this
// file's own ScenarioTheLedgerIsSeededOnceFromTheSurvivingBoothLog/ScenarioSeedingIsIdempotent) are
// WIRED at T354: they seed station.booth_log rows directly on an ephemeral station+library Postgres
// (tests/GenWave.Host.Tests/Support/EphemeralStationDatabase, the Story345/Story366 factory idiom's
// database half — no WebApplicationFactory here, since there is no production sink to boot yet), run
// db/41-gardener-migration.sh's one-shot seed step over that fixture via
// EphemeralStationDatabase.RunFileInContainer (the SAME docker-exec mechanism
// GenWave.MediaLibrary.Tests.DatabaseFixture already uses — no second mechanism invented), and read
// the resulting library.media_rotation row back. Each scenario is its own self-contained arc with its
// OWN ephemeral Postgres (the Story345 "every scenario arranges its own Postgres exactly once" idiom).

using Npgsql;
using GenWave.Host.Tests.Support;

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

    public sealed class ScenarioTheLedgerIsSeededOnceFromTheSurvivingBoothLog(LedgerSeedArc arc)
        : IClassFixture<LedgerSeedArc>
    {
        // Given a booth log with N track-started rows for media 42 (min T_first, max T_last) and
        // no ledger, When the migration runs.
        [Fact]
        public void PlayCountIsN() => Assert.Equal(3, arc.PlayCount);

        [Fact]
        public void FirstAiredAtIsTFirst() => Assert.Equal(arc.FirstOccurredAt, arc.LedgerFirstAiredAt);

        [Fact]
        public void LastAiredAtIsTLast() => Assert.Equal(arc.LastOccurredAt, arc.LedgerLastAiredAt);
    }

    public sealed class ScenarioSeedingIsIdempotent(SeedIdempotencyArc arc) : IClassFixture<SeedIdempotencyArc>
    {
        // Given a seeded ledger, When the migration runs again.
        [Fact]
        public void EveryLedgerRowIsUnchanged() => Assert.Equal(arc.FirstRun, arc.SecondRun);
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

/// <summary>
/// A booth log with three <c>track-started</c> rows for one media row, and no ledger yet. Runs
/// db/41-gardener-migration.sh's one-shot seed exactly once and captures the resulting
/// <c>library.media_rotation</c> row alongside the raw booth-log timestamps the assertions compare
/// it against (STORY-367 AC5).
/// </summary>
public sealed class LedgerSeedArc : IAsyncLifetime
{
    public long PlayCount { get; private set; }
    public DateTimeOffset FirstOccurredAt { get; private set; }
    public DateTimeOffset LastOccurredAt { get; private set; }
    public DateTimeOffset LedgerFirstAiredAt { get; private set; }
    public DateTimeOffset LedgerLastAiredAt { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field: GardenerSeedTestDatabase is file-local (CS9051 forbids it in a
        // member signature of this public type), and every value this arc exposes is captured
        // into a property below before the container ever tears down — the same "await using var
        // db = ..." shape Story345_PaWireProof.cs's own arcs already use.
        await using var database = await GardenerSeedTestDatabase.StartAsync();

        var mediaId = await GardenerSeedFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/gardener-seed-ac5.flac");

        FirstOccurredAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var middle = DateTimeOffset.Parse("2026-08-10T00:00:00Z");
        LastOccurredAt = DateTimeOffset.Parse("2026-08-20T00:00:00Z");

        await GardenerSeedFixtures.InsertTrackStartedAsync(database.StationConnectionString, mediaId, FirstOccurredAt);
        await GardenerSeedFixtures.InsertTrackStartedAsync(database.StationConnectionString, mediaId, middle);
        await GardenerSeedFixtures.InsertTrackStartedAsync(database.StationConnectionString, mediaId, LastOccurredAt);

        database.RunFileInContainer(Path.Combine(GardenerSeedFixtures.RepoRoot(), "db", "41-gardener-migration.sh"));

        var row = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the seed migration ran");
        PlayCount = row.PlayCount;
        LedgerFirstAiredAt = row.FirstAiredAt ?? throw new InvalidOperationException("first_aired_at was null");
        LedgerLastAiredAt = row.LastAiredAt ?? throw new InvalidOperationException("last_aired_at was null");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Runs db/41-gardener-migration.sh's one-shot seed step TWICE against the same booth log and
/// captures the full <c>library.media_rotation</c> row after each run — the honest way to prove
/// "every ledger row is unchanged" (STORY-367 AC6) is a byte-for-byte row comparison, including
/// <c>updated_at</c> (an <c>on conflict ... do nothing</c> re-run must never touch it).
/// </summary>
public sealed class SeedIdempotencyArc : IAsyncLifetime
{
    public GardenerSeedFixtures.LedgerRow FirstRun { get; private set; }
    public GardenerSeedFixtures.LedgerRow SecondRun { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — see LedgerSeedArc's own remarks (CS9051, the file-local
        // GardenerSeedTestDatabase type).
        await using var database = await GardenerSeedTestDatabase.StartAsync();

        var mediaId = await GardenerSeedFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/gardener-seed-ac6.flac");
        await GardenerSeedFixtures.InsertTrackStartedAsync(
            database.StationConnectionString, mediaId, DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var scriptPath = Path.Combine(GardenerSeedFixtures.RepoRoot(), "db", "41-gardener-migration.sh");

        database.RunFileInContainer(scriptPath);
        FirstRun = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the first seed run");

        database.RunFileInContainer(scriptPath);
        SecondRun = await GardenerSeedFixtures.ReadLedgerRowAsync(database.LibraryConnectionString, mediaId)
            ?? throw new InvalidOperationException("expected a library.media_rotation row after the second seed run");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Arrange helpers shared by <see cref="LedgerSeedArc"/> and <see cref="SeedIdempotencyArc"/> —
/// raw SQL against the ephemeral database's own connection strings, never through a repository (T354
/// ships no C# repository yet; that is T355's own job).</summary>
public static class GardenerSeedFixtures
{
    public readonly record struct LedgerRow(long PlayCount, DateTimeOffset? FirstAiredAt, DateTimeOffset? LastAiredAt, DateTimeOffset UpdatedAt);

    public static async Task<long> InsertMediaRowAsync(string libraryConnectionString, string path)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state)
            values (@path, 'flac', 1024, now(), 'ready')
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    public static async Task InsertTrackStartedAsync(string stationConnectionString, long mediaId, DateTimeOffset occurredAt)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.booth_log (occurred_at, kind, summary, media_id)
            values (@occurredAt, 'track-started', 'seed fixture', @mediaId)
            """;
        cmd.Parameters.AddWithValue("occurredAt", occurredAt);
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<LedgerRow?> ReadLedgerRowAsync(string libraryConnectionString, long mediaId)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select play_count, first_aired_at, last_aired_at, updated_at from library.media_rotation where media_id = @mediaId";
        cmd.Parameters.AddWithValue("mediaId", mediaId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new LedgerRow(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }
}

file sealed class GardenerSeedTestDatabase : EphemeralStationDatabase
{
    GardenerSeedTestDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<GardenerSeedTestDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-gardenseed");
        var db = new GardenerSeedTestDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
