// STORY-571 — Switch a rule per kind of talk (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

using GenWave.Core.TalkRules;
using GenWave.Host.Tests.Support;
using GenWave.MediaLibrary.Station;
using GenWave.TalkRules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSwitchARulePerKindOfTalk
{
    // AC1 — GET /api/talk-rules through the running host
    public sealed class ScenarioTheRulesAreListed
    {
        [Fact(Skip = "pending: T803")]
        public void EachRuleHasItsId() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasItsName() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasWhatItDoes() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasWhy() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasAnExample() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasTheTalkItCanCover() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasASwitchPerKind() { }

        [Fact(Skip = "pending: T803")]
        public void EachRuleHasLastWeeksFlagsPerKind() { }
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

    // AC3, AC4 — PUT /api/talk-rules/repeat-phrases/chat {enabled: false}
    public sealed class ScenarioARuleIsSwitchedOffForChats
    {
        [Fact(Skip = "pending: T803")]
        public void TheNextChatAttemptRecordsTheRuleAsOff() { }

        [Fact(Skip = "pending: T803")]
        public void DjLinesAreStillJudgedByIt() { }

        [Fact(Skip = "pending: T803")]
        public void NoRestartIsNeeded() { }
    }

    // AC5 — GET /api/talk-rules/{id}/flags?days=7
    public sealed class ScenarioFlaggedLinesAreRequested
    {
        [Fact(Skip = "pending: T803")]
        public void TheLatestTwentyComeBack() { }

        [Fact(Skip = "pending: T803")]
        public void EachHasWhenDjKindWordsAndOutcome() { }
    }

    // AC6 — a rule declared in the test assembly
    public sealed class ScenarioAOneFileRule
    {
        [Fact(Skip = "pending: T803")]
        public void TheApiListsIt() { }
    }

    // Sad path
    // AC7 — PUT /api/talk-rules/phone-number/dj-line {enabled: true}
    public sealed class ScenarioAKindTheRuleCannotCover
    {
        [Fact(Skip = "pending: T803")]
        public void ItIsRejectedWith400() { }
    }

    // Sad path
    // AC8 — PUT /api/talk-rules/nope/chat
    public sealed class ScenarioAnUnknownRule
    {
        [Fact(Skip = "pending: T803")]
        public void ItReturns404() { }
    }

    // Sad path
    // AC9 — no admin cookie
    public sealed class ScenarioNoAdminCookie
    {
        [Fact(Skip = "pending: T803")]
        public void TheListReturns401() { }

        [Fact(Skip = "pending: T803")]
        public void TheSwitchReturns401() { }

        [Fact(Skip = "pending: T803")]
        public void TheFlagsReturn401() { }
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
}
