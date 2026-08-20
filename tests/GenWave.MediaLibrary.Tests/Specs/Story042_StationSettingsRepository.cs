// STORY-042 — Station settings overlay store (gh-#406 slice 3: the DB-backed row I/O half)
//
// BDD specification — xUnit (SPEC F19, STORY-042). Postgres-backed (Category=Integration,
// shared DatabaseFixture) — the upsert's ON CONFLICT behavior and the raw stored JSONB text are
// real-DB behavior a fake store would never exercise honestly. gh-#406 slice 3 moved
// StationSettingsRepository's WriteAsync/ReadAllAsync out of GenWave.Host.Configuration.
// StationSettingsStore byte-identical; this file owns the repository's own contract. The
// allowlist-filtered/degrade-on-DB-down behavior stays GenWave.Host.Tests' own coverage
// (Story042_StationSettingsOverlayProvider.cs, FeatureStationSettingsOverlayProvider) — this
// repository deliberately returns every row unfiltered and lets failures propagate.
//
// gh-#406 slice 4 added ExistsAsync (single-key existence probe) for
// GenWave.Host.Seeding.SafeLoopSeedMarkerStore's boot-seed marker check (F27.10) — its own
// coverage is the FeatureExistsAsync section below.
//
// gh-#406 slice 5 added ReadAllForBoot (the sync exception, SQL byte-identical to ReadAllAsync)
// for GenWave.Host.Configuration.StationSettingsConfigurationProvider.Load(), the synchronous
// IConfigurationProvider contract member — its own coverage is the ScenarioReadAllForBoot section
// below.

