// STORY-550 — Every ask leaves a record, the store half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture; add [Collection(DatabaseCollection.Name)] on activation).
// Activated by T733 (store half); the migration facts landed with T730.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

/// <summary>Shared seeding for the callback store scenarios: one persona, rows planted with ages the database clock reads.</summary>
sealed class CallbackStoreWorld(DatabaseFixture db)
{
    public ICallbackStore Store { get; } = new CallbackStore(new Lazy<Npgsql.NpgsqlDataSource>(() => db.StationDataSource));
    public int Persona { get; private set; }

    public async Task ResetAsync()
    {
        await db.ResetStationAsync();
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        Persona = await conn.QuerySingleAsync<int>("insert into station.persona (name) values ('Flip') returning id");
    }

    public async Task<int> MemoryAsync(string kind = "lore", string source = "authored", string? approvedAt = null, bool forgotten = false,
        TimeSpan? usedAgo = null, int airedCount = 0)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            """
            insert into station.persona_memory (persona_id, kind, content, source, aired_count, last_aired_at, approved_at, forgotten_at)
            values (@Persona, @kind, 'm', @source, @airedCount,
                    case when @usedSeconds::float8 is null then null else now() - make_interval(secs => @usedSeconds::float8) end,
                    case when @approvedAt then now() end, case when @forgotten then now() end)
            returning id
            """,
            new { Persona, kind, source, airedCount, usedSeconds = usedAgo?.TotalSeconds, approvedAt = approvedAt is not null, forgotten });
    }

    public async Task AskedAsync(int memory, TimeSpan ago, bool used = false)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into station.memory_offer (memory_id, persona_id, offered_at, used) values (@memory, @Persona, now() - make_interval(secs => @secs), @used)",
            new { memory, Persona, secs = ago.TotalSeconds, used });
    }

    public async Task<(int AiredCount, DateTime? LastAired)> MemoryCountersAsync(int memory)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(int, DateTime?)>(
            "select aired_count, last_aired_at from station.persona_memory where id = @memory", new { memory });
    }

    public async Task<(bool Used, int? UtteranceId, string[] Words)> OfferAsync(long offer)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(bool, int?, string[])>(
            "select used, utterance_id, matched_words from station.memory_offer where id = @offer", new { offer });
    }

    public async Task<int> UtteranceAsync()
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "insert into station.utterance (persona_id, kind, segment_kind, shift_key, text) values (@Persona, 'patter', 'LeadIn', 'a', 't') returning id",
            new { Persona });
    }

    public async Task<int> OfferCountAsync()
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<int>("select count(*)::int from station.memory_offer");
    }
}

public static class FeatureEveryAskLeavesARecordStore
{
    // AC1 — written before the model call
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheOfferRow(DatabaseFixture db) : IAsyncLifetime
    {
        // When  OfferAsync runs
        readonly CallbackStoreWorld world = new(db);

        public Task InitializeAsync() => world.ResetAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task AMemoryOfferRowExistsWithUsedFalseAndNoUtterance()
        {
            var memory = await world.MemoryAsync();

            var offer = await world.Store.OfferAsync(memory, world.Persona, CancellationToken.None);

            var (used, utterance, words) = await world.OfferAsync(offer);
            Assert.False(used);
            Assert.Null(utterance);
            Assert.Empty(words);
        }
    }

