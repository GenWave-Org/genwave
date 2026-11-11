// STORY-534 — The DJ does not say that again (gh-#293 gh-#920 · SPEC F218 · PLAN T691)
//
// IRepetitionCheck is pure: every fact builds a RepetitionContext and reads the result.
//
// Spans two files by seam: AC2–AC4 (T691, IRepetitionCheck) here; AC1/AC5–AC8 (T692/T693, prompt + writer) in
// tests/GenWave.Tts.Tests/Specs/Story534_TheDjDoesNotSayThatAgain_Writer.cs.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureTheDjDoesNotSayThatAgainCheck
{
    static readonly IRepetitionCheck Check = new RepetitionCheck();

    static RepetitionContext Recent(params string[] texts) => new(texts, []);

    static RepetitionContext Named(params string[] texts) =>
        new(texts, [], "DJ Nova", "GenWave Radio", "Dancing in the Dark", "Bruce Springsteen");

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC2 — a five-word run is a hit
    // entry point: IRepetitionCheck (pure; reached via LlmCopyWriter.WriteAsync)
    public sealed class ScenarioAFiveWordRunIsAHit
    {
        // Given a recent utterance "welcome to the dance floor of the airwaves tonight"
        // When  the candidate copy contains "dance floor of the airwaves"

        [Fact]
        public void TheCheckReportsOneHitNamingThatPhrase()
        {
            var result = Check.Check(
                "Okay, so, dance floor of the airwaves is open.",
                Recent("welcome to the dance floor of the airwaves tonight"));

            Assert.Equal(["dance floor of the airwaves"], result.Hits.Select(h => h.Phrase));
        }
    }

    // AC3 — names and titles are not hits
    public sealed class ScenarioNamesAndTitlesAreNotHits
    {
        // Given the station name "GenWave Radio" and track "Dancing in the Dark" by "Bruce Springsteen"
        // When  the candidate repeats only those words in a run

        [Fact]
        public void TheCheckReportsNoHit()
        {
            var result = Check.Check(
                "Dancing in the Dark, Bruce Springsteen, on GenWave Radio",
                Named("That was Dancing in the Dark by Bruce Springsteen here on GenWave Radio"));

            Assert.False(result.IsHit);
        }
    }

    // AC4 — a spent catchphrase is a hit
    public sealed class ScenarioASpentCatchphraseIsAHit
    {
        // Given the quirk "Declares at least one song per shift 'officially a banger, log it'" is spent this shift
        // When  the candidate contains "banger, log it"

        [Fact]
        public void TheCheckReportsOneHitNamingOfficiallyABangerLogIt()
        {
            var context = new RepetitionContext(
                [], ["Declares at least one song per shift 'officially a banger, log it'"]);

            var result = Check.Check("That one is a banger, log it.", context);

            Assert.Equal(["officially a banger, log it"], result.Hits.Select(h => h.Phrase));
        }
    }

    // F218.11 follow-up — track facts are not repeats; real repeats still are
    public sealed class ScenarioTrackFactsAreNotRepeats
    {
        // Given a recent line that names the current track (title, artist, album, year)
        // When  the candidate shares a run made of those track facts plus at most one fresh word
        static RepetitionContext DuranDuran(params string[] texts) =>
            new(texts, [], null, null, "Do You Believe in Shame? (2010 Remaster)", "Duran Duran", "Big Thing", 2010);

        const string LeadIn =
            "Next, we have Duran Duran's 'Do You Believe in Shame?' Remastered in 2010. Quite the statement, wouldn't you agree?";

        [Fact]
        public void ABackAnnounceNamingThePossessiveArtistAndTitleIsNotAHit() =>
            Assert.False(Check.Check("Duran Duran's Do You Believe in Shame, that was", DuranDuran(LeadIn)).IsHit);

        [Fact]
        public void ABackAnnounceSayingRemasteredForUsInIsNotAHit() =>
            Assert.False(Check.Check("It was remastered for us in 2010, folks", DuranDuran("Remastered for us in 2010, here it is")).IsHit);

        [Fact]
        public void AYearInWordsPlusOneFillerWordIsNotAHit()
        {
            var context = new RepetitionContext(
                ["a classic from nineteen fifty nine naturally"], [], Year: 1959);

            Assert.False(Check.Check("nineteen fifty nine naturally, what a year", context).IsHit);
        }

        [Fact]
        public void ASharedRunWithOnlyOneFreshWordIsNotAHit() =>
            Assert.False(Check.Check("x of the dance floor", Titled("Dance", "y of the dance floor")).IsHit);

        [Fact]
        public void APossessiveFormOfANameWordDoesNotCountAsFresh() =>
            Assert.False(Check.Check("duran durans shame tonight", ShameByDuranDuran("duran durans shame tonight")).IsHit);

        [Fact]
        public void AnAlbumWordDoesNotCountAsFresh()
        {
            var context = new RepetitionContext(["velvet garden of tonight"], [], Album: "Velvet Garden");

            Assert.False(Check.Check("velvet garden of tonight", context).IsHit);
        }

        [Fact]
        public void AZeroPaddedYearDoesNotCountAsFresh() =>
            Assert.False(Check.Check("05 naturally in the", new RepetitionContext(["05 naturally in the"], [], Year: 2005)).IsHit);

        static RepetitionContext Titled(string title, params string[] texts) =>
            new(texts, [], null, null, title);

        static RepetitionContext ShameByDuranDuran(params string[] texts) =>
            new(texts, [], null, null, "Shame", "Duran Duran");
    }

    public sealed class ScenarioRealRepeatsAreStillCaught
    {
        // Given a recent line holding a persona phrasing run (a stock opener, a soak phrase)
        // When  the candidate shares that run, even with track facts set
        [Fact]
        public void AStockOpenerWithNoTrackFactsIsAHit()
        {
            var result = Check.Check("Okay, this one is for you, friend.", Recent("Well this one is for you"));

            Assert.Equal(["this one is for you"], result.Hits.Select(h => h.Phrase));
        }

        [Fact]
        public void AStopWordTitleDoesNotGrowFormsThatHideARepeat()
        {
            // "and its the one for" is the shortest run that goes red without the stop-word rule: only "its" is a form
            // of the title word "it", leaving "one" as the lone fresh word. A longer text with a second fresh word
            // (e.g. "so its the one for you") would pass either way, so it can't pin the rule.
            var context = new RepetitionContext(["and its the one for"], [], null, null, "Beat It");

            Assert.True(Check.Check("and its the one for", context).IsHit);
        }

        [Theory]
        [InlineData(2001)]
        [InlineData(1991)]
        public void ALoneNumberWordInAStockOpenerIsNotATrackFact(int year)
        {
            var result = Check.Check("Okay, this one is for you", new RepetitionContext(["this one is for you"], [], Year: year));

            Assert.Equal(["this one is for you"], result.Hits.Select(h => h.Phrase));
        }

        [Fact]
        public void ALoneTenInAStockOpenerIsNotATrackFact()
        {
            var result = Check.Check("top ten for you all", new RepetitionContext(["top ten for you all"], [], Year: 2010));

            Assert.Equal(["top ten for you all"], result.Hits.Select(h => h.Phrase));
        }

        [Fact]
        public void AClashRunNamesThePhrase()
        {
            var result = Check.Check(
                "Ladies and gentlemen, prepare for a clash of titans!",
                Recent("Well, ladies and gentlemen prepare for a clash of two giants tonight"));

            Assert.Equal(["ladies and gentlemen prepare for a clash of"], result.Hits.Select(h => h.Phrase));
        }

        [Fact]
        public void ADisagreesRunIsAHit()
        {
            var result = Check.Check(
                "This next track disagrees with everything I know.",
                Recent("Careful, this next track disagrees with the last one."));

            Assert.Equal(["this next track disagrees with"], result.Hits.Select(h => h.Phrase));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNearMissesAndDisguises
    {
        [Fact]
        public void AThreeWordSharedRunIsNotAHit()
        {
            var result = Check.Check("the dance floor is empty", Recent("the dance floor of the airwaves"));

            Assert.False(result.IsHit);
        }

        [Fact]
        public void PunctuationAndCaseDifferencesStillHit()
        {
            var result = Check.Check(
                "THE DANCE-FLOOR, of the airwaves!",
                Recent("the dance floor of the airwaves"));

            Assert.True(result.IsHit);
        }

        [Fact]
        public void AQuirkThatIsNotSpentIsNotChecked()
        {
            var context = new RepetitionContext(["something else entirely"], ["Says 'stay groovy' at sign-off"]);

            var result = Check.Check("officially a banger, log it", context);

            Assert.False(result.IsHit);
        }
    }

    public sealed class ScenarioQuirkKeyPhrases
    {
        static string KeyPhraseHit(string rule, string candidate) =>
            Assert.Single(Check.Check(candidate, new RepetitionContext([], [rule])).Hits).Phrase;

        [Fact]
        public void AnApostropheInsideSingleQuotesDoesNotEndTheSpan() =>
            Assert.Equal(
                "that's a wrap, my friends",
                KeyPhraseHit("Says 'that's a wrap, my friends' at the end of each shift", "well that's a wrap, my friends"));

        [Fact]
        public void ACurlyApostropheInsideCurlySingleQuotesDoesNotEndTheSpan() =>
            Assert.Equal(
                "don’t touch that dial",
                KeyPhraseHit("Says ‘don’t touch that dial’ before the news", "so don’t touch that dial"));

        [Fact]
        public void DoubleQuotesStillDelimitTheSpan() =>
            Assert.Equal(
                "stay groovy my friends",
                KeyPhraseHit("Signs off with \"stay groovy my friends\" every night", "and stay groovy my friends"));

        [Fact]
        public void WithNoQuoteTheLastFourWordsAreTheKeyPhrase() =>
            Assert.Equal(
                "wave to the neighbours",
                KeyPhraseHit("Tells listeners to wave to the neighbours", "go wave to the neighbours now"));
    }

    public sealed class ScenarioOneStretchOneHit
    {
        [Fact]
        public void ARunInsideALongerRunFromAnotherRecentLineIsNotReportedTwice()
        {
            var result = Check.Check("x a b c d e", Recent("x a b c d e", "a b c d e"));

            Assert.Single(result.Hits);
        }

        [Fact]
        public void TheSameStretchRepeatedWithinOneRecentLineIsReportedOnce()
        {
            var result = Check.Check("x a b c d e", Recent("x a b c d e q a b c d e"));

            Assert.Single(result.Hits);
        }

        [Fact]
        public void OverlappingRunsMergeIntoOneHitNamingTheUnion()
        {
            var result = Check.Check("one two three four five six", Recent("one two three four five", "two three four five six"));

            Assert.Equal(["one two three four five six"], result.Hits.Select(h => h.Phrase));
        }
    }

    // ---------------------------------------------------------------------
    // EDGE — properties over a fixed table
    // ---------------------------------------------------------------------

    public sealed class ScenarioPropertiesOverATable
    {
        public static TheoryData<string> Lines => new()
        {
            "welcome back to the late shift on the quiet side",
            "that was a gorgeous slice of seventies soul",
            "coming up a long run of rainy day records",
            "if you are just tuning in you picked a good night",
            "Somebody call the fire department, that groove is hot!",
            "the kettle is on and the vinyl is spinning",
            "midnight oil and a stack of forgotten singles",
            "this one goes out to everyone driving home",
            "three in a row from the crate marked mystery",
            "nothing but blue skies and slow guitars ahead",
            "we are halfway through the shift and feeling fine",
            "keep it right here for more music after the news",
        };

        [Theory]
        [MemberData(nameof(Lines))]
        public void ACandidateEqualToARecentLineAlwaysHits(string line)
        {
            var result = Check.Check(line, Named("unrelated text here", line));

            Assert.True(result.IsHit);
        }

        [Theory]
        [InlineData("DJ Nova, GenWave Radio, Dancing in the Dark, Bruce Springsteen")]
        [InlineData("the and of to in a is that it")]
        [InlineData("Bruce Springsteen and the GenWave Radio of the Dancing")]
        [InlineData("dj nova dj nova dj nova dj nova dj nova")]
        public void ACandidateOfOnlyExclusionsNeverHits(string candidate)
        {
            var result = Check.Check(
                candidate,
                Named(candidate, "DJ Nova, GenWave Radio, Dancing in the Dark, Bruce Springsteen and the of to in a is that it"));

            Assert.False(result.IsHit);
        }
    }
}
