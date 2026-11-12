// STORY-544 — A track name is not a repeat (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T710)
//
// BDD specification — xUnit. Active since T710.
// entry point: IRepetitionCheck (pure; reached via LlmCopyWriter.WriteAsync)

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureATrackNameIsNotARepeat
{
    static readonly IRepetitionCheck Check = new RepetitionCheck();

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — five words make a hit
    public sealed class ScenarioFiveWordsMakeAHit
    {
        // Given a recent utterance "now this next track disagrees with the last one"
        // When  the candidate contains "this next track disagrees with the"
        readonly RepetitionResult result = Check.Check(
            "Honestly, this next track disagrees with the radio.",
            new RepetitionContext(["now this next track disagrees with the last one"], []));

        [Fact]
        public void TheCheckReportsOneHitNamingThatPhrase() =>
            Assert.Equal(["this next track disagrees with the"], result.Hits.Select(h => h.Phrase));
    }

    // AC2 — four words do not
    public sealed class ScenarioFourWordsDoNot
    {
        // Given a recent utterance "next up we've got something special"
        // When  the candidate contains "next up we've got"
        readonly RepetitionResult result = Check.Check(
            "Okay, next up we've got a rocker.",
            new RepetitionContext(["next up we've got something special"], []));

        [Fact]
        public void TheCheckReportsNoHit() => Assert.False(result.IsHit);
    }

    // AC3 — a possessive artist is still the artist
    public sealed class ScenarioAPossessiveArtistIsStillTheArtist
    {
        // Given the track "Knowing Me, Knowing You" by "ABBA" and a recent utterance "That was ABBA's Knowing Me, Knowing You"
        // When  the candidate says "ABBA's Knowing Me, Knowing You, coming up"
        readonly RepetitionResult result = Check.Check(
            "ABBA's Knowing Me, Knowing You, coming up",
            new RepetitionContext(
                ["That was ABBA's Knowing Me, Knowing You"], [],
                TrackTitle: "Knowing Me, Knowing You", Artist: "ABBA"));

        [Fact]
        public void TheCheckReportsNoHit() => Assert.False(result.IsHit);
    }

    // AC4 — the track before counts too
    public sealed class ScenarioTheTrackBeforeCountsToo
    {
        // Given the crossing track "Do You Believe in Shame" by "Duran Duran"
        // When  the candidate repeats "Duran Duran's Do You Believe in Shame" from a recent utterance
        readonly RepetitionResult result = Check.Check(
            "Duran Duran's Do You Believe in Shame, and then some.",
            new RepetitionContext(
                ["That was Duran Duran's Do You Believe in Shame"], [],
                CrossingTrackTitle: "Do You Believe in Shame", CrossingTrackArtist: "Duran Duran"));

        [Fact]
        public void TheCheckReportsNoHit() => Assert.False(result.IsHit);
    }

    // The 11-10 soak table (SPEC F218.34), as literal facts.
    public sealed class ScenarioTheSoakTableIsNoHits
    {
        // "abbas knowing me knowing you": the possessive artist beside the title
        readonly RepetitionResult abba = Check.Check(
            "abba's knowing me knowing you",
            new RepetitionContext(
                ["ABBA's Knowing Me, Knowing You"], [],
                TrackTitle: "Knowing Me, Knowing You", Artist: "ABBA"));

        // "duran durans do you believe in shame": artist, possessive artist, then the title
        readonly RepetitionResult duran = Check.Check(
            "duran duran's do you believe in shame",
            new RepetitionContext(
                ["Duran Duran, Duran Duran's Do You Believe in Shame"], [],
                CrossingTrackTitle: "Do You Believe in Shame", CrossingTrackArtist: "Duran Duran"));

        // "next up weve got": four words
        readonly RepetitionResult nextUp = Check.Check(
            "next up we've got",
            new RepetitionContext(["next up we've got a great one"], []));

        [Fact]
        public void ThePossessiveArtistAndTitleIsNoHit() => Assert.False(abba.IsHit);

        [Fact]
        public void TheCrossingTrackNamesAreNoHit() => Assert.False(duran.IsHit);

        [Fact]
        public void TheFourWordOpenerIsNoHit() => Assert.False(nextUp.IsHit);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — a name does not hide the words around it
    public sealed class ScenarioANameDoesNotHideTheWordsAroundIt
    {
        // Given a recent utterance "ABBA's Knowing Me, Knowing You, and what a night it has been so far"
        // When  the candidate repeats "and what a night it has been so far"
        readonly RepetitionResult result = Check.Check(
            "Well, and what a night it has been so far.",
            new RepetitionContext(
                ["ABBA's Knowing Me, Knowing You, and what a night it has been so far"], [],
                TrackTitle: "Knowing Me, Knowing You", Artist: "ABBA"));

        [Fact]
        public void TheCheckReportsOneHit() => Assert.Single(result.Hits);
    }

    // F218.34 — the possessive drop decides: only it makes "pepper" a word of the title "Sgt. Pepper's ...".
    public sealed class ScenarioABareWordOfAPossessiveTitleIsNotFresh
    {
        // Given the track "Sgt. Pepper's Lonely Hearts Club Band" and a recent utterance "we went to the pepper in the garden"
        // When  the candidate repeats "to the pepper in the"
        readonly RepetitionResult result = Check.Check(
            "Right, to the pepper in the shed.",
            new RepetitionContext(
                ["we went to the pepper in the garden"], [],
                TrackTitle: "Sgt. Pepper's Lonely Hearts Club Band"));

        [Fact]
        public void TheCheckReportsNoHit() => Assert.False(result.IsHit);
    }

    // F218.34 — a contraction keeps its 's, so "it's" still meets "its".
    public sealed class ScenarioAContractionIsNotAPossessive
    {
        // Given a recent utterance "so its a good day out"
        // When  the candidate says "so it's a good day"
        readonly RepetitionResult result = Check.Check(
            "Well, so it's a good day.",
            new RepetitionContext(["so its a good day out"], []));

        [Fact]
        public void TheCheckReportsOneHit() => Assert.Single(result.Hits);
    }

    // STORY-543 (F218.33): tagline quota facts below.
    const string Tagline = "Every record is a footnote to another record.";

    // F218.33 — an allowed tagline does not excuse a different genuine repeat in the same copy.
    public sealed class ScenarioAnAllowedTaglineDoesNotHideAnotherRepeat
    {
        // Given the tagline allowed and a recent utterance "we shall now proceed with the grand finale"
        // When  the copy says the tagline and then "proceed with the grand finale"
        readonly RepetitionResult result = Check.Check(
            Tagline + " Now we proceed with the grand finale.",
            new RepetitionContext(["we shall now proceed with the grand finale", Tagline], [], AllowedPhrases: [Tagline]));

        [Fact]
        public void TheRepeatStillHits() => Assert.Equal(["proceed with the grand finale"], result.Hits.Select(h => h.Phrase));
    }

    // F218.33 — a run touching an unavailable tagline folds into that one hit.
    public sealed class ScenarioARunTouchingAnUnavailableTaglineIsOneHit
    {
        // Given the tagline unavailable and a recent utterance holding it
        // When  the copy says it
        readonly RepetitionResult result = Check.Check(
            "Settle in. " + Tagline + " Next up.",
            new RepetitionContext(["Settle in. " + Tagline], [], UnavailablePhrases: [Tagline]));

        [Fact]
        public void TheCheckReportsOneHit() => Assert.Single(result.Hits);
    }

    // F218.33 — a partial use (enough contiguous tagline words, F218.39) in quota is not a hit.
    public sealed class ScenarioAPartialAllowedTaglineIsNotAHit
    {
        // Given the tagline allowed and recent holding it
        // When  the copy says "every record is a footnote to another"
        readonly RepetitionResult result = Check.Check(
            "Remember, every record is a footnote to another, always.",
            new RepetitionContext([Tagline], [], AllowedPhrases: [Tagline]));

        [Fact]
        public void TheCheckReportsNoHit() => Assert.False(result.IsHit);
    }

    // F218.33 — "A tagline use inside quota is never a hit", even when the shared run reaches past it.
    public sealed class ScenarioAnAllowedTaglineWithAdjacentSharedWordsIsNotAHit
    {
        // Given the tagline allowed and recent holding "Settle in. <tagline> Number 5."
        // When  the copy says "Settle in. <tagline> Next up."
        readonly RepetitionResult result = Check.Check(
            "Settle in. " + Tagline + " Next up.",
            new RepetitionContext(["Settle in. " + Tagline + " Number 5."], [], AllowedPhrases: [Tagline]));

        [Fact]
        public void TheCheckReportsNoHit() => Assert.False(result.IsHit);
    }

    // F218.33 — a partial use of an unavailable tagline is one hit, named as the tagline is written.
    public sealed class ScenarioAPartialUnavailableTaglineIsOneNamedHit
    {
        // Given the tagline unavailable and recent holding it
        // When  the copy says "every record is a footnote to another"
        readonly RepetitionResult result = Check.Check(
            "Remember, every record is a footnote to another, always.",
            new RepetitionContext([Tagline], [], UnavailablePhrases: [Tagline]));

        [Fact]
        public void TheHitNamesTheTagline() => Assert.Equal([Tagline], result.Hits.Select(h => h.Phrase));
    }

    // F218.33 — an unavailable tagline hits even when its earlier use is outside RecentTexts.
    public sealed class ScenarioAnUnavailableTaglineHitsWithoutRecentText
    {
        // Given the tagline unavailable and no recent utterance holding it
        // When  the copy says it
        readonly RepetitionResult result = Check.Check(
            "Settle in. " + Tagline,
            new RepetitionContext(["something else entirely"], [], UnavailablePhrases: [Tagline]));

        [Fact]
        public void TheCheckReportsOneHit() => Assert.Single(result.Hits);
    }

    // F218.33 — a separate repeat touching an unavailable tagline is not swallowed by it.
    public sealed class ScenarioARepeatBesideAnUnavailableTaglineStillHits
    {
        const string Tail = "and the crowd went home happy tonight";

        // Given the tagline unavailable and a recent utterance holding it plus a longer sentence
        // When  the copy says the tagline and the same sentence straight after
        readonly RepetitionResult result = Check.Check(
            Tagline + " " + Tail,
            new RepetitionContext([Tagline + " " + Tail], [], UnavailablePhrases: [Tagline]));

        [Fact]
        public void TheTaglineAndTheRepeatAreTwoHits() => Assert.Equal(2, result.Hits.Count);

        [Fact]
        public void OneHitIsTheTaglineAsWritten() => Assert.Contains(Tagline, result.Hits.Select(h => h.Phrase));
    }

    // STORY-543 AC16 (F218.39) — the check cuts only a real use of a long tagline.
    public sealed class ScenarioTheCheckCutsOnlyARealUse
    {
        const string Long = "this next track disagrees with the last one, listen";

        // Given the 9-word tagline unavailable
        // When  the copy opens "This next track is a monster..." and there are no recent texts
        readonly RepetitionResult result = Check.Check(
            "This next track is a monster, folks, turn it up loud for the whole street to hear.",
            new RepetitionContext([], [], UnavailablePhrases: [Long]));

        [Fact]
        public void ThereIsNoHit() => Assert.False(result.IsHit);
    }
}
