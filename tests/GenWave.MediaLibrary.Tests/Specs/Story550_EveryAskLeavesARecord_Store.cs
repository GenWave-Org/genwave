// STORY-550 — Every ask leaves a record, the store half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture; add [Collection(DatabaseCollection.Name)] on activation).
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEveryAskLeavesARecordStore
{
    // AC1 — written before the model call
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheOfferRow
    {
        // When  OfferAsync runs

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void AMemoryOfferRowExistsWithUsedFalseAndNoUtterance() => Assert.Fail("pending: T733 — a memory offer row exists with used false and no utterance");
    }

    // AC4 — use starts the rest
    [Trait("Category", "Integration")]
    public sealed class ScenarioUseStartsTheRest
    {
        // Given an offer settled as used

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void TheMemorysAiredCountIsOneHigherAndLastAiredIsSet() => Assert.Fail("pending: T733 — the memorys aired count is one higher and last aired is set");
    }

    // AC5 — a miss leaves the memory alone
    [Trait("Category", "Integration")]
    public sealed class ScenarioAMissLeavesTheMemoryAlone
    {
        // Given an offer settled as not used

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void TheMemorysAiredCountAndLastAiredAreUnchanged() => Assert.Fail("pending: T733 — the memorys aired count and last aired are unchanged");
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
    [Trait("Category", "Integration")]
    public sealed class ScenarioTwentyEightDays
    {
        // Given an offer 29 days old
        // When  the next offer is written

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void The29DayOldOfferIsGone() => Assert.Fail("pending: T733 — the 29 day old offer is gone");
    }

    // STORY-549 AC4, AC5, AC8 — the store's eligibility query
    [Trait("Category", "Integration")]
    public sealed class ScenarioEligibilityInSQL
    {

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void LoreUsed5HoursAgoIsNotEligible() => Assert.Fail("pending: T733 — lore used 5 hours ago is not eligible");

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void AMemoryAsked90MinutesAgoAndNotUsedIsNotEligible() => Assert.Fail("pending: T733 — a memory asked 90 minutes ago and not used is not eligible");

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void AnUnapprovedDraftAndAForgottenRowAreNotEligible() => Assert.Fail("pending: T733 — an unapproved draft and a forgotten row are not eligible");

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void NeverOfferedRowsComeFirst() => Assert.Fail("pending: T733 — never offered rows come first");
    }
}
