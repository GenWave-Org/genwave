// STORY-559 — Old audio is replaced quietly (SPEC F219.22): the safe-content ledger against a real Postgres.
//
// The jsonb round trip and the hand-damaged row are database behaviour a fake ledger never exercises.

namespace GenWave.Host.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using GenWave.Host.Seeding;
using GenWave.Host.Tests.Support;

public static class FeatureTheSafeContentLedgerSurvivesPostgres
{
    [Trait("Category", "Integration")]
    public sealed class ScenarioARecordIsSavedAndRead : IAsyncLifetime
    {
        IAsyncDisposable? database;
        IReadOnlyList<SafeContentRecord> read = [];

        public async Task InitializeAsync()
        {
            var db = await LedgerDatabase.StartAsync();
            database = db;
            var ledger = new SafeContentLedger(db.StationConnectionString, NullLogger<SafeContentLedger>.Instance);
            await ledger.SaveAsync([new SafeContentRecord(11, "chatterbox\nafheart", PendingRetireMediaId: 4)], CancellationToken.None);
            read = await ledger.ListAsync(CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            if (database is not null)
                await database.DisposeAsync();
        }

        [Fact]
        public void TheRecordComesBackWhole() =>
            Assert.Equal(new SafeContentRecord(11, "chatterbox\nafheart", 4), Assert.Single(read));
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioARecordIsMissingItsMarker : IAsyncLifetime
    {
        IAsyncDisposable? database;
        IReadOnlyList<SafeContentRecord> read = [];

        public async Task InitializeAsync()
        {
            var db = await LedgerDatabase.StartAsync();
            database = db;
            await db.WriteRawAsync(SafeContentLedger.Key, """[{"MediaId":12}]""");
            var ledger = new SafeContentLedger(db.StationConnectionString, NullLogger<SafeContentLedger>.Instance);
            read = await ledger.ListAsync(CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            if (database is not null)
                await database.DisposeAsync();
        }

        [Fact]
        public void TheRecordIsStillListed() => Assert.Equal(12, Assert.Single(read).MediaId);

        [Fact]
        public void ItsMarkerReadsAsEmpty() => Assert.Equal(string.Empty, Assert.Single(read).Marker);
    }
}

file sealed class LedgerDatabase : EphemeralStationDatabase
{
    LedgerDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<LedgerDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-ledger");
        var db = new LedgerDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }

    public async Task WriteRawAsync(string key, string json)
    {
        await using var conn = new NpgsqlConnection(StationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into station.settings (key, value) values (@key, @value::jsonb)";
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", json);
        await cmd.ExecuteNonQueryAsync();
    }
}