using System.Text.Json;
using Dapper;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureStationSettingsRepository
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static StationSettingsRepository Repo(DatabaseFixture db) => new(db.StationConnectionString);

    sealed record SettingsRow
    {
        public string Key { get; init; } = "";
        public string Value { get; init; } = "";
    }

    static async Task<SettingsRow> ReadRowAsync(DatabaseFixture db, string key)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<SettingsRow>(
            "select key, value::text as value from station.settings where key = @key",
            new { key });
    }

    static async Task<int> CountRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.settings");
    }

    static async Task InsertRawRowAsync(DatabaseFixture db, string key, string jsonValue)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into station.settings (key, value) values (@key, @jsonValue::jsonb)",
            new { key, jsonValue });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — WriteAsync inserts a new row (F19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWriteNewKey(DatabaseFixture db)
    {
        [Fact]
        public async Task A_new_key_lands_with_its_json_serialized_value()
        {
            // Given no prior row for this key...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When a value is written...
            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);

            // Then the row holds exactly the JSON serialization of that value.
            var row = await ReadRowAsync(db, "Loudness:TargetLufs");
            Assert.Equal(JsonSerializer.Serialize(-14.0), row.Value);
        }

        [Fact]
        public async Task A_string_value_is_stored_as_a_json_string()
        {
            // Given no prior row...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When a string setting is written...
            await repo.WriteAsync("Station:Theme", "midnight", CancellationToken.None);

            // Then it round-trips through JSONB as a quoted JSON string.
            var row = await ReadRowAsync(db, "Station:Theme");
            Assert.Equal("\"midnight\"", row.Value);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — WriteAsync upserts an existing row (F19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWriteExistingKey(DatabaseFixture db)
    {
        [Fact]
        public async Task WritingTheSameKeyTwiceUpdatesInPlaceRatherThanDuplicating()
        {
            // Given a key already written once...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteAsync("Loudness:TargetLufs", -16.0, CancellationToken.None);

            // When it is written again with a new value...
            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);

            // Then the row now holds the new value, and there is still exactly one row for it.
            var row = await ReadRowAsync(db, "Loudness:TargetLufs");
            Assert.Equal(JsonSerializer.Serialize(-14.0), row.Value);
            Assert.Equal(1, await CountRowsAsync(db));
        }

        [Fact]
        public async Task UpdatedAtAdvancesOnTheSecondWrite()
        {
            // Given a key already written once...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteAsync("Loudness:TargetLufs", -16.0, CancellationToken.None);

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var firstUpdatedAt = await conn.ExecuteScalarAsync<DateTime>(
                "select updated_at from station.settings where key = @key",
                new { key = "Loudness:TargetLufs" });

            // When it is written again a moment later...
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);

            // Then updated_at moved forward.
            var secondUpdatedAt = await conn.ExecuteScalarAsync<DateTime>(
                "select updated_at from station.settings where key = @key",
                new { key = "Loudness:TargetLufs" });
            Assert.True(secondUpdatedAt > firstUpdatedAt);
        }

        [Fact]
        public async Task VersionAdvancesOnTheSecondUnconditionalWrite()
        {
            // gh-#486: WriteAsync's own ON CONFLICT branch must keep `version` moving even for the
            // unconditional path, so a LATER version-guarded write (WriteIfVersionMatchesAsync)
            // reading this key afterward sees the true current version, not a stale one frozen at
            // whatever the first insert set.
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            await repo.WriteAsync("Loudness:TargetLufs", -16.0, CancellationToken.None);
            var versions = await repo.ReadVersionsAsync(CancellationToken.None);
            Assert.Equal(1, versions["Loudness:TargetLufs"]);

            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);
            versions = await repo.ReadVersionsAsync(CancellationToken.None);
            Assert.Equal(2, versions["Loudness:TargetLufs"]);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — WriteIfVersionMatchesAsync (gh-#486)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWriteIfVersionMatchesHappyPath(DatabaseFixture db)
    {
        [Fact]
        public async Task ExpectedVersionZeroInsertsANewKeyAtVersionOne()
        {
            // Given no prior row for this key...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When it is written with expectedVersion 0 ("I read no row")...
            var newVersion = await repo.WriteIfVersionMatchesAsync(
                "Tts:Pronunciations", "[]", 0, CancellationToken.None);

            // Then the row lands at version 1.
            Assert.Equal(1, newVersion);
            var row = await ReadRowAsync(db, "Tts:Pronunciations");
            Assert.Equal("\"[]\"", row.Value);
        }

        [Fact]
        public async Task TheMatchingCurrentVersionUpdatesAndAdvancesTheVersion()
        {
            // Given a row at version 1...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteIfVersionMatchesAsync("Tts:Pronunciations", "[\"a\"]", 0, CancellationToken.None);

            // When it is written again with the version that row is actually at...
            var newVersion = await repo.WriteIfVersionMatchesAsync(
                "Tts:Pronunciations", "[\"b\"]", 1, CancellationToken.None);

            // Then the write lands, the new value is stored, and the version advances.
            Assert.Equal(2, newVersion);
            var row = await ReadRowAsync(db, "Tts:Pronunciations");
            Assert.Equal("\"[\\\"b\\\"]\"", row.Value);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — WriteIfVersionMatchesAsync loses the race (gh-#486, the T144 probe)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioWriteIfVersionMatchesConflict(DatabaseFixture db)
    {
        [Fact]
        public async Task ExpectedVersionZeroAgainstAnAlreadyExistingRowIsAConflict()
        {
            // Given a row already exists (another writer created it since the caller's own read
            // saw nothing)...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteIfVersionMatchesAsync("Tts:Pronunciations", "[\"a\"]", 0, CancellationToken.None);

            // When a second caller, still believing no row exists, also writes with expectedVersion 0...
            var result = await repo.WriteIfVersionMatchesAsync(
                "Tts:Pronunciations", "[\"b\"]", 0, CancellationToken.None);

            // Then the write is refused (null), and the FIRST writer's value survives untouched —
            // never silently clobbered.
            Assert.Null(result);
            var row = await ReadRowAsync(db, "Tts:Pronunciations");
            Assert.Equal("\"[\\\"a\\\"]\"", row.Value);
        }

        [Fact]
        public async Task AStaleExpectedVersionIsAConflictAndTheConcurrentEditSurvives()
        {
            // Given a row at version 1 that a SECOND writer then advances to version 2, unseen by
            // the first writer's own stale read (the exact "DELETE || PUT both 2xx" T144 probe:
            // one editor's revision must never silently vanish under another's) ...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteIfVersionMatchesAsync("Tts:Pronunciations", "[\"a\"]", 0, CancellationToken.None);
            await repo.WriteIfVersionMatchesAsync("Tts:Pronunciations", "[\"b\"]", 1, CancellationToken.None);

            // When the first writer's own write finally lands, still expecting version 1...
            var result = await repo.WriteIfVersionMatchesAsync(
                "Tts:Pronunciations", "[\"c\"]", 1, CancellationToken.None);

            // Then it is refused (null), and the SECOND writer's revision survives — a lost
            // revision is reported (409, at the caller), never a silent overwrite.
            Assert.Null(result);
            var row = await ReadRowAsync(db, "Tts:Pronunciations");
            Assert.Equal("\"[\\\"b\\\"]\"", row.Value);
        }

        [Fact]
        public async Task ANonZeroExpectedVersionAgainstAMissingRowIsAConflict()
        {
            // Given no row at all for this key (perhaps it was never written, or was written and
            // this repository has no delete path to remove it) ...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When a write claims a specific prior version that cannot possibly be right...
            var result = await repo.WriteIfVersionMatchesAsync(
                "Tts:Pronunciations", "[\"a\"]", 5, CancellationToken.None);

            // Then the write is refused (null) — never fabricates a row out of a bogus version claim.
            Assert.Null(result);
            Assert.Equal(0, await CountRowsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ReadVersionsAsync (gh-#486)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadVersions(DatabaseFixture db)
    {
        [Fact]
        public async Task AnEmptyTableReadsAsAnEmptyDictionary()
        {
            // Given no rows at all...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When every version is read...
            var versions = await repo.ReadVersionsAsync(CancellationToken.None);

            // Then nothing comes back.
            Assert.Empty(versions);
        }

        [Fact]
        public async Task AFreshlyWrittenKeyReadsAtVersionOne()
        {
            // Given a key written once...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);

            // When every version is read...
            var versions = await repo.ReadVersionsAsync(CancellationToken.None);

            // Then it reads at version 1.
            Assert.Equal(1, versions["Loudness:TargetLufs"]);
        }

        [Fact]
        public async Task KeysAreLookedUpCaseInsensitively()
        {
            // Given a row stored under its canonical casing...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteAsync("Station:Theme", "midnight", CancellationToken.None);

            // When every version is read...
            var versions = await repo.ReadVersionsAsync(CancellationToken.None);

            // Then a differently-cased lookup still finds it.
            Assert.True(versions.TryGetValue("station:theme", out var version));
            Assert.Equal(1, version);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ReadAllAsync (F19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadAll(DatabaseFixture db)
    {
        [Fact]
        public async Task AnEmptyTableReadsAsAnEmptyDictionary()
        {
            // Given no rows at all...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When every row is read...
            var rows = await repo.ReadAllAsync(CancellationToken.None);

            // Then nothing comes back.
            Assert.Empty(rows);
        }

        [Fact]
        public async Task EveryRowComesBackKeyedByItsKeyRegardlessOfAllowlistMembership()
        {
            // Given several rows, including one an allowlist filter would reject elsewhere —
            // this repository has no allowlist notion of its own, it returns every row it finds.
            await db.ResetSettingsAsync();
            await InsertRawRowAsync(db, "Loudness:TargetLufs", "-14");
            await InsertRawRowAsync(db, "Station:Theme", "\"midnight\"");
            await InsertRawRowAsync(db, "Admin:Password", "\"never-allowlisted-but-still-a-row\"");
            var repo = Repo(db);

            // When every row is read...
            var rows = await repo.ReadAllAsync(CancellationToken.None);

            // Then all three come back, values verbatim as stored.
            Assert.Equal(3, rows.Count);
            Assert.Equal("-14", rows["Loudness:TargetLufs"]);
            Assert.Equal("\"midnight\"", rows["Station:Theme"]);
            Assert.Equal("\"never-allowlisted-but-still-a-row\"", rows["Admin:Password"]);
        }

        [Fact]
        public async Task KeysAreLookedUpCaseInsensitively()
        {
            // Given a row stored under its canonical casing...
            await db.ResetSettingsAsync();
            await InsertRawRowAsync(db, "Station:Theme", "\"midnight\"");
            var repo = Repo(db);

            // When every row is read...
            var rows = await repo.ReadAllAsync(CancellationToken.None);

            // Then a differently-cased lookup still finds it.
            Assert.True(rows.TryGetValue("station:theme", out var value));
            Assert.Equal("\"midnight\"", value);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — write/read round trip (F19)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRoundTrip(DatabaseFixture db)
    {
        [Fact]
        public async Task AWrittenValueIsReadBackByReadAllAsync()
        {
            // Given a fresh table...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When a value is written, then every row is read back...
            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);
            var rows = await repo.ReadAllAsync(CancellationToken.None);

            // Then the written value comes back exactly.
            Assert.Equal(JsonSerializer.Serialize(-14.0), rows["Loudness:TargetLufs"]);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the key column's own constraints have teeth
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioKeyConstraint(DatabaseFixture db)
    {
        [Fact]
        public async Task ANullKeyIsRejectedByTheDatabaseItself()
        {
            // Given no prior rows...
            await db.ResetSettingsAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // When a direct INSERT tries a null key (the primary key)...
            // Then the database itself rejects it — regardless of what the repository would ever write.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into station.settings (key, value) values (null, '\"x\"'::jsonb)"));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ExistsAsync (gh-#406 slice 4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioExists(DatabaseFixture db)
    {
        [Fact]
        public async Task AMissingKeyReportsFalse()
        {
            // Given no row for this key...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When its existence is probed...
            var exists = await repo.ExistsAsync("Internal:BootSeed:SafeLoopCompletedAt", CancellationToken.None);

            // Then it reports absent.
            Assert.False(exists);
        }

        [Fact]
        public async Task AWrittenKeyReportsTrue()
        {
            // Given a row written under this key...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteAsync("Internal:BootSeed:SafeLoopCompletedAt", DateTimeOffset.UtcNow, CancellationToken.None);

            // When its existence is probed...
            var exists = await repo.ExistsAsync("Internal:BootSeed:SafeLoopCompletedAt", CancellationToken.None);

            // Then it reports present.
            Assert.True(exists);
        }

        [Fact]
        public async Task ItOnlyReportsTheExactKeyProbedNotOtherRows()
        {
            // Given a row written under a different key...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);

            // When a different key's existence is probed...
            var exists = await repo.ExistsAsync("Internal:BootSeed:SafeLoopCompletedAt", CancellationToken.None);

            // Then it reports absent.
            Assert.False(exists);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ReadAllForBoot, the sync exception (gh-#406 slice 5)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadAllForBoot(DatabaseFixture db)
    {
        [Fact]
        public async Task AnEmptyTableReadsAsAnEmptyDictionary()
        {
            // Given no rows at all...
            await db.ResetSettingsAsync();
            var repo = Repo(db);

            // When every row is read synchronously...
            var rows = repo.ReadAllForBoot();

            // Then nothing comes back.
            Assert.Empty(rows);
        }

        [Fact]
        public async Task AWrittenValueIsReadBackSynchronously()
        {
            // Given a value written through the async side...
            await db.ResetSettingsAsync();
            var repo = Repo(db);
            await repo.WriteAsync("Loudness:TargetLufs", -14.0, CancellationToken.None);

            // When every row is read synchronously...
            var rows = repo.ReadAllForBoot();

            // Then the written value comes back exactly, same shape ReadAllAsync would return.
            Assert.Equal(JsonSerializer.Serialize(-14.0), rows["Loudness:TargetLufs"]);
        }

        [Fact]
        public async Task KeysAreLookedUpCaseInsensitivelyJustLikeReadAllAsync()
        {
            // Given a row stored under its canonical casing...
            await db.ResetSettingsAsync();
            await InsertRawRowAsync(db, "Station:Theme", "\"midnight\"");
            var repo = Repo(db);

            // When every row is read synchronously...
            var rows = repo.ReadAllForBoot();

            // Then a differently-cased lookup still finds it.
            Assert.True(rows.TryGetValue("station:theme", out var value));
            Assert.Equal("\"midnight\"", value);
        }
    }
}
