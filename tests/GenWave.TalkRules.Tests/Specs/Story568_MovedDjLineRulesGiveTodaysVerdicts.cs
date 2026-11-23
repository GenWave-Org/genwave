// STORY-568 — The moved DJ-line rules give today's verdicts (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Facts still carrying a Skip are activated by the task their Skip names.

using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;
using GenWave.Tts;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureMovedDjLineRulesGiveTodaysVerdicts
{
    // AC1 — the fact fixtures (digits, weekday, condition words), against today's truth gate
    public sealed class ScenarioOfferedFacts : IAsyncLifetime
    {
        private const string Facts = "Sunday, 21 degrees, sunny.";
        private const string SeveralLines = "It is Sunday.\n\n21 degrees.\n   \nSunny skies.";

        private static readonly DjParityCase[] Cases =
        [
            new("It is 25 degrees out.", Facts),
            new("It is 21 degrees out.", Facts),
            new("It's Sunday.", "21 degrees, sunny."),
            new("It's Sunday.", Facts),
            new("Looks like snow today.", Facts),
            new("Looks sunny today.", Facts),
            new("Welcome to GenWave.", Facts),
            new("It is 25 [laughs] degrees out.", Facts),
            new("It is 21 [laughs] degrees out.", Facts),
            new("It is 21 degrees and sunny.", SeveralLines),
            new("It is 22 degrees and sunny.", SeveralLines),
            new("It is 25 degrees and snowing.", null),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];

        public Task InitializeAsync()
        {
            (legacyFlags, ruleFlags) = DjParity.Verdicts(new OfferedFactsRule(), Cases);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItFlagsExactlyTheLinesCheckFactsFlags() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void ItFlagsTheDigitTheWeekdayAndTheWeatherWord() =>
            Assert.Equal([true, false, true, false, true, false, false, true, false, false, true, false], ruleFlags);
    }

    // Sad path
    // AC1 — a DJ line with no facts offered
    public sealed class ScenarioNoFactsOffered : IAsyncLifetime
    {
        private RuleFlag? flag;

        public Task InitializeAsync()
        {
            flag = new OfferedFactsRule().Judge(TalkSnapshots.DjLine("It is 25 degrees and snowing."));
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ADjLineWithNoFactsOfferedIsNotJudged() => Assert.Null(flag);
    }

    // AC1 — a chat holds no facts, so only weather words are checked
    public sealed class ScenarioOfferedFactsInAChat : IAsyncLifetime
    {
        private static readonly string[] Replies =
        [
            ChatParity.Reply("Nice day.", "Lovely and sunny too.", "Sure is."),
            ChatParity.Reply("Nice day.", "Lovely, welcome to GenWave.", "Sure is."),
            ChatParity.Reply("It is 25 degrees.", "Sure is.", "Yes."),
            ChatParity.Reply("Nice day.", "Looks like [laughs] snow.", "Sure is."),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];

        public Task InitializeAsync()
        {
            (legacyFlags, ruleFlags) = ChatParity.Verdicts(new OfferedFactsRule(), Replies);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItAgreesWithTheCrosstalkParser() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void OnlyAWeatherWordIsFlagged() => Assert.Equal([true, false, false, true], ruleFlags);
    }

    // AC2 — the clock fixtures (the clock reads Sunday 12:00), against today's truth gate
    public sealed class ScenarioDayAndTime : IAsyncLifetime
    {
        private static readonly DjParityCase[] Cases =
        [
            new("It's Monday."),
            new("It's Sunday."),
            new("Good morning!"),
            new("Good afternoon!"),
            new("Good night, everyone."),
            new("Welcome to GenWave."),
            new("It's [sigh] Monday."),
            new("Next, it's Monday by the Mamas.", Crossing: "It's Monday"),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];

        public Task InitializeAsync()
        {
            (legacyFlags, ruleFlags) = DjParity.Verdicts(new DayAndTimeRule(), Cases);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItFlagsExactlyTheLinesCheckClockFlags() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void ItFlagsTheWrongDayAndTheWrongTimeOfDay() =>
            Assert.Equal([true, false, true, false, true, false, true, true], ruleFlags);
    }

    // Sad path
    // AC2 — a track title and the owner's message are left alone
    public sealed class ScenarioDayAndTimeExemptions : IAsyncLifetime
    {
        private const string Reply = "Next, it's Monday by the Mamas.";

        private static readonly DjParityCase[] Cases =
        [
            new(Reply, Track: "It's Monday"),
            new(Reply, Owner: "It's Monday by the Mamas"),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];

        public Task InitializeAsync()
        {
            (legacyFlags, ruleFlags) = DjParity.Verdicts(new DayAndTimeRule(), Cases);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheTitleAndTheOwnersMessageAreExemptJustAsTheTruthGateExemptsThem() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void TheTitleIsNotFlagged() => Assert.False(ruleFlags[0]);

        [Fact]
        public void TheOwnersMessageIsNotFlagged() => Assert.False(ruleFlags[1]);
    }

    // AC2 — the same clock check on a chat
    public sealed class ScenarioDayAndTimeInAChat : IAsyncLifetime
    {
        private static readonly string[] Replies =
        [
            ChatParity.Reply("Happy Monday.", "Sure is.", "Yes."),
            ChatParity.Reply("Happy Sunday.", "Sure is.", "Yes."),
            ChatParity.Reply("Good morning.", "Sure is.", "Yes."),
            ChatParity.Reply("Good afternoon.", "Sure is.", "Yes."),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];

        public Task InitializeAsync()
        {
            (legacyFlags, ruleFlags) = ChatParity.Verdicts(new DayAndTimeRule(), Replies);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItAgreesWithTheCrosstalkParser() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void OnlyTheWrongDayAndTheWrongTimeOfDayAreFlagged() => Assert.Equal([true, false, true, false], ruleFlags);
    }

    // AC3 — an announcement whose reply drops the owner's words
    public sealed class ScenarioOwnersMessage : IAsyncLifetime
    {
        private const string OwnerMessage = "Dinner is ready, come and get it.";

        private static readonly DjParityCase[] Cases =
        [
            new("Something smells good in the kitchen.", Owner: OwnerMessage),
            new("DINNER is ready - come and get it!", Owner: OwnerMessage),
            new("Dinner is ready, [sigh] come and get it.", Owner: OwnerMessage),
            new("Dinner is ready, come and get it.", FactBlock: "Sunday.\n\nSunny.", Owner: OwnerMessage),
        ];

        private List<bool> legacyFlags = [];
        private List<bool> ruleFlags = [];

        public Task InitializeAsync()
        {
            (legacyFlags, ruleFlags) = DjParity.Verdicts(new OwnerMessageRule(), Cases);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsFlagged() => Assert.True(ruleFlags[0]);

        [Fact]
        public void ItAgreesWithTheTruthGate() => Assert.Equal(legacyFlags, ruleFlags);

        [Fact]
        public void TheWordsInOrderWithOtherCapitalsAndPunctuationAreNotFlagged() => Assert.False(ruleFlags[1]);
    }

    // Sad path
    // AC3 — a talk that carries no owner's message
    public sealed class ScenarioNoOwnerMessage : IAsyncLifetime
    {
        private List<bool> ruleFlags = [];

        public Task InitializeAsync()
        {
            (_, ruleFlags) = DjParity.Verdicts(new OwnerMessageRule(), [new("Something smells good in the kitchen.")]);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ATalkWithNoOwnerMessageIsNotJudged() => Assert.False(ruleFlags[0]);
    }

    // AC4 — the repeat fixtures
    public sealed class ScenarioRepeatedPhrases
    {
        [Fact(Skip = "pending: T784")]
        public void ItFlagsExactlyTheLinesTheSharedRunCheckFlags() { }

        [Fact(Skip = "pending: T784")]
        public void CardPhrasesAreNeverTheFlaggedWords() { }
    }

    // AC5 — a capped catchphrase not offered this break, and an unavailable tagline
    public sealed class ScenarioCatchphraseLimits
    {
        [Fact(Skip = "pending: T785")]
        public void TheCatchphraseIsFlagged() { }

        [Fact(Skip = "pending: T785")]
        public void TheUnavailableTaglineIsFlagged() { }
    }

    // AC6 — any flag from the truth rules
    public sealed class ScenarioTheAsk : IAsyncLifetime
    {
        private List<RuleFlag?> flags = [];

        public Task InitializeAsync()
        {
            var djFacts = new TalkList<string>(["Sunday, 21 degrees, sunny."]);
            flags =
            [
                new OfferedFactsRule().Judge(TalkSnapshots.DjLine("It is 25 degrees and snowing, Monday.") with { Facts = djFacts }),
                new OfferedFactsRule().Judge(TalkSnapshots.Chat("Nice day.", "Lovely and sunny too.")),
                new DayAndTimeRule().Judge(TalkSnapshots.DjLine("Good morning, it's Monday and Tuesday.")),
                new OwnerMessageRule().Judge(TalkSnapshots.DjLine("Hello.") with { OwnerMessageCore = "Dinner is ready." }),
                new StationDetailsRule().Judge(TalkSnapshots.Chat("You are on 98.7 FM.", "Yes.")),
                new StationDetailsRule().Judge(TalkSnapshots.Chat("This is KXYZ.", "Yes.")),
                new StationDetailsRule().Judge(TalkSnapshots.Chat("It is August 20.", "Yes.")),
            ];
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void EveryFixtureIsFlagged() => Assert.All(flags, flag => Assert.NotNull(flag));

        [Fact]
        public void IsOneSentenceTellingTheModelWhatToFix() =>
            Assert.All(flags, flag => Assert.Matches(@"^[A-Z][^.!?]*(?:\.\d[^.!?]*)*\.$", flag?.Ask ?? string.Empty));
    }

    // Sad path
    // AC7 — a preview render
    public sealed class ScenarioAPreviewRender
    {
        [Fact(Skip = "pending: T784")]
        public void RepeatPhrasesDoesNotJudge() { }
    }

    // Sad path
    // AC8 — no catchphrase memo on the snapshot
    public sealed class ScenarioNoCatchphraseMemo
    {
        [Fact(Skip = "pending: T785")]
        public void NothingIsFlagged() { }
    }
}
