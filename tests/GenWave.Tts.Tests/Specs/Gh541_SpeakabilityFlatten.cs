// gh-#541 — "More puncuation issues": every mark the LLM writes for the eye, the pinned engines
// read as a prosody cue for the mouth — dashes pause, quotes accentuate, elision apostrophes
// pause, TitleCase mid-sentence reads as a sentence start (gh-#432), and the comma-vocative
// stumbles (gh-#292). Dean's ruling on the issue: lowercase the copy and discard anything that
// isn't a-z — no more whack-a-mole. The flatten pass applies that ruling at the F68 chokepoint
// with three deliberate survivors, each pinned below because removal would air WORSE than a pause:
// sentence enders (the gh-#116 pause splice and the cue analyzer key off them), digits (dropping
// "76" from "76 degrees" mangles copy; HOW a number is read is gh-#211's lexicon problem), and
// intra-word apostrophes/hyphens ("we'll" stripped to "well" is a different word on air).
//
// BDD specification — xUnit. Every scenario drives SpeechText.Normalize with an empty correction
// set: the flatten is a fixed pass, not an operator rule, and must hold with nothing configured.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSpeakabilityFlatten
{
    static string Flatten(string text) => SpeechText.Normalize(text, SpeechCorrectionSet.Empty);

    public static class ScenarioDeansExhibitsFromTheBoothLogs
    {
        [Fact]
        public static void The_spaced_pause_dash_disappears()
        {
            // Given the first gh-#541 booth-log exhibit / When it is normalized
            var spoken = Flatten("time to clear the waters with some sonic turmoil - lyonn's 'iceberg' is heading our way");

            // Then the dash is gone and the quoted title closes up, while the contraction survives
            Assert.Equal("time to clear the waters with some sonic turmoil lyonn's iceberg is heading our way", spoken);
        }

        [Fact]
        public static void The_elision_apostrophe_closes_up()
        {
            // Given the second exhibit's elision words
            var spoken = Flatten("Comin' right up, you know the track that's gonna get those toes tappin'");

            // Then comin'/tappin' lose the trailing pause mark but that's keeps its contraction
            Assert.Equal("comin right up you know the track that's gonna get those toes tappin", spoken);
        }
    }

    public static class ScenarioCommaVocative
    {
        [Fact]
        public static void The_comma_before_a_vocative_is_removed()
        {
            // Given gh-#292's on-air exhibit
            var spoken = Flatten("Hold on to your hats, folks. We have a great track coming up.");

            // Then the copy runs straight through the vocative and keeps its sentence boundary
            Assert.Equal("hold on to your hats folks. we have a great track coming up.", spoken);
        }
    }

    public static class ScenarioMidSentenceCapitals
    {
        [Fact]
        public static void A_mid_sentence_titlecase_word_is_lowercased()
        {
            // Given gh-#432's on-air exhibit shape
            var spoken = Flatten("a brass and glass record from the collection, Lyonn's The Symphony makes its way in");

            // Then nothing mid-sentence can read as a sentence start
            Assert.Equal("a brass and glass record from the collection lyonn's the symphony makes its way in", spoken);
        }
    }

    public static class ScenarioTheThreeSurvivors
    {
        [Fact]
        public static void Sentence_enders_survive_and_runs_collapse()
        {
            var spoken = Flatten("What a track! Really?! Stay with us.");

            Assert.Equal("what a track! really? stay with us.", spoken);
        }

        [Fact]
        public static void Digits_survive_with_their_expanded_units()
        {
            // Given weather copy with a digit-anchored unit / Then the number still airs
            var spoken = Flatten("A warm 76°F out there");

            Assert.Equal("a warm 76 degrees fahrenheit out there", spoken);
        }

        [Fact]
        public static void An_intra_word_hyphen_survives_where_a_pause_dash_does_not()
        {
            var spoken = Flatten("A brass-and-glass sound - pure gold");

            Assert.Equal("a brass-and-glass sound pure gold", spoken);
        }
    }

    public static class ScenarioEllipsesAndQuotes
    {
        [Fact]
        public static void An_ellipsis_becomes_a_plain_space()
        {
            // Given the pause-maker in both spellings / Then neither leaves a mark behind
            var spoken = Flatten("Hold on to your hats… folks... here it comes");

            Assert.Equal("hold on to your hats folks here it comes", spoken);
        }

        [Fact]
        public static void Curly_quotes_flatten_like_straight_ones()
        {
            var spoken = Flatten("She said “hello” and we’ll take that as a ‘yes’");

            Assert.Equal("she said hello and we'll take that as a yes", spoken);
        }
    }

    public static class ScenarioNamesAreNeverTruncated
    {
        [Fact]
        public static void An_accented_name_folds_to_its_base_letters()
        {
            // Given an accented artist name / Then it folds (beyonce), never truncates (beyonc)
            var spoken = Flatten("That was Beyoncé with the club classic");

            Assert.Equal("that was beyonce with the club classic", spoken);
        }
    }

    public static class ScenarioSpeechMarkupPassesThroughVerbatim
    {
        [Fact]
        public static void A_pause_directive_is_preserved()
        {
            // Given an authored segment carrying a pause token (the F96 vocabulary)
            var spoken = Flatten("Please stand by [pause:0.5s] We'll be right back");

            // Then the token is untouched while the prose around it flattens
            Assert.Equal("please stand by [pause:0.5s] we'll be right back", spoken);
        }

        [Fact]
        public static void A_pronunciation_annotation_is_preserved()
        {
            // Given an operator correction whose replacement carries an annotation — the one
            // production door annotated markup enters Normalize through (raw input markup is
            // eaten upstream by the markdown-link strip, corrections inject after it)
            var rules = SpeechCorrectionSet.Create(
                [new SpeechCorrection("MacLeod", "[MacLeod](/məˈklaʊd/)")]);

            var spoken = SpeechText.Normalize("Music by MacLeod tonight", rules);

            // Then brackets, casing, and IPA all survive while the prose flattens
            Assert.Equal("music by [MacLeod](/məˈklaʊd/) tonight", spoken);
        }
    }
}