    // AC4 — use starts the rest
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUseStartsTheRest(DatabaseFixture db) : IAsyncLifetime
    {
        // Given an offer settled as used
        readonly CallbackStoreWorld world = new(db);
        int memory;
        long offer;
        int utterance;

        public async Task InitializeAsync()
        {
            await world.ResetAsync();
            memory = await world.MemoryAsync();
            utterance = await world.UtteranceAsync();
            offer = await world.Store.OfferAsync(memory, world.Persona, CancellationToken.None);
            await world.Store.SettleAsync(offer, utterance, true, ["kettle"], CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TheMemorysAiredCountIsOneHigherAndLastAiredIsSet()
        {
            var (count, last) = await world.MemoryCountersAsync(memory);
            Assert.Equal(1, count);
            Assert.NotNull(last);

            var (used, utteranceId, words) = await world.OfferAsync(offer);
            Assert.True(used);
            Assert.Equal(utterance, utteranceId);
            Assert.Equal(["kettle"], words);
        }

        [Fact]
        public async Task ASecondSettleOfTheSameOfferDoesNotCountAgain()
        {
            await world.Store.SettleAsync(offer, utterance, true, ["kettle"], CancellationToken.None);

            Assert.Equal(1, (await world.MemoryCountersAsync(memory)).AiredCount);
        }
    }

    // AC5 — a miss leaves the memory alone
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAMissLeavesTheMemoryAlone(DatabaseFixture db) : IAsyncLifetime
    {
        // Given an offer settled as not used
        readonly CallbackStoreWorld world = new(db);
        int memory;
        long offer;

        public async Task InitializeAsync()
        {
            await world.ResetAsync();
            memory = await world.MemoryAsync();
            offer = await world.Store.OfferAsync(memory, world.Persona, CancellationToken.None);
            await world.Store.SettleAsync(offer, null, false, [], CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TheMemorysAiredCountAndLastAiredAreUnchanged()
        {
            var (count, last) = await world.MemoryCountersAsync(memory);
            Assert.Equal(0, count);
            Assert.Null(last);
            Assert.False((await world.OfferAsync(offer)).Used);
        }

        [Fact]
        public async Task TheAskStillCountsAsTheLastAskButNotTheLastUse()
        {
            var history = await world.Store.LastOfferAsync(world.Persona, CancellationToken.None);

            Assert.NotNull(history.SinceLastAsk);
            Assert.Null(history.SinceLastUsed);
        }
    }

    // AC6 — the migration resets old counts
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheMigrationResetsOldCounts(DatabaseFixture db) : IAsyncLifetime
    {
        // Given pre-db/52 counts on lore, episode and life rows and a quirk spend row
        // When  db/52 runs (the table does not exist yet, as on an upgrading box)

        // Postgres stores microseconds; truncate so the round trip compares equal.
        static readonly DateTime LastAired = Truncate(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-2), DateTimeKind.Utc));

        static DateTime Truncate(DateTime value) => new(value.Ticks - value.Ticks % 10, value.Kind);

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("drop table if exists station.memory_offer cascade");
            // The once-only branch fires when the claim key is absent, as on a box that has not run db/52 yet.
            await conn.ExecuteAsync("delete from station.one_time_fix where key = 'f218-callback-counts-reset'");
            var persona = await conn.QuerySingleAsync<int>("insert into station.persona (name) values ('Flip') returning id");
            await conn.ExecuteAsync(
                """
                insert into station.persona_memory (persona_id, kind, content, source, aired_count, last_aired_at)
                values (@persona, 'lore', 'a', 'authored', 4, @lastAired),
                       (@persona, 'episode', 'b', 'extracted', 2, @lastAired),
                       (@persona, 'life', 'c', 'extracted', 1, @lastAired),
                       (@persona, 'quirk', 'd', 'authored', 3, @lastAired)
                """, new { persona, lastAired = LastAired });
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "52-memory-offer-migration.sh"));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        async Task<(int AiredCount, DateTime? LastAired)> ReadAsync(string kind)
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            return await conn.QuerySingleAsync<(int, DateTime?)>(
                "select aired_count, last_aired_at from station.persona_memory where kind = @kind", new { kind });
        }

        [Fact]
        public async Task LoreEpisodeAndLifeRowsHaveAiredCount0AndNoLastAired()
        {
            foreach (var kind in new[] { "lore", "episode", "life" })
            {
                var (count, last) = await ReadAsync(kind);
                Assert.Equal(0, count);
                Assert.Null(last);
            }
        }

        [Fact]
        public async Task TheQuirkRowIsUnchanged()
        {
            var (count, last) = await ReadAsync("quirk");
            Assert.Equal(3, count);
            Assert.Equal(LastAired, last);
        }

