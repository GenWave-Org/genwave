// STORY-490 — The gauntlet speaks the station's text (gh-#614 · SPEC F214.2, F214.3 · PLAN T607, T611)
//
// BDD specification — xUnit. AC1–AC11 are live (T607); AC12 stays pending for T611.

using System.Text.RegularExpressions;
using GenWave.Core.Domain;
using GenWave.Tts;
using PronunciationRule = GenWave.Tts.PronunciationRule;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Text;
using GenWave.VoiceOff.Tests.Support;
using static GenWave.VoiceOff.Tests.Support.GauntletFixtureDirectory;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheGauntletSpeaksTheStationsText
{
    private static string ShippedDirectory => Path.Combine(AppContext.BaseDirectory, "gauntlet");

    private static readonly SpeechCorrectionSet MacLeodCorrection =
        SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod", "Muh-cloud")]);

    private static readonly IReadOnlyDictionary<string, string?> LaughsAndNoSigh =
        new Dictionary<string, string?> { ["laugh"] = "[laughs]", ["sigh"] = null };

    private static InvalidDataException LoadFailure(string directory) =>
        Assert.Throws<InvalidDataException>(() => GauntletSet.Load(directory));

    // AC1 — every class is present
    public sealed class ScenarioEveryClassIsPresent
    {
        private readonly IReadOnlySet<GauntletClass> classes =
            GauntletSet.Load(ShippedDirectory).Lines.Select(l => l.Class).ToHashSet();

        [Theory]
        [InlineData("LeadIn")]
        [InlineData("BackAnnounce")]
        [InlineData("StationId")]
        [InlineData("Pronunciation")]
        [InlineData("Emotive")]
        [InlineData("NonVerbal")]
        [InlineData("Crosstalk")]
        public void ItHoldsAtLeastOneLineOfTheClass(string expected) =>
            Assert.Contains(Enum.Parse<GauntletClass>(expected), classes);
    }

    // AC2 — the voiced classes cover every register
    public sealed class ScenarioTheVoicedClassesCoverEveryRegister
    {
        private readonly GauntletSet set = GauntletSet.Load(ShippedDirectory);

        [Theory]
        [InlineData("LeadIn")]
        [InlineData("BackAnnounce")]
        [InlineData("StationId")]
        public void EachClassHasLinesForBubblyLaconicAndLateNight(string className) =>
            Assert.Equal(
                [GauntletRegister.Bubbly, GauntletRegister.Laconic, GauntletRegister.LateNight],
                set.Lines.Where(l => l.Class == Enum.Parse<GauntletClass>(className)).Select(l => l.Register).OfType<GauntletRegister>().Distinct().Order());
    }

    // AC3 — ids are stable and unique
    public sealed class ScenarioIdsAreStableAndUnique
    {
        private readonly string[] ids = GauntletSet.Load(ShippedDirectory).Lines.Select(l => l.Id).ToArray();

        [Fact]
        public void NoIdRepeats() => Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    // AC4 — the hash pins the content
    public sealed class ScenarioTheHashPinsTheContent
    {
        private readonly string first = GauntletSet.Load(ShippedDirectory).ContentHash;
        private readonly string second = GauntletSet.Load(ShippedDirectory).ContentHash;

        [Fact]
        public void BothHashesAreEqual() => Assert.Equal(first, second);

        [Fact]
        public void TheHashIs64LowercaseHexCharacters() => Assert.Matches("^[0-9a-f]{64}$", first);
    }

    // AC5 — any edit moves the hash
    public sealed class ScenarioAnyEditMovesTheHash : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly string original;
        private readonly string edited;

        public ScenarioAnyEditMovesTheHash()
        {
            string text = EightWords + " nine";
            original = GauntletSet.Load(fixtures.Create(("a.json", Lines(Line("x-1", "LeadIn", text))))).ContentHash;
            edited = GauntletSet.Load(fixtures.Create(("a.json", Lines(Line("x-1", "LeadIn", text + "s"))))).ContentHash;
        }

        [Fact]
        public void ItDiffersFromTheOriginalHash() => Assert.NotEqual(original, edited);

        public void Dispose() => fixtures.Dispose();
    }

    // AC5b — key order and whitespace do not move the hash
    public sealed class ScenarioFormattingDoesNotMoveTheHash : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly string compact;
        private readonly string spaced;

        public ScenarioFormattingDoesNotMoveTheHash()
        {
            compact = GauntletSet.Load(fixtures.Create(("a.json",
                """{"lines":[{"id":"x-1","class":"LeadIn","register":"bubbly","text":"one two three four five six seven eight","cues":[]}]}"""))).ContentHash;
            spaced = GauntletSet.Load(fixtures.Create(("a.json",
                """
                { "lines": [ { "cues": [], "text": "one two three four five six seven eight",
                  "register": "bubbly", "class": "LeadIn", "id": "x-1" } ] }
                """))).ContentHash;
        }

        [Fact]
        public void TheHashesAreEqual() => Assert.Equal(compact, spaced);

        public void Dispose() => fixtures.Dispose();
    }

    // AC5c — the station's rules files are part of the hash
    public sealed class ScenarioTheRulesFilesAreInTheHash : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly string without;
        private readonly string with;

        public ScenarioTheRulesFilesAreInTheHash()
        {
            without = GauntletSet.Load(fixtures.Create(("a.json", Lines(Line("x-1", "LeadIn", EightWords))))).ContentHash;
            with = GauntletSet.Load(fixtures.Create(
                ("a.json", Lines(Line("x-1", "LeadIn", EightWords))),
                ("corrections.json", """{ "provenance": "test", "corrections": [ { "from": "a", "to": "b" } ] }"""))).ContentHash;
        }

        [Fact]
        public void AddingACorrectionMovesTheHash() => Assert.NotEqual(without, with);

        public void Dispose() => fixtures.Dispose();
    }

    // AC5d — the shipped rules are real
    public sealed class ScenarioTheShippedCorrectionsAreReal
    {
        private readonly GauntletSet set = GauntletSet.Load(ShippedDirectory);

        [Fact]
        public void CorrectionsAreNotPending() => Assert.False(set.CorrectionsPending);
    }

    // AC5e — a pending file is flagged
    public sealed class ScenarioAPendingCorrectionsFileIsFlagged : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly GauntletSet set;

        public ScenarioAPendingCorrectionsFileIsFlagged() =>
            set = GauntletSet.Load(fixtures.Create(
                ("a.json", Lines(Line("x-1", "LeadIn", EightWords))),
                ("corrections.json", """{ "provenance": "pending", "corrections": [] }""")));

        [Fact]
        public void CorrectionsPendingIsTrue() => Assert.True(set.CorrectionsPending);

        public void Dispose() => fixtures.Dispose();
    }

    // AC6 — the station's normalizer runs first
    public sealed class ScenarioTheStationSNormalizerRunsFirst
    {
        private const string Text = "Next is a great one from MacLeod and it is {laugh} wonderful today.";
        private readonly string plain = CueText.Plain(Text, MacLeodCorrection, PronunciationRuleSet.Create([]), PlainLaneStyle.Bare);

        [Fact]
        public void ItEqualsSpeechTextNormalizeTextCorrectionsWithCueMarkersStripped() =>
            Assert.Equal(
                SpeechText.Normalize("Next is a great one from MacLeod and it is  wonderful today.", MacLeodCorrection),
                plain);

        [Fact]
        public void TheCorrectionWasApplied() => Assert.Contains("muh-cloud", plain, StringComparison.OrdinalIgnoreCase);
    }

    // AC7 — Kokoro's plain lane is what airs
    public sealed class ScenarioKokoroSPlainLaneIsWhatAirs
    {
        private const string Text = "Next is a great one from MacLeod. It is wonderful today, and I hope you like it.";
        private static readonly PronunciationRuleSet Rules =
            PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "məkˈlaʊd")]);
        private readonly string plain = CueText.Plain(Text, SpeechCorrectionSet.Create([]), Rules, PlainLaneStyle.StationSpeechMarkup);

        [Fact]
        public void ItEqualsKokoroSpeechMarkupRenderNormalizedTextPronunciationRules06() =>
            Assert.Equal(
                KokoroSpeechMarkup.Render(SpeechText.Normalize(Text, SpeechCorrectionSet.Create([])), Rules, 0.6),
                plain);

        [Fact]
        public void TheStationMarkupIsActuallyPresent() => Assert.Contains("(/", plain, StringComparison.Ordinal);
    }

    // AC8 — the markup lane maps cues
    public sealed class ScenarioTheMarkupLaneMapsCues
    {
        private readonly string markup = CueText.Markup(
            "I was reading the notes {laugh} and somebody asked for it three times.",
            SpeechCorrectionSet.Create([]), LaughsAndNoSigh);

        [Fact]
        public void TheCueIsReplacedByLaughs() => Assert.Contains("[laughs]", markup, StringComparison.Ordinal);
    }

    // AC8b — normalization still reaches the text around a cue
    public sealed class ScenarioCorrectionsApplyAroundACue
    {
        private readonly string markup = CueText.Markup(
            "A song from MacLeod {laugh} and a story about MacLeod too.", MacLeodCorrection, LaughsAndNoSigh);

        [Fact]
        public void BothSidesOfTheCueAreCorrected() =>
            Assert.Equal(2, Regex.Matches(markup, "muh-cloud", RegexOptions.IgnoreCase).Count);
    }

    // AC9 — a null mapping strips the cue
    public sealed class ScenarioANullMappingStripsTheCue
    {
        private readonly string markup = CueText.Markup(
            "It has been a long day {sigh} so here is something gentle.", SpeechCorrectionSet.Create([]), LaughsAndNoSigh);

        [Fact]
        public void TheTextContainsNoCueMarker() => Assert.DoesNotContain("{", markup, StringComparison.Ordinal);

        [Fact]
        public void TheWordsAroundItSurvive() =>
            Assert.Equal(
                SpeechText.Normalize("It has been a long day", SpeechCorrectionSet.Create([])) + " "
                    + SpeechText.Normalize("so here is something gentle.", SpeechCorrectionSet.Create([])),
                markup);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC10 — short lines are refused
    public sealed class ScenarioShortLinesAreRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioShortLinesAreRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("short-1", "LeadIn", "only five words in here")))));

        [Fact]
        public void LoadingFailsNamingThatLineId() => Assert.Contains("short-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11 — a duplicate id is refused
    public sealed class ScenarioADuplicateIdIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioADuplicateIdIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("dup-1", "LeadIn", EightWords))), ("b.json", Lines(Line("dup-1", "StationId", EightWords)))));

        [Fact]
        public void LoadingFailsNamingTheDuplicateId() => Assert.Contains("dup-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11b — an unknown cue token is refused
    public sealed class ScenarioAnUnknownCueIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAnUnknownCueIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("cue-1", "NonVerbal", EightWords + " {cackle}", "[\"cackle\"]")))));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("cue-1", failure.Message, StringComparison.Ordinal);

        [Fact]
        public void LoadingFailsNamingTheToken() => Assert.Contains("cackle", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11c — cues[] must match the tokens in the text
    public sealed class ScenarioCuesMustMatchTheText : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioCuesMustMatchTheText() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("mismatch-1", "NonVerbal", EightWords + " {laugh}", "[]")))));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("mismatch-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11d — a crosstalk line needs two turns
    public sealed class ScenarioACrosstalkNeedsTwoTurns : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioACrosstalkNeedsTwoTurns() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "talk-1", "class": "Crosstalk", "cues": [], "turns": [ { "register": "bubbly", "text": "one two three four five six seven eight" } ] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("talk-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11e — a crosstalk line takes turns, not text
    public sealed class ScenarioACrosstalkLineWithTextIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioACrosstalkLineWithTextIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "talk-2", "class": "Crosstalk", "cues": [], "text": "one two three four five six seven eight", "turns": [ { "register": "bubbly", "text": "one two three four" }, { "register": "laconic", "text": "five six seven eight" } ] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("talk-2", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11f — a single-voice line takes text, not turns
    public sealed class ScenarioASingleLineWithTurnsIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioASingleLineWithTurnsIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "solo-1", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [], "turns": [ { "register": "bubbly", "text": "one two three four" }, { "register": "laconic", "text": "five six seven eight" } ] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("solo-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11g — a file that is not JSON is refused naming the file
    public sealed class ScenarioAFileThatIsNotJsonIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAFileThatIsNotJsonIsRefused() => failure = LoadFailure(fixtures.Create(("broken.json", "this is not json")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("broken.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11h — an empty file is refused naming the file
    public sealed class ScenarioAnEmptyFileIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAnEmptyFileIsRefused() => failure = LoadFailure(fixtures.Create(("empty.json", "")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("empty.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11i — a null line is refused naming the file
    public sealed class ScenarioANullLineIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioANullLineIsRefused() => failure = LoadFailure(fixtures.Create(("nulls.json", """{ "lines": [ null ] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("nulls.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11j — a null turn is refused naming the line
    public sealed class ScenarioANullTurnIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioANullTurnIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "talk-3", "class": "Crosstalk", "cues": [], "turns": [ null, null ] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("talk-3", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11k — a numeric class is refused
    public sealed class ScenarioANumericClassIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioANumericClassIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "num-1", "class": "1", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("num-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11l — register names are exact-case
    public sealed class ScenarioAWronglyCasedRegisterIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAWronglyCasedRegisterIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "case-1", "class": "LeadIn", "register": "Bubbly", "text": "one two three four five six seven eight", "cues": [] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("case-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11m — a duplicate key is refused naming the file
    public sealed class ScenarioADuplicateKeyIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioADuplicateKeyIsRefused() => failure = LoadFailure(fixtures.Create(("dupe.json", """{ "lines": [ { "id": "ok-1", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ], "lines": [ { "id": "ok-2", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("dupe.json", failure.Message, StringComparison.Ordinal);

        [Fact]
        public void LoadingFailsSayingThePropertyIsDuplicated() => Assert.Contains("Duplicate property 'lines'", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11n — a lines file without lines is refused naming the file
    public sealed class ScenarioAFileWithoutLinesIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAFileWithoutLinesIsRefused() => failure = LoadFailure(fixtures.Create(("nolines.json", """{}""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("nolines.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11o — a null lines list is refused naming the file
    public sealed class ScenarioANullLinesListIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioANullLinesListIsRefused() => failure = LoadFailure(fixtures.Create(("nulllines.json", """{ "lines": null }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("nulllines.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11p — an unknown property is refused naming the file
    public sealed class ScenarioAnUnknownPropertyIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAnUnknownPropertyIsRefused() => failure = LoadFailure(fixtures.Create(("typo.json", """{ "lines": [ { "id": "typo-1", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ], "line": [] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("typo.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11q — a crosstalk line register is refused
    public sealed class ScenarioACrosstalkLineRegisterIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioACrosstalkLineRegisterIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "talk-4", "class": "Crosstalk", "register": "bubbly", "cues": [], "turns": [ { "register": "bubbly", "text": "one two three four" }, { "register": "laconic", "text": "five six seven eight" } ] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("talk-4", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11r — a null cue entry is refused
    public sealed class ScenarioANullCueIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioANullCueIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "cue-9", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [ null ] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineAndTheNullEntry() => Assert.Contains("cue-9 has a null entry in its cues", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11s — a missing corrections file is refused
    public sealed class ScenarioAMissingCorrectionsFileIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAMissingCorrectionsFileIsRefused() => failure = LoadFailure(fixtures.CreateBare(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("pronunciations.json", ValidPronunciations)));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("corrections.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11t — a missing pronunciations file is refused
    public sealed class ScenarioAMissingPronunciationsFileIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAMissingPronunciationsFileIsRefused() => failure = LoadFailure(fixtures.CreateBare(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("corrections.json", ValidCorrections)));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("pronunciations.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11u — an absent rules list is refused
    public sealed class ScenarioAMissingCorrectionsListIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAMissingCorrectionsListIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("corrections.json", """{ "provenance": "test" }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("corrections.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11v — a null rules list is refused
    public sealed class ScenarioANullPronunciationsListIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioANullPronunciationsListIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("pronunciations.json", """{ "provenance": "test", "pronunciations": null }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("pronunciations.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11w — a blank provenance is refused
    public sealed class ScenarioABlankProvenanceIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioABlankProvenanceIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("corrections.json", """{ "provenance": "  ", "corrections": [] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("corrections.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11x — a null rules entry is refused
    public sealed class ScenarioANullRulesEntryIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioANullRulesEntryIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("corrections.json", """{ "provenance": "test", "corrections": [ null ] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("null entry in its corrections", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11y — a blank correction \"from\" is refused
    public sealed class ScenarioABlankCorrectionFromIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioABlankCorrectionFromIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("corrections.json", """{ "provenance": "test", "corrections": [ { "from": " ", "to": "x" } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("corrections.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11z — a blank pronunciation field is refused
    public sealed class ScenarioABlankPronunciationFieldIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioABlankPronunciationFieldIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", Lines(Line("ok-1", "LeadIn", EightWords))), ("pronunciations.json", """{ "provenance": "test", "pronunciations": [ { "pattern": "a", "word": "a", "ipa": "" } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("pronunciations.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11aa — an empty lines array is refused
    public sealed class ScenarioAnEmptyLinesArrayIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAnEmptyLinesArrayIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("a.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11ab — an id that is not lowercase words and hyphens is refused
    public sealed class ScenarioABadIdShapeIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioABadIdShapeIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "Bad_Id", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheId() => Assert.Contains("Bad_Id", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11ab — an id that ends in a newline is not lowercase words and hyphens either
    public sealed class ScenarioAnIdWithATrailingNewlineIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAnIdWithATrailingNewlineIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "ok-id\n", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ] }""")));

        [Fact]
        public void LoadingFailsSayingTheIdShape() => Assert.Contains("lowercase words", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11ac — a cue listed twice is refused
    public sealed class ScenarioADuplicateCueIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioADuplicateCueIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "ok-1", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight {laugh}", "cues": ["laugh", "laugh"] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("ok-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11ad — turns: [] on a single-voice line is refused
    public sealed class ScenarioAnEmptyTurnsArrayOnASingleLineIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAnEmptyTurnsArrayOnASingleLineIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "ok-1", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [], "turns": [] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("ok-1", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11ae — a crosstalk turn with no words is refused
    public sealed class ScenarioABlankTurnTextIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioABlankTurnTextIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "talk-5", "class": "Crosstalk", "cues": ["laugh"], "turns": [ { "register": "bubbly", "text": "one two three four five six seven eight nine" }, { "register": "laconic", "text": "{laugh}" } ] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheLineId() => Assert.Contains("talk-5", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11af — a key repeated in another case is refused
    public sealed class ScenarioACaseVariantDuplicateKeyIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioACaseVariantDuplicateKeyIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "ok-1", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ], "Lines": [ { "id": "ok-2", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "cues": [] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("a.json", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC11ag — a wrongly cased property name is refused
    public sealed class ScenarioAWronglyCasedPropertyNameIsRefused : IDisposable
    {
        private readonly GauntletFixtureDirectory fixtures = new();
        private readonly InvalidDataException failure;

        public ScenarioAWronglyCasedPropertyNameIsRefused() => failure = LoadFailure(fixtures.Create(("a.json", """{ "lines": [ { "id": "ok-1", "class": "LeadIn", "register": "bubbly", "text": "one two three four five six seven eight", "Cues": [] } ] }""")));

        [Fact]
        public void LoadingFailsNamingTheFile() => Assert.Contains("a.json", failure.Message, StringComparison.Ordinal);

        [Fact]
        public void LoadingFailsSayingThePropertyIsUnmapped() => Assert.Contains("'Cues' could not be mapped", failure.Message, StringComparison.Ordinal);

        public void Dispose() => fixtures.Dispose();
    }

    // AC12 — pending corrections are visible
    public sealed class ScenarioPendingCorrectionsAreVisible : IDisposable
    {
        // Given corrections.json with "provenance":"pending"
        // When  render starts

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioPendingCorrectionsAreVisible()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            run = scenario.Run(
                scenario.EnginesDirectory, scenario.GauntletDirectory([GauntletFixtureDirectory.Line("leadin-1", "LeadIn", RenderScenario.Eight)], correctionsPending: true), "cpu");
        }

        [Fact]
        public void ItPrintsALineSayingCorrectionsArePending() =>
            Assert.Equal(RenderCommand.CorrectionsPendingNotice, run.OutputLines[0]);

        public void Dispose() => scenario.Dispose();
    }
}
