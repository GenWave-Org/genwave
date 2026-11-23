// STORY-568 — The moved DJ-line rules give today's verdicts (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Facts still carrying a Skip are activated by the task their Skip names.

using GenWave.Core.TalkRules;
using GenWave.Memory;
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

    // AC4 — the repeat fixtures, against today's DJ-line repeat check
    public sealed class ScenarioRepeatedPhrases : IAsyncLifetime
    {
        private const string Said = "We have a really lovely long set coming up next";
        private const string Tagline = "Stay golden my friends";

        // Filler lines share no five words with any copy below.
        private static RecentSaid[] Filler(int count) =>
            [.. Enumerable.Range(1, count).Select(i => new RecentSaid($"Filler row {i} says hello"))];

        private static readonly RepeatParityCase[] Cases =
        [
            // 0 — five fresh words in a row from the newest line
            new("We have a really lovely long set coming up for you tonight", [new(Said)]),
            // 1 — only four words in a row
            new("A really lovely long song to play", [new(Said)]),
            // 2 — the repeat is in a template line only
            new("We have a really lovely long set coming up for you tonight", [new(Said, Fallback: true)]),
            // 3 — the repeat is the tenth row
            new("We have a really lovely long set coming up for you tonight", [.. Filler(9), new(Said)]),
            // 4 — the repeat is the eleventh row, past the window
            new("We have a really lovely long set coming up for you tonight", [.. Filler(10), new(Said)]),
            // 5 — a template line is the tenth row, the repeat the eleventh: ten rows then drop templates, not ten real rows
            new("We have a really lovely long set coming up for you tonight", [.. Filler(9), new("Template line here", Fallback: true), new(Said)]),
            // 6 — a template line among the newest ten leaves the real repeat still inside
            new("We have a really lovely long set coming up for you tonight", [new("Template line here", Fallback: true), new(Said)]),
            // 7 — the card's tagline is used again; once the tagline is cut away fewer than five words are left
            new("Stay golden my friends and keep it going", [.. Filler(3), new("Stay golden my friends and keep it going")], Tagline),
            // 8 — the same run with no tagline on the card is a repeat
            new("Stay golden my friends and keep it going", [.. Filler(3), new("Stay golden my friends and keep it going")]),
            // 9 — a run made only of the track's own words is not a repeat
            new("Bohemian Rhapsody by Queen is up next", [new("Bohemian Rhapsody by Queen is on now")], TrackTitle: "Bohemian Rhapsody", TrackArtist: "Queen"),
            // 10 — the same run with no track set is a repeat
            new("Bohemian Rhapsody by Queen is up next", [new("Bohemian Rhapsody by Queen is on now")]),
            // 11 — nothing said before
            new("We have a really lovely long set coming up for you tonight", []),
        ];

        private List<string> legacyNamed = [];
        private List<string> ruleNamed = [];

        public Task InitializeAsync()
        {
            (legacyNamed, ruleNamed) = RepeatParity.DjVerdicts(new RepeatPhrasesRule(new RepetitionCheck()), Cases);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItNamesExactlyThePhrasesTheSharedRunCheckNames() => Assert.Equal(legacyNamed, ruleNamed);

        [Fact]
        public void ItFlagsTheFiveWordRunsInsideTheTenRealLines() =>
            Assert.Equal(
                [true, false, false, true, false, false, true, false, true, false, true, false],
                ruleNamed.Select(named => named.Length > 0));
    }

    // AC4 — an unavailable tagline (used inside its minimum breaks apart) sitting in a longer repeated run
    public sealed class ScenarioAnUnavailableTaglineInsideARun : IAsyncLifetime
    {
        private const string Tagline = "Stay golden my friends";
        private const string Run = "Stay golden my friends and keep it going tonight folks";

        // Filler lines share no five words with any copy below.
        private static RecentSaid[] Filler(int count) =>
            [.. Enumerable.Range(1, count).Select(i => new RecentSaid($"Filler row {i} says hello"))];

        private static readonly RepeatParityCase[] Cases =
        [
            // 0 — the tagline was said two lines back (inside its three breaks apart), so it is unavailable; the run goes on past it
            new(Run, [.. Filler(2), new(Run)], Tagline),
            // 1 — unavailable again, but only four words follow it: the shared run has nothing left to name
            new("Stay golden my friends and keep it going", [.. Filler(2), new("Stay golden my friends and keep it going")], Tagline),
        ];

        private List<string> legacyNamed = [];
        private List<string> ruleNamed = [];

        public Task InitializeAsync()
        {
            (legacyNamed, ruleNamed) = RepeatParity.DjVerdicts(new RepeatPhrasesRule(new RepetitionCheck()), Cases);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRuleNamesWhatTheSharedRunPartNames() => Assert.Equal(legacyNamed, ruleNamed);

        [Fact]
        public void OnlyTheWordsOutsideTheTaglineAreNamed() =>
            Assert.Equal(["and keep it going tonight folks", string.Empty], ruleNamed);
    }

    // AC4 — each thing the check never counts breaks a run that would otherwise repeat. The run is stop words around one word, and
    // nothing else (no clock, fact or containment words), so only the repeat rule can trip. Each is paired with a control where
    // the same run has no such exclusion and is flagged.
    public sealed class ScenarioExclusionsBreakARun : IAsyncLifetime
    {
        private static RepeatParityCase[] Pair(string word, Func<RepeatParityCase, RepeatParityCase> exclude)
        {
            var run = $"We have the {word} by the";
            var plain = new RepeatParityCase($"Here {run} tonight", [new($"Now {run} again")]);
            return [exclude(plain), plain];
        }

        private static readonly RepeatParityCase[] Cases =
        [
            .. Pair("moonlight", c => c with { TrackTitle = "Untitled", TrackAlbum = "Moonlight" }),
            // The year is digits: a year fact is never fresh. The control keeps the digits with no track year.
            .. Pair("1975", c => c with { TrackTitle = "Untitled", TrackYear = 1975 }),
            .. Pair("sunrise", c => c with { CrossingTitle = "Sunrise", CrossingArtist = "Band" }),
            .. Pair("sunset", c => c with { CrossingTitle = "Song", CrossingArtist = "Sunset" }),
            .. Pair("tony", c => c with { PersonaName = "Tony" }),
            .. Pair("radiorama", c => c with { StationName = "Radiorama" }),
        ];

        private List<string> legacyNamed = [];
        private List<string> ruleNamed = [];

        public Task InitializeAsync()
        {
            (legacyNamed, ruleNamed) = RepeatParity.DjVerdicts(new RepeatPhrasesRule(new RepetitionCheck()), Cases);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRuleNamesWhatTheSharedRunCheckNames() => Assert.Equal(legacyNamed, ruleNamed);

        [Fact]
        public void OnlyTheRunsWithNoExclusionAreFlagged() =>
            Assert.Equal(
                [false, true, false, true, false, true, false, true, false, true, false, true],
                ruleNamed.Select(named => named.Length > 0));
    }

    // AC4 — card phrases are never the flagged words
    public sealed class ScenarioCardPhrasesAreSkipped : IAsyncLifetime
    {
        private RuleFlag? flag;

        public Task InitializeAsync()
        {
            var said = new RecentSaid("Stay golden my friends and keep it going tonight folks");
            var snapshot = RepeatParity.DjSnapshot(
                new RepeatParityCase("Stay golden my friends and keep it going tonight folks", [said], "Stay golden my friends"));
            flag = new RepeatPhrasesRule(new RepetitionCheck()).Judge(snapshot);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OnlyTheWordsOutsideTheTaglineAreNamed() => Assert.Equal("and keep it going tonight folks", flag?.Evidence);
    }

    // AC4 — a known difference from today: a quirk's own words are skipped too (PLAN T784 note: legacy skips tagline spans only).
    // The shadow report must explain it; if it shows, raise it with Dean.
    public sealed class ScenarioQuirkPhrasesAreSkippedToo : IAsyncLifetime
    {
        private const string Copy = "Honestly log it that was a banger";
        private const string QuirkRule = "Finish with log it that was a banger";

        private RuleFlag? ruleFlag;
        private bool legacyFlags;

        public Task InitializeAsync()
        {
            var said = new RecentSaid("Honestly log it that was a banger");
            var snapshot = RepeatParity.DjSnapshot(new RepeatParityCase(Copy, [said]));
            var speaker = snapshot.Speakers[0] with { CardPhrases = new TalkList<string>([QuirkRule]) };
            ruleFlag = new RepeatPhrasesRule(new RepetitionCheck()).Judge(snapshot with { Speakers = new TalkList<TalkSpeaker>([speaker]) });
            legacyFlags = RepeatParity.LegacyDjFlags(new RepeatParityCase(Copy, [said]));
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRuleDoesNotFlagTheQuirkWords() => Assert.Null(ruleFlag);

        [Fact]
        public void TodaysCheckStillFlagsThem() => Assert.True(legacyFlags);
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
    public sealed class ScenarioAPreviewRender : IAsyncLifetime
    {
        private static readonly RepeatParityCase Repeat =
            new("We have a really lovely long set coming up for you tonight", [new("We have a really lovely long set coming up next")]);

        private RuleFlag? preview;
        private RuleFlag? onAir;

        public Task InitializeAsync()
        {
            var rule = new RepeatPhrasesRule(new RepetitionCheck());
            preview = rule.Judge(RepeatParity.DjSnapshot(Repeat, preview: true));
            onAir = rule.Judge(RepeatParity.DjSnapshot(Repeat));
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void RepeatPhrasesDoesNotJudge() => Assert.Null(preview);

        [Fact]
        public void TheSameLineOnAirIsFlagged() => Assert.NotNull(onAir);
    }

    // Sad path
    // AC8 — no catchphrase memo on the snapshot
    public sealed class ScenarioNoCatchphraseMemo
    {
        [Fact(Skip = "pending: T785")]
        public void NothingIsFlagged() { }
    }
}
