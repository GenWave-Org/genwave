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

    // AC3 — talk longer than its time, against today's chat parser (OverLength) and today's ad duration check
    public sealed class ScenarioFitsTheTime : IAsyncLifetime
    {
        // A 10 second chat target allows 150 characters at 15 a second. Each case trips only the length check.
        private const int ChatTarget = 10;

        private static readonly string[] ChatReplies =
        [
            // 0 — 240 characters: 16 seconds
            ChatParity.Reply(AdParity.Words(60), AdParity.Words(60), AdParity.Words(60), AdParity.Words(60)),
            // 1 — 140 characters: under
            ChatParity.Reply(AdParity.Words(35), AdParity.Words(35), AdParity.Words(35), AdParity.Words(35)),
            // 2 — 158 characters with two kept sound tags, 140 without: only the parser's own count (tags kept) is over
            ChatParity.Reply("[laughs] " + AdParity.Words(35), AdParity.Words(35), "[laughs] " + AdParity.Words(35), AdParity.Words(35)),
        ];

        private static readonly Fixtures.AdParityCase[] AdCases =
        [
            // 0 — 750 characters of plain text over a 42 second ceiling: 50 seconds, on text length alone
            new(AdLines(250, 250, 250), AdParity.Request(), AdParity.Heuristic),
            // 1 — short text, but a recorded-before estimate of 20 seconds a line, three lines: 60 seconds
            new(AdLines(40, 40, 40), AdParity.Request(), Measured(20)),
            // 2 — a short ad with only the untrusted estimate
            new(AdLines(40, 40), AdParity.Request(), AdParity.Heuristic),
            // 3 — short text and a recorded-before estimate of 5 seconds a line: 10 seconds
            new(AdLines(40, 40), AdParity.Request(), Measured(5)),
        ];

        private List<bool> legacyChat = [];
        private List<bool> ruleChat = [];
        private List<string?> legacyAd = [];
        private List<bool> ruleAd = [];
        private bool everyCaseTripsOneFamily;

        public Task InitializeAsync()
        {
            var rule = new FitsTimeRule();
            (legacyChat, ruleChat) = ChatParity.LengthVerdicts(rule, ChatReplies, ChatTarget);
            (legacyAd, ruleAd) = AdParity.Verdicts(rule, AdCases);
            everyCaseTripsOneFamily = AdCases.All(c => AdParity.FamiliesTripped(c) <= 1);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private static string AdLines(params int[] lengths) =>
            string.Join('\n', lengths.Select(length => "ANNOUNCER: " + AdParity.Words(length)));

        private static FixedDurationEstimator Measured(int secondsPerLine) =>
            new(TimeSpan.FromSeconds(secondsPerLine), GenWave.Core.Domain.PatterEstimateConfidence.Historical);

        [Fact]
        public void ALongChatIsFlagged() => Assert.Equal([true, false, true], ruleChat);

        [Fact]
        public void TheChatVerdictsAgreeWithTheCrosstalkParser() => Assert.Equal(legacyChat, ruleChat);

        [Fact]
        public void ALongAdIsFlagged() => Assert.Equal([true, true, false, false], ruleAd);

        [Fact]
        public void TheAdVerdictsAgreeWithTheDurationCheck() =>
            Assert.Equal(legacyAd.Select(id => id == GenWave.Ads.AdScriptRuleIds.Duration).ToList(), ruleAd);

        [Fact]
        public void TodaysValidatorRefusesTheAdsOnlyForTheirLength() =>
            Assert.Equal([GenWave.Ads.AdScriptRuleIds.Duration, GenWave.Ads.AdScriptRuleIds.Duration, null, null], legacyAd);

        [Fact]
        public void NoAdCaseTripsAnotherRule() => Assert.True(everyCaseTripsOneFamily);
    }

    // AC4 — an ad naming a blocklisted brand, against today's brand check
    public sealed class ScenarioOtherBrands : IAsyncLifetime
    {
        private static readonly Fixtures.AdParityCase[] Cases =
        [
            // 0 — a real brand by name
            Case("ANNOUNCER: Our pie beats anything Pepsi can offer."),
            // 1 — a real brand with a separator, which the fold sees through
            Case("ANNOUNCER: Wash it down with Coca-Cola today."),
            // 2 — nothing on the list
            Case("ANNOUNCER: Come in for a hot breakfast at Cravin's Diner."),
        ];

        private List<string?> legacy = [];
        private List<bool> rule = [];
        private RuleFlag? flag;
        private bool everyCaseTripsOneFamily;

        public Task InitializeAsync()
        {
            var judge = new OtherBrandsRule();
            (legacy, rule) = AdParity.Verdicts(judge, Cases);
            flag = judge.Judge(AdParity.Snapshot(Cases[0].Reply, Cases[0].Request, Cases[0].Estimator));
            everyCaseTripsOneFamily = Cases.All(c => AdParity.FamiliesTripped(c) <= 1);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private static Fixtures.AdParityCase Case(string reply) => new(reply, AdParity.Request(), AdParity.Heuristic);

        [Fact]
        public void ItIsFlagged() => Assert.Equal("pepsi", flag?.Evidence);

        [Fact]
        public void BrandsAreFlaggedAndAPlainAdIsNot() => Assert.Equal([true, true, false], rule);

        [Fact]
        public void ItAgreesWithTheBrandCheck() =>
            Assert.Equal(legacy.Select(id => id == GenWave.Ads.AdScriptRuleIds.BrandCollision).ToList(), rule);

        [Fact]
        public void NoCaseTripsAnotherRule() => Assert.True(everyCaseTripsOneFamily);
    }

    // AC5 — audience Everyone and an ad with a listed word, against today's audience check
    public sealed class ScenarioCleanLanguage : IAsyncLifetime
    {
        private static readonly Fixtures.AdParityCase[] Cases =
        [
            // 0 — a listed word under Everyone
            Case("ANNOUNCER: This coffee is shit hot.", GenWave.Core.Domain.AudiencePosture.Everyone),
            // 1 — nothing listed
            Case("ANNOUNCER: This coffee is piping hot.", GenWave.Core.Domain.AudiencePosture.Everyone),
            // 2 — the listed word is only part of a longer word
            Case("ANNOUNCER: Pass the glass and the class.", GenWave.Core.Domain.AudiencePosture.Everyone),
            // 3 — the same listed word under Mature
            Case("ANNOUNCER: This coffee is shit hot.", GenWave.Core.Domain.AudiencePosture.Mature),
        ];

        private List<string?> legacy = [];
        private List<bool> rule = [];
        private RuleFlag? flag;
        private bool everyCaseTripsOneFamily;

        public Task InitializeAsync()
        {
            var judge = new CleanLanguageRule();
            (legacy, rule) = AdParity.Verdicts(judge, Cases);
            flag = judge.Judge(AdParity.Snapshot(Cases[0].Reply, Cases[0].Request, Cases[0].Estimator));
            everyCaseTripsOneFamily = Cases.All(c => AdParity.FamiliesTripped(c) <= 1);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private static Fixtures.AdParityCase Case(string reply, GenWave.Core.Domain.AudiencePosture posture) =>
            new(reply, AdParity.Request(posture), AdParity.Heuristic);

        [Fact]
        public void ItIsFlagged() => Assert.Equal("shit", flag?.Evidence);

        [Fact]
        public void OnlyAListedWholeWordUnderEveryoneIsFlagged() => Assert.Equal([true, false, false, false], rule);

        [Fact]
        public void ItAgreesWithTheAudienceCheck() =>
            Assert.Equal(legacy.Select(id => id == GenWave.Ads.AdScriptRuleIds.AudiencePosture).ToList(), rule);

        [Fact]
        public void NoCaseTripsAnotherRule() => Assert.True(everyCaseTripsOneFamily);
    }

    // AC6 — a 7-digit number that is not the sponsor's and has no 555, against today's phone check
    public sealed class ScenarioPhoneNumber : IAsyncLifetime
    {
        private static readonly Fixtures.AdParityCase[] Cases =
        [
            // 0 — a wrong number with no 555, the sponsor's number on file
            new("ANNOUNCER: Call us at 222-0199 today.", AdParity.Request(), AdParity.Heuristic),
            // 1 — a wrong number that has 555 in it, the sponsor's number on file: still wrong
            new("ANNOUNCER: Call us at 555-0142 today.", AdParity.Request(), AdParity.Heuristic),
            // 2 — a number with no 555 and no sponsor number on file
            new("ANNOUNCER: Call us at 222-0199 today.", AdParity.Request(phone: null), AdParity.Heuristic),
            // 3 — a number with 555 and no sponsor number on file
            new("ANNOUNCER: Call us at 555-0142 today.", AdParity.Request(phone: null), AdParity.Heuristic),
            // 4 — a pack-owned sponsor gets no number skip, even for its own number
            new("ANNOUNCER: Call us at 222-0100 today.", AdParity.Request(packOwned: true), AdParity.Heuristic),
            // 5 — digits that are not phone-shaped
            new("ANNOUNCER: Open since 1998 with 4 hot tables.", AdParity.Request(), AdParity.Heuristic),
        ];

        private List<string?> legacy = [];
        private List<bool> rule = [];
        private RuleFlag? flag;
        private bool everyCaseTripsOneFamily;

        public Task InitializeAsync()
        {
            var judge = new PhoneNumberRule();
            (legacy, rule) = AdParity.Verdicts(judge, Cases);
            flag = judge.Judge(AdParity.Snapshot(Cases[0].Reply, Cases[0].Request, Cases[0].Estimator));
            everyCaseTripsOneFamily = Cases.All(c => AdParity.FamiliesTripped(c) <= 1);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsFlagged() => Assert.Equal("222-0199", flag?.Evidence);

        [Fact]
        public void TheAskNamesTheSponsorsNumber() => Assert.Contains(AdParity.SponsorPhone, flag?.Ask, StringComparison.Ordinal);

        [Fact]
        public void EveryWrongNumberIsFlagged() => Assert.Equal([true, true, true, false, true, false], rule);

        [Fact]
        public void ItAgreesWithThePhoneCheck() =>
            Assert.Equal(legacy.Select(id => id == GenWave.Ads.AdScriptRuleIds.PhoneShape).ToList(), rule);

        [Fact]
        public void NoCaseTripsAnotherRule() => Assert.True(everyCaseTripsOneFamily);
    }

    // Sad path
    // AC7 — the sponsor's own name is on the brand list, not pack-owned
    public sealed class ScenarioTheSponsorsOwnName : IAsyncLifetime
    {
        private static readonly Fixtures.AdParityCase[] Cases =
        [
            // 0 — an owner sponsor named like a listed brand says its own name
            Case("ANNOUNCER: Visit Coca Cola today.", packOwned: false),
            // 1 — the same name from a pack-owned sponsor gets no exemption
            Case("ANNOUNCER: Visit Coca Cola today.", packOwned: true),
            // 2 — the owner sponsor's name plus a different listed brand
            Case("ANNOUNCER: Visit Coca Cola, not Pepsi.", packOwned: false),
        ];

        private List<string?> legacy = [];
        private List<bool> rule = [];
        private bool everyCaseTripsOneFamily;

        public Task InitializeAsync()
        {
            (legacy, rule) = AdParity.Verdicts(new OtherBrandsRule(), Cases);
            everyCaseTripsOneFamily = Cases.All(c => AdParity.FamiliesTripped(c) <= 1);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private static Fixtures.AdParityCase Case(string reply, bool packOwned) =>
            new(reply, new GenWave.Ads.AdScriptValidationRequest(
                GenWave.Core.Domain.AudiencePosture.Everyone, 400, 30, 0.4, "Coca Cola", AdParity.SponsorPhone, packOwned),
                AdParity.Heuristic);

        [Fact]
        public void ItIsNotFlagged() => Assert.False(rule[0]);

        [Fact]
        public void APackOwnedSponsorIsStillFlagged() => Assert.True(rule[1]);

        [Fact]
        public void AnotherBrandIsStillFlagged() => Assert.True(rule[2]);

        [Fact]
        public void ItAgreesWithTheBrandCheck() =>
            Assert.Equal(legacy.Select(id => id == GenWave.Ads.AdScriptRuleIds.BrandCollision).ToList(), rule);

        [Fact]
        public void NoCaseTripsAnotherRule() => Assert.True(everyCaseTripsOneFamily);
    }

    // Sad path
    // AC8 — the ad says the sponsor's phone
    public sealed class ScenarioTheSponsorsNumber : IAsyncLifetime
    {
        private static readonly Fixtures.AdParityCase[] Cases =
        [
            // 0 — the sponsor's number as stored
            new("ANNOUNCER: Call us at 222-0100 today.", AdParity.Request(), AdParity.Heuristic),
            // 1 — the same digits written with a space
            new("ANNOUNCER: Call us at 222 0100 today.", AdParity.Request(), AdParity.Heuristic),
        ];

        private List<string?> legacy = [];
        private List<bool> rule = [];
        private bool everyCaseTripsOneFamily;

        public Task InitializeAsync()
        {
            (legacy, rule) = AdParity.Verdicts(new PhoneNumberRule(), Cases);
            everyCaseTripsOneFamily = Cases.All(c => AdParity.FamiliesTripped(c) <= 1);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsNotFlagged() => Assert.False(rule[0]);

        [Fact]
        public void TheSameDigitsWrittenDifferentlyAreNotFlagged() => Assert.False(rule[1]);

        [Fact]
        public void TodaysPhoneCheckAcceptsThemToo() => Assert.Equal([null, null], legacy);

        [Fact]
        public void NoCaseTripsAnotherRule() => Assert.True(everyCaseTripsOneFamily);
    }

    // Sad path
    // AC9 — a wrong number in the reply
    public sealed class ScenarioAWrongNumberIsNotRewritten : IAsyncLifetime
    {
        private const string Reply = "ANNOUNCER: Call us at 222-0199 today.";

        private TalkSnapshot snapshot = TalkSnapshots.DjLine(string.Empty);
        private string rewrittenToday = string.Empty;

        public Task InitializeAsync()
        {
            snapshot = AdParity.Snapshot(Reply, AdParity.Request(), AdParity.Heuristic);
            rewrittenToday = GenWave.Tts.AdScriptWriter.ApplyPhoneHygiene(
                GenWave.Tts.AdScriptWriter.ApplyLineAwareHygiene(Reply), AdParity.SponsorPhone);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheCleanedTextStillHoldsThatNumber() => Assert.Contains("222-0199", snapshot.Lines[0].Text, StringComparison.Ordinal);

        [Fact]
        public void TodaysRewriteWouldHaveChangedIt() => Assert.DoesNotContain("222-0199", rewrittenToday, StringComparison.Ordinal);
    }
}
