// STORY-569 — Chat and ad rules moved (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Facts still carrying a Skip are activated by the task their Skip names.

using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.TalkRules.Tests.Fixtures;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureChatAndAdRulesMoved
{
    // AC1 — a chat line with a frequency, a call sign or a date claim, against today's crosstalk parser
    public sealed class ScenarioStationDetails : IAsyncLifetime
    {
        private static readonly string[] Replies =
        [
            ChatParity.Reply("You are listening to 98.7 FM.", "Yes.", "Indeed."),
            ChatParity.Reply("This is KXYZ.", "Yes.", "Indeed."),
            ChatParity.Reply("It is August 20 today.", "Yes.", "Indeed."),
            ChatParity.Reply("It is 2026.", "Yes.", "Indeed."),
            ChatParity.Reply("The fifth is the 5th of June.", "Yes.", "Indeed."),
            ChatParity.Reply("You are on 98.7", "[laughs]", "FM."),
            ChatParity.Reply("Welcome back.", "Good to be here.", "Indeed."),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];
        private RuleFlag? frequency;
        private RuleFlag? callSign;
        private RuleFlag? dateClaim;

        public Task InitializeAsync()
        {
            var rule = new StationDetailsRule();
            (legacyFlags, ruleFlags) = ChatParity.Verdicts(rule, Replies);
            frequency = rule.Judge(TalkSnapshots.Chat("You are listening to 98.7 FM.", "Yes."));
            callSign = rule.Judge(TalkSnapshots.Chat("This is KXYZ.", "Yes."));
            dateClaim = rule.Judge(TalkSnapshots.Chat("It is August 20 today.", "Yes."));
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItAgreesWithTheCrosstalkParser() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void EveryDetailIsFlaggedEvenWithALoneSoundTagBetweenItsWords() =>
            Assert.Equal([true, true, true, true, true, true, false], ruleFlags);

        [Fact]
        public void AFrequencyIsFlagged() => Assert.Equal("98.7 FM", frequency?.Evidence);

        [Fact]
        public void ACallSignIsFlagged() => Assert.Equal("KXYZ", callSign?.Evidence);

        [Fact]
        public void ADateClaimIsFlagged() => Assert.Equal("August 20", dateClaim?.Evidence);
    }

    // AC1 sad path — a chat with no station detail, and talk that is not a chat
    public sealed class ScenarioNoStationDetails : IAsyncLifetime
    {
        private static readonly string[] Replies =
        [
            ChatParity.Reply("Welcome back.", "Good to be here.", "Indeed."),
            ChatParity.Reply("Welcome back.", "[laughs] Good to be here.", "Indeed."),
            ChatParity.Reply("Tune in to GenWave.", "Always.", "Indeed."),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];
        private RuleFlag? plainChat;
        private RuleFlag? ownNameChat;
        private RuleFlag? djLine;

        public Task InitializeAsync()
        {
            var rule = new StationDetailsRule();
            (legacyFlags, ruleFlags) = ChatParity.Verdicts(rule, Replies);
            plainChat = rule.Judge(TalkSnapshots.Chat("Welcome back.", "Good to be here."));
            var named = TalkSnapshots.Chat("You are listening to Radio 98.7 FM.", "Yes.");
            ownNameChat = rule.Judge(named with { Station = new TalkStation("Radio 98.7 FM", "friendly") });
            djLine = rule.Judge(TalkSnapshots.DjLine("You are listening to 98.7 FM."));
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItAgreesWithTheCrosstalkParser() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void AnOrdinaryChatIsNotFlagged() => Assert.Null(plainChat);

        [Fact]
        public void TheStationsOwnNameIsNotFlagged() => Assert.Null(ownNameChat);

        [Fact]
        public void ADjLineIsNotJudged() => Assert.Null(djLine);
    }

    // AC2 — a chat line sharing 5 words with that speaker's last 10 chat lines, against today's per-speaker repeat check
    public sealed class ScenarioChatRepeats : IAsyncLifetime
    {
        private const string Said = "We have a really lovely long set coming up next";
        private const string Repeat = "We have a really lovely long set coming up for you";

        private static readonly RecentSaid[] None = [];

        private static readonly ChatRepeatCase[] Cases =
        [
            // 0 — the host repeats the host's own history
            new(["Hello there.", "Hi.", Repeat, "Indeed."], [new(Said)], None),
            // 1 — the same words sit in the neighbor's history, so the host line is not a repeat
            new(["Hello there.", "Hi.", Repeat, "Indeed."], None, [new(Said)]),
            // 2 — four words in a row
            new(["Hello there.", "Hi.", "A really lovely long song to play.", "Indeed."], [new(Said)], None),
            // 3 — the neighbor repeats the neighbor's own history
            new(["Hello there.", Repeat, "Indeed.", "Yes."], None, [new(Said)]),
            // 4 — the repeat is in a template line only
            new(["Hello there.", "Hi.", Repeat, "Indeed."], [new(Said, Fallback: true)], None),
            // 5 — nothing said before
            new(["Hello there.", "Hi.", Repeat, "Indeed."], None, None),
        ];

        private List<string> legacyNamed = [];
        private List<string> ruleNamed = [];
        private RuleFlag? flag;

        public Task InitializeAsync()
        {
            var rule = new RepeatPhrasesRule(new RepetitionCheck());
            (legacyNamed, ruleNamed) = RepeatParity.ChatVerdicts(rule, Cases);
            flag = rule.Judge(RepeatParity.ChatSnapshot(Cases[0]));
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsFlagged() => Assert.NotNull(flag);

        [Fact]
        public void ItNamesWhatTheCrosstalkRepeatCheckNames() => Assert.Equal(legacyNamed, ruleNamed);

        [Fact]
        public void OnlyTheSpeakersOwnRealLinesCount() => Assert.Equal([true, false, false, true, false, false], ruleNamed.Select(named => named.Length > 0));
    }

    // AC2 — parity: a chat speaker's own tagline is flagged when reused, as today's chat check does (chats have no tagline limiter; Dean 2026-11-23)
    public sealed class ScenarioTheSpeakersTaglineIsChecked : IAsyncLifetime
    {
        private const string Tagline = "Stay golden my friends and keep going";

        private static readonly ChatRepeatCase Case =
            new(["Hello there.", "Hi.", Tagline + ".", "Indeed."], [new(Tagline)], [], Tagline);

        private RuleFlag? ruleFlag;
        private string legacyNamed = string.Empty;
        private string ruleNamed = string.Empty;

        public Task InitializeAsync()
        {
            var rule = new RepeatPhrasesRule(new RepetitionCheck());
            ruleFlag = rule.Judge(RepeatParity.ChatSnapshot(Case));
            var verdicts = RepeatParity.ChatVerdicts(rule, [Case]);
            legacyNamed = verdicts.Legacy[0];
            ruleNamed = verdicts.Rule[0];
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRuleFlagsTheTagline() => Assert.NotNull(ruleFlag);

        [Fact]
        public void TheRuleNamesTheRun() => Assert.Contains("golden", ruleFlag?.Evidence, StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void TodaysChatCheckFlagsItToo() => Assert.NotEqual(string.Empty, legacyNamed);

        [Fact]
        public void TheRuleNamesWhatTodaysChatCheckNames() => Assert.Equal(legacyNamed, ruleNamed);
    }

    // AC3 — talk longer than its time
    public sealed class ScenarioFitsTheTime
    {
        [Fact(Skip = "pending: T786")]
        public void ALongChatIsFlagged() { }

        [Fact(Skip = "pending: T786")]
        public void ALongAdIsFlagged() { }
    }

    // AC4 — an ad naming a blocklisted brand
    public sealed class ScenarioOtherBrands
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsFlagged() { }
    }

    // AC5 — audience Everyone and an ad with a listed word
    public sealed class ScenarioCleanLanguage
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsFlagged() { }
    }

    // AC6 — a 7-digit number that is not the sponsor's and has no 555
    public sealed class ScenarioPhoneNumber
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsFlagged() { }

        [Fact(Skip = "pending: T786")]
        public void TheAskNamesTheSponsorsNumber() { }
    }

    // Sad path
    // AC7 — the sponsor's own name is on the brand list, not pack-owned
    public sealed class ScenarioTheSponsorsOwnName
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsNotFlagged() { }
    }

    // Sad path
    // AC8 — the ad says the sponsor's phone
    public sealed class ScenarioTheSponsorsNumber
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsNotFlagged() { }
    }

    // Sad path
    // AC9 — a wrong number in the reply
    public sealed class ScenarioAWrongNumberIsNotRewritten
    {
        [Fact(Skip = "pending: T786")]
        public void TheCleanedTextStillHoldsThatNumber() { }
    }
}