        [Fact]
        public async Task ARerunDoesNotResetCountsAgain()
        {
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update station.persona_memory set aired_count = 5, last_aired_at = now() where kind = 'lore'");

            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "52-memory-offer-migration.sh"));

            var (count, last) = await ReadAsync("lore");
            Assert.Equal(5, count);
            Assert.NotNull(last);
        }
    }

    // AC6 — a raw `docker compose up` runs db/06 alone, so db/06 must claim the reset key itself on a fresh DB
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshInstallClaimsTheResetKey(DatabaseFixture db) : IAsyncLifetime
    {
        const string Key = "f218-callback-counts-reset";

        public async Task InitializeAsync()
        {
            await db.ResetStationAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("delete from station.one_time_fix where key = @Key", new { Key });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        async Task<bool> KeyClaimedAsync()
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            return await conn.QuerySingleAsync<bool>("select exists (select 1 from station.one_time_fix where key = @Key)", new { Key });
        }

        [Fact]
        public async Task Db06WithNoCallbackCountsLeavesTheKeyClaimed()
        {
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "06-station-settings-migration.sh"));

            Assert.True(await KeyClaimedAsync());
        }

        [Fact]
        public async Task Db06WhileALoreRowHoldsAnOldCountLeavesTheKeyUnclaimedAndDb52ResetsIt()
        {
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                var persona = await conn.QuerySingleAsync<int>("insert into station.persona (name) values ('Flip') returning id");
                await conn.ExecuteAsync(
                    "insert into station.persona_memory (persona_id, kind, content, source, aired_count) values (@persona, 'lore', 'a', 'authored', 4)",
                    new { persona });
            }

            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "06-station-settings-migration.sh"));
            Assert.False(await KeyClaimedAsync());

            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "52-memory-offer-migration.sh"));

            await using var check = await db.StationDataSource.OpenConnectionAsync();
            Assert.Equal(0, await check.QuerySingleAsync<int>("select aired_count from station.persona_memory where kind = 'lore'"));
            Assert.True(await KeyClaimedAsync());
        }
    }

    // AC8 — 28 days (sad path)
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTwentyEightDays(DatabaseFixture db) : IAsyncLifetime
    {
        // Given an offer 29 days old
        // When  the next offer is written
        readonly CallbackStoreWorld world = new(db);
        int memory;

        public async Task InitializeAsync()
        {
            await world.ResetAsync();
            memory = await world.MemoryAsync();
            await world.AskedAsync(memory, TimeSpan.FromDays(29));
            await world.AskedAsync(memory, TimeSpan.FromDays(27));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task The29DayOldOfferIsGone()
        {
            await world.Store.OfferAsync(memory, world.Persona, CancellationToken.None);

            Assert.Equal(2, await world.OfferCountAsync());
        }
    }


    // STORY-549 AC4, AC5, AC8 — the store's eligibility query
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEligibilityInSQL(DatabaseFixture db) : IAsyncLifetime
    {
        readonly CallbackStoreWorld world = new(db);

        public Task InitializeAsync() => world.ResetAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        async Task<IReadOnlyList<long>> EligibleIdsAsync() =>
            (await world.Store.EligibleAsync(world.Persona, CancellationToken.None)).Select(m => m.Id).ToList();

        [Fact]
        public async Task LoreUsed5HoursAgoIsNotEligible()
        {
            var lore = await world.MemoryAsync(usedAgo: TimeSpan.FromHours(5), airedCount: 1);

            Assert.DoesNotContain(lore, await EligibleIdsAsync());
        }

        [Fact]
        public async Task LoreUsed7HoursAgoIsEligible()
        {
            var lore = await world.MemoryAsync(usedAgo: TimeSpan.FromHours(7), airedCount: 1);

            Assert.Contains(lore, await EligibleIdsAsync());
        }

        [Theory]
        [InlineData("episode")]
        [InlineData("life")]
        public async Task EpisodeAndLifeRestFor48Hours(string kind)
        {
            var at47 = await world.MemoryAsync(kind, "extracted", usedAgo: TimeSpan.FromHours(47), airedCount: 1);
            var at49 = await world.MemoryAsync(kind, "extracted", usedAgo: TimeSpan.FromHours(49), airedCount: 1);

            var eligible = await EligibleIdsAsync();

            Assert.DoesNotContain(at47, eligible);
            Assert.Contains(at49, eligible);
        }

        [Fact]
        public async Task AMemoryAsked90MinutesAgoAndNotUsedIsNotEligible()
        {
            var memory = await world.MemoryAsync();
            await world.AskedAsync(memory, TimeSpan.FromMinutes(90));

            Assert.DoesNotContain(memory, await EligibleIdsAsync());
        }

        [Fact]
        public async Task AMemoryAsked3HoursAgoAndNotUsedIsEligible()
        {
            var memory = await world.MemoryAsync();
            await world.AskedAsync(memory, TimeSpan.FromHours(3));

            Assert.Contains(memory, await EligibleIdsAsync());
        }

        [Fact]
        public async Task AnUnapprovedDraftAndAForgottenRowAreNotEligible()
        {
            var draft = await world.MemoryAsync("episode", "drafted");
            var forgotten = await world.MemoryAsync(forgotten: true);
            var approved = await world.MemoryAsync("episode", "drafted", approvedAt: "now");

            var eligible = await EligibleIdsAsync();

            Assert.DoesNotContain(draft, eligible);
            Assert.DoesNotContain(forgotten, eligible);
            Assert.Contains(approved, eligible);
        }

        [Fact]
        public async Task QuirksAreNeverEligible()
        {
            var quirk = await world.MemoryAsync("quirk");

            Assert.DoesNotContain(quirk, await EligibleIdsAsync());
        }

        [Fact]
        public async Task NeverOfferedRowsComeFirst()
        {
            var askedLongAgo = await world.MemoryAsync();
            var neverOffered = await world.MemoryAsync();
            var askedRecently = await world.MemoryAsync();
            await world.AskedAsync(askedLongAgo, TimeSpan.FromDays(3));
            await world.AskedAsync(askedRecently, TimeSpan.FromHours(5));

            Assert.Equal([neverOffered, askedLongAgo, askedRecently], await EligibleIdsAsync());
        }
    }
}
