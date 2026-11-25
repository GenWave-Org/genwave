// STORY-571 — Switch a rule per kind of talk (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GenWave.Core.TalkRules;
using GenWave.Host.Tests.Support;
using GenWave.MediaLibrary.Station;
using GenWave.TalkRules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSwitchARulePerKindOfTalk
{
    // The booted host with the Postgres stores pointed at a real station database. The host's own registrations point at no
    // database, so the two store registrations are repeated here and win (last registration resolves; the switch store uses Replace).
    // Everything above the stores stays the real container: the controller, the switchboard and the rule book.
    [Trait("Category", "Integration")]
    public abstract class RulesApiOverPostgres : EngineSettingScenario
    {
        // A field initializer runs before the base constructor that boots the host, so the database is up when ConfigureServices runs.
        readonly EphemeralStationDatabase database = Story571Database.Start();

        protected string StationConnectionString => database.StationConnectionString;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddTalkCheckLog(database.StationConnectionString, new ConfigurationBuilder().Build());
            services.AddTalkRuleSwitchStore(database.StationConnectionString);
            services.RemoveAll<IHostedService>(); // the drain service would otherwise race the rows a spec writes by hand
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();
            await database.DisposeAsync();
        }

        protected Task AppendAsync(TalkCheckRecord record) => Service<ITalkCheckStore>().AppendAsync(record, CancellationToken.None);

        protected async Task<long> InsertPersonaAsync(string name)
        {
            await using var connection = new NpgsqlConnection(database.StationConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("insert into station.persona (name) values (@name) returning id", connection);
            command.Parameters.AddWithValue("name", name);
            return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    static readonly RuleFlag RepeatFlag = new("Repeats itself", "Say something new.", "golden hour groove");

    static TalkCheckRecord Row(
        TalkKinds kind,
        RuleVerdict verdict,
        long? personaId = null,
        TalkOutcome outcome = TalkOutcome.ReAsked,
        TalkSnapshot? snapshot = null,
        TalkCheckMode mode = TalkCheckMode.Rules) =>
        new(
            personaId,
            mode,
            "raw",
            "cleaned",
            snapshot ?? ChatSnapshot() with { Kind = kind },
            new TalkList<RuleVerdict>([verdict]),
            null,
            outcome);

    // AC1 — GET /api/talk-rules through the running host. The rows: repeat-phrases flagged on 2 chats (rules mode) and 1 chat (shadow mode, a real verdict too) and 1 DJ line; not counted are a
    // judge that could not run, a rule that was switched off, a verdict that is only a broken shaper, and an unflagged chat.
    public sealed class ScenarioTheRulesAreListed : RulesApiOverPostgres
    {
        const string RuleId = "repeat-phrases";

        JsonElement listed;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, RepeatFlag)));
            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, RepeatFlag)));
            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, RepeatFlag), mode: TalkCheckMode.Shadow));
            await AppendAsync(Row(TalkKinds.DjLine, new RuleVerdict(RuleId, true, RepeatFlag)));
            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, new RuleFlag(TalkRuleReasons.CouldNotCheck, "Again.", "boom"))));
            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, false, RepeatFlag)));
            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, null, Broken: TalkRuleReasons.CouldNotShape)));
            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, null)));
            using var body = JsonDocument.Parse(await Client.GetStringAsync("/api/talk-rules"));
            listed = body.RootElement.Clone();
        }

        JsonElement Repeat => listed.EnumerateArray().Single(rule => rule.GetProperty("id").GetString() == RuleId);

        IEnumerable<string?> Strings(string property) => listed.EnumerateArray().Select(rule => rule.GetProperty(property).GetString());

        IReadOnlyList<ITalkRule> Book => Service<ITalkRuleBook>().Rules;

        // The host formats numbers in the invariant culture ("15 %"), so the expected text is described under it too.
        IReadOnlyList<string> Described(Func<TalkRuleDescription, string> pick)
        {
            var before = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                return [.. Book.Select(rule => pick(rule.Describe()))];
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = before;
            }
        }

        [Fact]
        public void EachRuleHasItsId() => Assert.Equal(Book.Select(rule => rule.Id), Strings("id"));

        [Fact]
        public void EachRuleHasItsName() => Assert.Equal(Described(description => description.Name), Strings("name"));

        [Fact]
        public void EachRuleHasWhatItDoes() => Assert.Equal(Described(description => description.WhatItDoes), Strings("whatItDoes"));

        [Fact]
        public void EachRuleHasWhy() => Assert.Equal(Described(description => description.Why), Strings("why"));

        [Fact]
        public void EachRuleHasAnExample() => Assert.Equal(Described(description => description.Example), Strings("example"));

        [Fact]
        public void EachRuleHasTheTalkItCanCover() =>
            Assert.Equal(["dj-line", "chat"], Repeat.GetProperty("canCover").EnumerateArray().Select(kind => kind.GetString()));

        [Fact]
        public void EachRuleHasASwitchPerKind() =>
            Assert.Equal(
                [("dj-line", true), ("chat", true)],
                Repeat.GetProperty("switches").EnumerateObject().Select(kind => (kind.Name, kind.Value.GetBoolean())));

        [Fact]
        public void EachRuleHasLastWeeksFlagsPerKind() =>
            Assert.Equal(
                [("dj-line", 1), ("chat", 3)],
                Repeat.GetProperty("flags7d").EnumerateObject().Select(kind => (kind.Name, kind.Value.GetInt32())));
    }

    // AC2 — no switch row
    // The host's registration extension (AddTalkRuleSwitchStore) over a bare ServiceCollection, not the composed host, on a real station database: the Postgres store, no row, the rule book.
    [Trait("Category", "Integration")]
    public sealed class ScenarioNoSwitchRow : IAsyncLifetime
    {
        EphemeralStationDatabase? database;
        TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync()
        {
            database = await Story571Database.StartAsync();
            var services = new ServiceCollection().AddTalkRuleSwitchStore(database.StationConnectionString);
            await using var provider = services.BuildServiceProvider();
            var book = new TalkRuleBook(
                [new DefaultsOnlyForChatRule()], provider.GetRequiredService<ITalkRuleSwitches>(), NullLogger<TalkRuleBook>.Instance);
            verdicts = await book.JudgeAsync(ChatSnapshot(), CancellationToken.None);
        }

        public async Task DisposeAsync()
        {
            if (database is not null)
            {
                await database.DisposeAsync();
            }
        }

        [Fact]
        public void TheSwitchShowsTheRulesDefault() => Assert.True(Assert.Single(verdicts).SwitchedOn);
    }

    static TalkSnapshot ChatSnapshot() =>
        new(
            TalkKinds.Chat,
            "Crosstalk",
            1,
            new TalkList<TalkLine>([new TalkLine("HOST", "Hello.")]),
            new TalkList<TalkSpeaker>([new TalkSpeaker(1, "Flip", TalkList<string>.Empty)]),
            null,
            null,
            new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(-6)),
            "America/Edmonton",
            null,
            new TalkStation("GenWave", "friendly"),
            TalkList<TalkRecentLine>.Empty,
            null,
            TalkMemos.Empty);

    sealed class DefaultsOnlyForChatRule : IJudgingTalkRule
    {
        public string Id => "defaults-only-for-chat";

        public TalkKinds CanCover => TalkKinds.DjLine | TalkKinds.Chat;

        public TalkKinds OnByDefault => TalkKinds.Chat;

        public TalkRuleDescription Describe() => new("Defaults", "Never flags.", "A fixture.", "Any line.");

        public RuleFlag? Judge(TalkSnapshot after) => null;
    }

    // AC3, AC4 — PUT /api/talk-rules/repeat-phrases/chat {enabled: false}, then the real rule book judges a chat and a DJ line on the same host.
    public sealed class ScenarioARuleIsSwitchedOffForChats : RulesApiOverPostgres
    {
        HttpStatusCode status;
        bool chatBefore;
        TalkList<RuleVerdict> chat = TalkList<RuleVerdict>.Empty;
        TalkList<RuleVerdict> djLine = TalkList<RuleVerdict>.Empty;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            var book = Service<ITalkRuleBook>();
            chatBefore = Repeat(await book.JudgeAsync(ChatSnapshot(), CancellationToken.None)).SwitchedOn;
            var response = await Client.PutAsJsonAsync("/api/talk-rules/repeat-phrases/chat", new { enabled = false });
            status = response.StatusCode;
            chat = await book.JudgeAsync(ChatSnapshot(), CancellationToken.None);
            djLine = await book.JudgeAsync(ChatSnapshot() with { Kind = TalkKinds.DjLine }, CancellationToken.None);
        }

        static RuleVerdict Repeat(TalkList<RuleVerdict> verdicts) => Assert.Single(verdicts, verdict => verdict.RuleId == "repeat-phrases");

        [Fact]
        public void TheSwitchIsSaved() => Assert.Equal(HttpStatusCode.OK, status);

        [Fact]
        public void TheNextChatAttemptRecordsTheRuleAsOff() => Assert.False(Repeat(chat).SwitchedOn);

        [Fact]
        public void DjLinesAreStillJudgedByIt() => Assert.True(Repeat(djLine).SwitchedOn);

        // AC4: the same booted host judged the chat on before the PUT; with TheNextChatAttemptRecordsTheRuleAsOff
        // (off after it) that shows nothing restarted.
        [Fact]
        public void TheRuleWasOnBeforeTheSwitch() => Assert.True(chatBefore);
    }

    // AC5 — GET /api/talk-rules/fits-time/flags?days=7 (a rule that covers chats and ads). 24 flagged chats by Flip ("words 1".."words 24"),
    // a flagged chat from shadow mode, then a flagged ad (newest, Failed: a still-flagged ad cannot be re-asked, F220.14);
    // one chat whose judge could not run is not a flag.
    public sealed class ScenarioFlaggedLinesAreRequested : RulesApiOverPostgres
    {
        const string RuleId = "fits-time";

        JsonElement[] flags = [];

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            var persona = await InsertPersonaAsync("Flip");
            for (var number = 1; number <= 24; number++)
            {
                await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, new RuleFlag("Repeats itself", "Say something new.", $"words {number}")), persona));
            }

            await AppendAsync(Row(TalkKinds.Chat, new RuleVerdict(RuleId, true, new RuleFlag(TalkRuleReasons.CouldNotCheck, "Again.", "boom")), persona));
            await AppendAsync(Row(
                TalkKinds.Chat, new RuleVerdict(RuleId, true, new RuleFlag("Repeats itself", "Say something new.", "shadow words")), persona, mode: TalkCheckMode.Shadow));
            var ad = ChatSnapshot() with
            {
                Kind = TalkKinds.Ad,
                Ad = new TalkAdFields("Cravin's Diner", null, 30, false, 0.4, 450, 15, TalkList<double>.Empty),
            };
            await AppendAsync(Row(
                TalkKinds.Ad, new RuleVerdict(RuleId, true, new RuleFlag("Repeats itself", "Say something new.", "words 25")), null, TalkOutcome.Failed, ad));
            using var body = JsonDocument.Parse(await Client.GetStringAsync($"/api/talk-rules/{RuleId}/flags?days=7"));
            flags = [.. body.RootElement.EnumerateArray().Select(item => item.Clone())];
        }

        [Fact]
        public void TheLatestTwentyComeBack() => Assert.Equal(20, flags.Length);

        [Fact]
        public void TheNewestComesFirst() =>
            Assert.Equal(
                ["words 25", "shadow words", .. Enumerable.Range(7, 18).Reverse().Select(number => $"words {number}")],
                flags.Select(flag => flag.GetProperty("words").GetString()));

        [Fact]
        public void AShadowModeFlagIsListed() => Assert.Equal("shadow words", flags[1].GetProperty("words").GetString());

        [Fact]
        public void TheLatestFlaggedChatHasWhen() =>
            Assert.True(DateTimeOffset.UtcNow - flags[2].GetProperty("when").GetDateTimeOffset() < TimeSpan.FromMinutes(10));

        [Fact]
        public void TheLatestFlaggedChatHasItsDj() => Assert.Equal("Flip", flags[2].GetProperty("dj").GetString());

        [Fact]
        public void TheLatestFlaggedChatHasItsKind() => Assert.Equal("chat", flags[2].GetProperty("kind").GetString());

        [Fact]
        public void TheLatestFlaggedChatHasItsWords() => Assert.Equal("words 24", flags[2].GetProperty("words").GetString());

        [Fact]
        public void TheLatestFlaggedChatHasItsOutcome() => Assert.Equal("re-asked", flags[2].GetProperty("outcome").GetString());

        [Fact]
        public void AStillFlaggedAdIsFailed() => Assert.Equal("failed", flags[0].GetProperty("outcome").GetString());

        [Fact]
        public void AnAdNamesItsSponsorForTheDj() => Assert.Equal("Cravin's Diner", flags[0].GetProperty("dj").GetString());

        [Fact]
        public void WhenIsUtc() => Assert.Equal(TimeSpan.Zero, flags[0].GetProperty("when").GetDateTimeOffset().Offset);
    }

    // AC6 — a rule declared in this test assembly, found by the same scan production uses
    public sealed class ScenarioAOneFileRule : EngineSettingScenario
    {
        JsonElement listed;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddTalkRules(typeof(OneFileRule).Assembly);
            services.RemoveAll<ITalkRuleSwitches>();
            services.AddSingleton<ITalkRuleSwitches, InMemoryTalkRuleSwitches>();
            services.RemoveAll<ITalkFlagReader>();
            services.AddSingleton<ITalkFlagReader, NoFlags>();
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            using var body = JsonDocument.Parse(await Client.GetStringAsync("/api/talk-rules"));
            listed = body.RootElement.Clone();
        }

        [Fact]
        public void TheApiListsIt() =>
            Assert.Contains(listed.EnumerateArray(), rule => rule.GetProperty("id").GetString() == "one-file-fixture");
    }

    // AC2 through the list — no switch row: the list shows the book's effective state. The fixture rule is on for chat by default and off for DJ lines.
    public sealed class ScenarioTheListShowsTheDefaults : EngineSettingScenario
    {
        JsonElement switchesShown;

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ITalkRule, DefaultsOnlyForChatRule>();
            services.RemoveAll<ITalkRuleSwitches>();
            services.AddSingleton<ITalkRuleSwitches, InMemoryTalkRuleSwitches>();
            services.RemoveAll<ITalkFlagReader>();
            services.AddSingleton<ITalkFlagReader, NoFlags>();
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            using var body = JsonDocument.Parse(await Client.GetStringAsync("/api/talk-rules"));
            switchesShown = body.RootElement.EnumerateArray()
                .Single(rule => rule.GetProperty("id").GetString() == "defaults-only-for-chat")
                .GetProperty("switches").Clone();
        }

        [Fact]
        public void ARuleDefaultOffForDjLinesReadsOff() => Assert.False(switchesShown.GetProperty("dj-line").GetBoolean());

        [Fact]
        public void ARuleDefaultOnForChatReadsOn() => Assert.True(switchesShown.GetProperty("chat").GetBoolean());
    }

    // The rules API reads no database here: nothing is flagged.
    sealed class NoFlags : ITalkFlagReader
    {
        public Task<IReadOnlyList<TalkRuleFlagCount>> CountFlagsAsync(DateTimeOffset since, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TalkRuleFlagCount>>([]);

        public Task<IReadOnlyList<TalkFlaggedAttempt>> LatestFlagsAsync(string ruleId, DateTimeOffset since, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TalkFlaggedAttempt>>([]);
    }

    // The one file a rule takes (F220.2): a judge that never flags.
    public sealed class OneFileRule : IJudgingTalkRule
    {
        public string Id => "one-file-fixture";

        public TalkKinds CanCover => TalkKinds.Chat;

        public TalkKinds OnByDefault => TalkKinds.Chat;

        public TalkRuleDescription Describe() => new("One file", "Never flags.", "A fixture.", "Any line.");

        public RuleFlag? Judge(TalkSnapshot after) => null;
    }

    // Sad path
    // AC7 — PUT /api/talk-rules/phone-number/dj-line {enabled: true}: phone-number covers ads only. 400 comes before any store is touched.
    public sealed class ScenarioAKindTheRuleCannotCover : EngineSettingScenario
    {
        HttpStatusCode status;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            status = (await Client.PutAsJsonAsync("/api/talk-rules/phone-number/dj-line", new { enabled = true })).StatusCode;
        }

        [Fact]
        public void ItIsRejectedWith400() => Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // Sad path
    // AC8 — PUT /api/talk-rules/nope/chat
    public sealed class ScenarioAnUnknownRule : EngineSettingScenario
    {
        HttpStatusCode status;
        HttpStatusCode flagsStatus;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            status = (await Client.PutAsJsonAsync("/api/talk-rules/nope/chat", new { enabled = true })).StatusCode;
            flagsStatus = (await Client.GetAsync("/api/talk-rules/nope/flags")).StatusCode;
        }

        [Fact]
        public void ItReturns404() => Assert.Equal(HttpStatusCode.NotFound, status);

        [Fact]
        public void ItsFlagsReturn404() => Assert.Equal(HttpStatusCode.NotFound, flagsStatus);
    }

    // Sad path — what the routes refuse before any store is touched
    public sealed class ScenarioARequestTheRoutesRefuse : EngineSettingScenario
    {
        HttpStatusCode unknownKind;
        HttpStatusCode noBody;
        HttpStatusCode daysOutOfRange;
        HttpStatusCode daysBelowRange;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            unknownKind = (await Client.PutAsJsonAsync("/api/talk-rules/repeat-phrases/podcast", new { enabled = true })).StatusCode;
            noBody = (await Client.PutAsJsonAsync("/api/talk-rules/repeat-phrases/chat", new { })).StatusCode;
            daysOutOfRange = (await Client.GetAsync("/api/talk-rules/repeat-phrases/flags?days=91")).StatusCode;
            daysBelowRange = (await Client.GetAsync("/api/talk-rules/repeat-phrases/flags?days=0")).StatusCode;
        }

        [Fact]
        public void AnUnknownKindIs400() => Assert.Equal(HttpStatusCode.BadRequest, unknownKind);

        [Fact]
        public void ABodyWithoutEnabledIs400() => Assert.Equal(HttpStatusCode.BadRequest, noBody);

        [Fact]
        public void DaysOutOfRangeIs400() => Assert.Equal(HttpStatusCode.BadRequest, daysOutOfRange);

        [Fact]
        public void ZeroDaysIs400() => Assert.Equal(HttpStatusCode.BadRequest, daysBelowRange);
    }

    // Sad path
    // AC9 — no admin cookie
    public sealed class ScenarioNoAdminCookie : EngineSettingScenario
    {
        // The base fixture logs in; this scenario never does.
        public override Task InitializeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TheListReturns401() =>
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/talk-rules")).StatusCode);

        [Fact]
        public async Task TheSwitchReturns401() =>
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PutAsJsonAsync("/api/talk-rules/repeat-phrases/chat", new { enabled = false })).StatusCode);

        [Fact]
        public async Task TheFlagsReturn401() =>
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/talk-rules/repeat-phrases/flags?days=7")).StatusCode);
    }

    // Wiring — the Postgres store beats the in-memory stand-in in the host's real order (Program.cs: store first, then
    // AddTalkRules). The repository is internal to MediaLibrary, so the resolved type is pinned by its assembly.
    public abstract class ScenarioTheStoreBeatsTheStandIn : IDisposable
    {
        readonly ServiceProvider provider;
        readonly int descriptors;

        protected ScenarioTheStoreBeatsTheStandIn(Func<IServiceCollection, IServiceCollection> register)
        {
            var services = register(new ServiceCollection());
            descriptors = services.Count(descriptor => descriptor.ServiceType == typeof(ITalkRuleSwitches));
            provider = services.BuildServiceProvider();
        }

        public void Dispose() => provider.Dispose();

        [Fact]
        public void TheStoreIsWhatResolves() =>
            Assert.Same(
                typeof(TalkRuleSwitchServiceCollectionExtensions).Assembly,
                provider.GetRequiredService<ITalkRuleSwitches>().GetType().Assembly);

        [Fact]
        public void ExactlyOneRegistrationRemains() => Assert.Equal(1, descriptors);
    }

    public sealed class ScenarioTheStoreIsRegisteredFirst() : ScenarioTheStoreBeatsTheStandIn(
        services => services.AddTalkRuleSwitchStore("Host=localhost").AddTalkRules());

    public sealed class ScenarioTheStoreIsRegisteredAfter() : ScenarioTheStoreBeatsTheStandIn(
        services => services.AddTalkRules().AddTalkRuleSwitchStore("Host=localhost"));
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness.</summary>
file sealed class Story571Database : EphemeralStationDatabase
{
    Story571Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story571Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t799");
        var db = new Story571Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }

    /// <summary>For a fixture whose host boots in its constructor, before any async setup can run.</summary>
    public static Story571Database Start() => StartAsync().GetAwaiter().GetResult();
}
