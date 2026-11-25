// STORY-571 — A switch is honoured per rule per kind, and refused where the rule cannot cover (SPEC F220.22, F220.23 · PLAN T799)
// Fixture-level spec over fixture rules and the in-memory stand-in (the Postgres store is specified in MediaLibrary.Tests).

using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureTheSwitchboardAndTheBook
{
    static TalkRuleBook Book(ITalkRuleSwitches switches) =>
        new([new AnyKindRepeatsFixtureRule()], switches, new CapturingLogger<TalkRuleBook>());

    static TalkRuleSwitchboard Switchboard(ITalkRuleSwitches switches) =>
        new([new AnyKindRepeatsFixtureRule(), new FactsFixtureRule()], switches);

    static TalkSnapshot Chat() => TalkSnapshots.Chat("As I said before.", "Sure.");

    static TalkSnapshot DjLine() => TalkSnapshots.DjLine("As I said before.");

    public sealed class ScenarioARuleIsSwitchedOffForChats : IAsyncLifetime
    {
        TalkList<RuleVerdict> chatBefore = TalkList<RuleVerdict>.Empty;
        TalkList<RuleVerdict> chatAfter = TalkList<RuleVerdict>.Empty;
        TalkList<RuleVerdict> djLineAfter = TalkList<RuleVerdict>.Empty;
        TalkRuleSwitchResult result;

        public async Task InitializeAsync()
        {
            var switches = new InMemoryTalkRuleSwitches();
            var book = Book(switches);
            chatBefore = await book.JudgeAsync(Chat(), CancellationToken.None);
            result = await Switchboard(switches).SwitchAsync("fixture-any-repeats", TalkKinds.Chat, false, CancellationToken.None);
            chatAfter = await book.JudgeAsync(Chat(), CancellationToken.None);
            djLineAfter = await book.JudgeAsync(DjLine(), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheSwitchIsStored() => Assert.Equal(TalkRuleSwitchResult.Stored, result);

        [Fact]
        public void TheChatVerdictWasOnBefore() => Assert.True(Assert.Single(chatBefore).SwitchedOn);

        [Fact]
        public void TheNextChatVerdictRecordsTheRuleAsOffOnTheSameBook() => Assert.False(Assert.Single(chatAfter).SwitchedOn);

        [Fact]
        public void TheOffVerdictStillCarriesItsFlag() => Assert.NotNull(Assert.Single(chatAfter).Flag);

        [Fact]
        public void DjLinesAreStillJudgedByIt() => Assert.True(Assert.Single(djLineAfter).SwitchedOn);
    }

    public sealed class ScenarioARowForARuleThatWasRemoved : IAsyncLifetime
    {
        TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync()
        {
            var switches = new InMemoryTalkRuleSwitches();
            await switches.SetAsync("long-gone", TalkKinds.Chat, true, CancellationToken.None);
            verdicts = await Book(switches).JudgeAsync(Chat(), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsIgnored() => Assert.Equal("fixture-any-repeats", Assert.Single(verdicts).RuleId);
    }

    // Sad path — F220.22
    public sealed class ScenarioAKindTheRuleCannotCover : IAsyncLifetime
    {
        TalkRuleSwitchResult switchedOn;
        TalkRuleSwitchResult switchedOff;
        IReadOnlyDictionary<string, bool> stored = new Dictionary<string, bool>();

        public async Task InitializeAsync()
        {
            var switches = new InMemoryTalkRuleSwitches();
            var board = Switchboard(switches);
            switchedOn = await board.SwitchAsync("fixture-facts", TalkKinds.Chat, true, CancellationToken.None);
            switchedOff = await board.SwitchAsync("fixture-facts", TalkKinds.Chat, false, CancellationToken.None);
            stored = await switches.GetAllAsync(TalkKinds.Chat, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void SwitchingItOnIsRefused() => Assert.Equal(TalkRuleSwitchResult.KindNotCovered, switchedOn);

        [Fact]
        public void SwitchingItOffIsRefusedToo() => Assert.Equal(TalkRuleSwitchResult.KindNotCovered, switchedOff);

        [Fact]
        public void NothingIsStored() => Assert.Empty(stored);
    }

    // Sad path — not one single kind
    public sealed class ScenarioNotOneSingleKind : IAsyncLifetime
    {
        TalkRuleSwitchResult none;
        TalkRuleSwitchResult several;

        public async Task InitializeAsync()
        {
            var board = Switchboard(new InMemoryTalkRuleSwitches());
            none = await board.SwitchAsync("fixture-any-repeats", TalkKinds.None, true, CancellationToken.None);
            several = await board.SwitchAsync("fixture-any-repeats", TalkKinds.Chat | TalkKinds.Ad, true, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoKindIsRefused() => Assert.Equal(TalkRuleSwitchResult.KindNotCovered, none);

        [Fact]
        public void SeveralKindsAreRefused() => Assert.Equal(TalkRuleSwitchResult.KindNotCovered, several);
    }

    // Sad path — F220.22 (404 in T803)
    public sealed class ScenarioAnUnknownRule : IAsyncLifetime
    {
        TalkRuleSwitchResult result;
        IReadOnlyDictionary<string, bool> stored = new Dictionary<string, bool>();

        public async Task InitializeAsync()
        {
            var switches = new InMemoryTalkRuleSwitches();
            result = await Switchboard(switches).SwitchAsync("nope", TalkKinds.Chat, true, CancellationToken.None);
            stored = await switches.GetAllAsync(TalkKinds.Chat, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsReportedAsUnknown() => Assert.Equal(TalkRuleSwitchResult.UnknownRule, result);

        [Fact]
        public void NothingIsStored() => Assert.Empty(stored);
    }
}
