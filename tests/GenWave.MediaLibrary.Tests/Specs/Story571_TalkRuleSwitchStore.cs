// STORY-571 — The switch store (SPEC F220.22, F220.23 · PLAN T799)
//
// BDD specification — xUnit, REAL Postgres via the shared DatabaseFixture (the behaviour under test IS the
// upsert and the cache cleared after it commits).

using Dapper;
using GenWave.Core.TalkRules;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTalkRuleSwitchStore
{
    static TalkRuleSwitchRepository Repository(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static async Task ResetAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("truncate table station.talk_rule_switch");
    }

    static async Task<IReadOnlyList<(string RuleId, string TalkKind, bool Enabled)>> RowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<(string, string, bool)>(
            "select rule_id, talk_kind, enabled from station.talk_rule_switch order by rule_id, talk_kind");
        return rows.ToList();
    }

    // AC2 — no switch row
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNoSwitchRow(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyDictionary<string, bool>? stored;

        IReadOnlyDictionary<string, bool> switches => stored ?? throw new InvalidOperationException("InitializeAsync did not run.");

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            stored = await Repository(db).GetAllAsync(TalkKinds.Chat, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsStoredSoTheRulesDefaultApplies() => Assert.Empty(switches);
    }

    // AC3 — a rule is switched off for chats
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARuleIsSwitchedOffForChats(DatabaseFixture db) : IAsyncLifetime
    {
        IReadOnlyDictionary<string, bool>? chats;
        IReadOnlyDictionary<string, bool>? djLines;
        IReadOnlyList<(string RuleId, string TalkKind, bool Enabled)>? rows;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            await Repository(db).SetAsync("repeat-phrases", TalkKinds.Chat, false, CancellationToken.None);
            var fresh = Repository(db);
            chats = await fresh.GetAllAsync(TalkKinds.Chat, CancellationToken.None);
            djLines = await fresh.GetAllAsync(TalkKinds.DjLine, CancellationToken.None);
            rows = await RowsAsync(db);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItReadsBackOffForChatsAfterARestart() =>
            Assert.False((chats ?? throw new InvalidOperationException("InitializeAsync did not run."))["repeat-phrases"]);

        [Fact]
        public void DjLinesAreUntouched() =>
            Assert.Empty(djLines ?? throw new InvalidOperationException("InitializeAsync did not run."));

        [Fact]
        public void OneRowIsStored() =>
            Assert.Single(rows ?? throw new InvalidOperationException("InitializeAsync did not run."));
    }

    // AC4 — no restart: the same repository sees the change on the next read
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASwitchChangesWithNoRestart(DatabaseFixture db) : IAsyncLifetime
    {
        bool? beforeChange;
        bool? afterChange;
        bool? afterChangeBack;
        int rowCount;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var repo = Repository(db);
            await repo.SetAsync("repeat-phrases", TalkKinds.Chat, true, CancellationToken.None);
            beforeChange = (await repo.GetAllAsync(TalkKinds.Chat, CancellationToken.None))["repeat-phrases"];
            await repo.SetAsync("repeat-phrases", TalkKinds.Chat, false, CancellationToken.None);
            afterChange = (await repo.GetAllAsync(TalkKinds.Chat, CancellationToken.None))["repeat-phrases"];
            await repo.SetAsync("repeat-phrases", TalkKinds.Chat, true, CancellationToken.None);
            afterChangeBack = (await repo.GetAllAsync(TalkKinds.Chat, CancellationToken.None))["repeat-phrases"];
            rowCount = (await RowsAsync(db)).Count;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFirstReadSeesOn() => Assert.True(beforeChange);

        [Fact]
        public void TheNextReadSeesOff() => Assert.False(afterChange);

        [Fact]
        public void TheReadAfterThatSeesOnAgain() => Assert.True(afterChangeBack);

        [Fact]
        public void SettingTwiceKeepsOneRow() => Assert.Equal(1, rowCount);
    }

    // AC4 — a change made behind the cache's back is not seen until a write goes through it (the cache is the point)
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAReadIsServedFromTheCache(DatabaseFixture db) : IAsyncLifetime
    {
        int servedCount;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var repo = Repository(db);
            await repo.GetAllAsync(TalkKinds.Chat, CancellationToken.None);
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync("insert into station.talk_rule_switch (rule_id, talk_kind, enabled) values ('repeat-phrases', 'chat', false)");
            }

            servedCount = (await repo.GetAllAsync(TalkKinds.Chat, CancellationToken.None)).Count;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheDatabaseIsNotReadAgain() => Assert.Equal(0, servedCount);
    }

    // Sad path — the write fails
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheWriteFails(DatabaseFixture db) : IAsyncLifetime
    {
        Exception? thrown;
        IReadOnlyDictionary<string, bool>? served;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            var repo = Repository(db);
            await repo.SetAsync("repeat-phrases", TalkKinds.Chat, false, CancellationToken.None);
            await repo.GetAllAsync(TalkKinds.Chat, CancellationToken.None);
            // A rule id the table's check refuses.
            thrown = await Record.ExceptionAsync(async () => await repo.SetAsync("Not A Slug", TalkKinds.Chat, true, CancellationToken.None));
            served = await repo.GetAllAsync(TalkKinds.Chat, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheErrorReachesTheCaller() => Assert.IsType<PostgresException>(thrown);

        /// <summary>
        /// Proves the last good state is served after the failed write, not that the cache was kept: the table is
        /// unchanged either way, so a reload would serve the same answer.
        /// </summary>
        [Fact]
        public void TheLastGoodStateIsStillServed() =>
            Assert.False((served ?? throw new InvalidOperationException("InitializeAsync did not run."))["repeat-phrases"]);
    }

    // Sad path — a kind that is not one stored kind
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAKindWithNoSingleToken(DatabaseFixture db) : IAsyncLifetime
    {
        Exception? thrown;

        public async Task InitializeAsync()
        {
            await ResetAsync(db);
            thrown = await Record.ExceptionAsync(async () =>
                await Repository(db).SetAsync("repeat-phrases", TalkKinds.None, true, CancellationToken.None));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsRefused() => Assert.IsType<ArgumentException>(thrown);
    }

    // Wiring — the store lands after an earlier ITalkRuleSwitches registration (MediaLibrary cannot see AddTalkRules; the
    // production order, store first then AddTalkRules, is covered in Host.Tests' FeatureSwitchARulePerKindOfTalk)
    public sealed class ScenarioTheStoreIsRegisteredAfterAnEarlierRegistration : IDisposable
    {
        readonly ServiceProvider provider;
        readonly int descriptors;

        public ScenarioTheStoreIsRegisteredAfterAnEarlierRegistration()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITalkRuleSwitches>(new NoopSwitches());
            services.AddTalkRuleSwitchStore("Host=localhost");
            descriptors = services.Count(descriptor => descriptor.ServiceType == typeof(ITalkRuleSwitches));
            provider = services.BuildServiceProvider();
        }

        public void Dispose() => provider.Dispose();

        [Fact]
        public void TheStoreIsWhatResolves() =>
            Assert.IsType<TalkRuleSwitchRepository>(provider.GetRequiredService<ITalkRuleSwitches>());

        [Fact]
        public void ExactlyOneRegistrationRemains() => Assert.Equal(1, descriptors);

        sealed class NoopSwitches : ITalkRuleSwitches
        {
            public ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(TalkKinds kind, CancellationToken ct) =>
                ValueTask.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());

            public ValueTask SetAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct) => ValueTask.CompletedTask;
        }
    }
}
