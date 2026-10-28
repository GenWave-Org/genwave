// STORY-524 — Same text in: raw, cue-mapped, words law (gh-#614 · SPEC F217 · PLAN T662)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

using GenWave.Tts;
using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Tests.Support;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureSameTextIn
{
    private const string Phone = "1-800-555-0199";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — a best-effort card keeps the script verbatim
    public sealed class ScenarioABestEffortCardKeepsTheScriptVerbatim
    {
        // Given the paragraph with 1-800-555-0199 and a best-effort card with no overrides
        // When  the card text is prepared
        private readonly ScriptFile script = SameTextScenario.Script();
        private readonly Card card = SameTextScenario.BestEffort("""{ "laugh": "[laugh]" }""");

        [Fact]
        public void ThePhoneNumberIsStillInTheText() =>
            Assert.Contains(Phone, SameTextScenario.Prepared(card, script, 2).Text, StringComparison.Ordinal);

        [Fact]
        public void TheParagraphsCapitalsAreKept() =>
            Assert.StartsWith("Okay, quick bit of housekeeping, 'cause I kinda forgot last hour. The request line", SameTextScenario.Prepared(card, script, 2).Text, StringComparison.Ordinal);

        [Fact]
        public void TheParagraphsEllipsisIsKept() =>
            Assert.Contains("Weather today is... gorgeous", SameTextScenario.Prepared(card, script, 5).Text, StringComparison.Ordinal);
    }

    // AC2 — a cue becomes the card's tag
    public sealed class ScenarioACueBecomesTheCardSTag
    {
        // Given a card mapping laugh to [laugh]
        // When  the laugh paragraph is prepared
        private readonly ScriptFile script = SameTextScenario.Script();
        private readonly Card card = SameTextScenario.BestEffort("""{ "laugh": "[laugh]" }""");

        [Fact]
        public void LaughStandsWhereLaughWas() =>
            Assert.Contains("man [laugh] I love me some Metallica in the morning!", SameTextScenario.Prepared(card, script, 7).Text, StringComparison.Ordinal);
    }

    // AC3 — a cue becomes a written stand-in
    public sealed class ScenarioACueBecomesAWrittenStandIn
    {
        // Given a card mapping um to the word um followed by a comma
        // When  the um paragraph is prepared
        private readonly ScriptFile script = SameTextScenario.Script();
        private readonly Card card = SameTextScenario.BestEffort("""{ "um": "Um," }""");

        [Fact]
        public void TheStandInStandsWhereUmWas() =>
            Assert.StartsWith("Um, Oh! And the bake sale", SameTextScenario.Prepared(card, script, 3).Text, StringComparison.Ordinal);
    }

    // AC4 — an unmapped cue is dropped and declared
    public sealed class ScenarioAnUnmappedCueIsDroppedAndDeclared
    {
        // Given a card mapping cough to null
        // When  the cough paragraph is prepared
        private readonly ScriptFile script = SameTextScenario.Script();
        private readonly Card card = SameTextScenario.BestEffort("""{ "cough": null, "giggle": "Hehe!" }""", null, "cough");

        [Fact]
        public void NoCoughTokenRemains()
        {
            string text = SameTextScenario.Prepared(card, script, 4).Text;

            Assert.DoesNotContain("cough", text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Sorry. Hehe! Allergies.", text);
        }

        [Fact]
        public void CoughIsReportedAsDropped() =>
            Assert.Contains("cough", SameTextScenario.Prepared(card, script, 4).DroppedCues);
    }

    // AC5 — an override wins for its paragraph
    public sealed class ScenarioAnOverrideWinsForItsParagraph
    {
        // Given a card overriding paragraph 3 with the same words and an extra ellipsis
        // When  the card text is prepared
        private readonly ScriptFile script = SameTextScenario.Script();
        private readonly string overridden;
        private readonly Card card;

        public ScenarioAnOverrideWinsForItsParagraph()
        {
            overridden = SameTextScenario.Source(script, 3).Replace("{um} Oh! And", "Oh!... And", StringComparison.Ordinal);
            card = SameTextScenario.BestEffort("""{ "um": null }""", new Dictionary<int, string> { [3] = overridden }, "um");
        }

        [Fact]
        public void Paragraph3EqualsTheOverride() =>
            Assert.Equal(overridden, SameTextScenario.Prepared(card, script, 3).Text);

        [Fact]
        public void Paragraph4IsUnchanged() =>
            Assert.Equal(
                SameTextScenario.Prepared(SameTextScenario.BestEffort("""{ "um": null }""", cannot: "um"), script, 4).Text,
                SameTextScenario.Prepared(card, script, 4).Text);
    }

    // AC6 — the as-shipped card speaks the station style
    public sealed class ScenarioTheAsShippedCardSpeaksTheStationStyle
    {
        // Given the asShipped card
        // When  its text is prepared
        private readonly ScriptFile script = SameTextScenario.Script();
        private readonly Card card = SameTextScenario.ShippedCard("kokoro-as-shipped");

        [Fact]
        public void EveryCueIsStripped() =>
            Assert.All(CardText.Prepare(card, script), p => Assert.DoesNotContain('{', p.Text));

        [Fact]
        public void TheTextEqualsKokoroSpeechMarkupOverTheNormalisedCueStrippedParagraph()
        {
            Assert.All(script.Paragraphs, paragraph =>
            {
                string normalised = SpeechText.Normalize(CardText.ReplaceCues(paragraph.Text, new Dictionary<string, string?>()), AsShippedStation.Corrections);
                string expected = KokoroSpeechMarkup.Render(normalised, AsShippedStation.Rules, AsShippedStation.SentencePauseSeconds);

                Assert.Equal(expected, CardText.Prepare(card, paragraph).Text);
            });
        }

        [Fact]
        public void ThePhoneParagraphHasNoRawPhoneNumberLeft() =>
            Assert.DoesNotContain(Phone, SameTextScenario.Prepared(card, script, 2).Text, StringComparison.Ordinal);
    }

    // AC7 — the words law passes for tags and punctuation
    public sealed class ScenarioTheWordsLawPassesForTagsAndPunctuation
    {
        // Given a card whose override adds [sigh] and a comma
        // When  the words law runs
        private readonly ScriptFile script = SameTextScenario.Script();

        [Fact]
        public void ItPasses()
        {
            string text = "It just doesn't get any better than that, really... [sigh] Man oh man, I love me some Metallica in the morning!";
            Card card = SameTextScenario.BestEffort(
                """{ "sigh": "[sigh]", "laugh": null }""", new Dictionary<int, string> { [7] = text }, "laugh");

            Assert.Empty(WordsLaw.Check(card, CardText.Prepare(card, script)));
        }
    }

    // AC7b — every shipped card keeps the script's words
    public sealed class ScenarioEveryShippedCardKeepsTheScriptSWords
    {
        // Given the shipped card set and the script
        // When  the words law runs over every card
        private readonly ScriptFile script = SameTextScenario.Script();

        [Fact]
        public void NoCardBreaksIt()
        {
            string[] broken = Directory.GetFiles(SameTextScenario.CardsDirectory, "*.json")
                .Select(path => SameTextScenario.ShippedCard(Path.GetFileNameWithoutExtension(path)))
                .Where(card => WordsLaw.Check(card, CardText.Prepare(card, script)).Count > 0)
                .Select(card => card.Id)
                .ToArray();

            Assert.Empty(broken);
        }
    }

    // AC7c — pronunciation markup is not an undeclared tag
    public sealed class ScenarioPronunciationMarkupIsNotAnUndeclaredTag
    {
        // Given a best-effort card and a paragraph whose word w is written [w](/i/)
        // When  the words law runs
        private readonly Card card = SameTextScenario.BestEffort("{}");
        private readonly ParagraphText marked = new(1, "Say [w](/i/) now", "Say w now", []);

        [Fact]
        public void ItPasses() =>
            Assert.Empty(WordsLaw.Check(card, [marked]));

        [Fact]
        public void TheSpokenWordIsTheWordInsideTheBrackets()
        {
            WordsLawFailure failure = Assert.Single(WordsLaw.Check(card, [marked with { Expected = "Say x now" }]));

            Assert.Contains("got \"w\"", failure.Message, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — a changed word fails the words law
    public sealed class ScenarioAChangedWordFailsTheWordsLaw
    {
        // Given an override replacing kinda with kind of
        // When  the words law runs
        [Fact]
        public void ItFailsNamingTheParagraphNumber()
        {
            ScriptFile script = SameTextScenario.Script();
            string text = SameTextScenario.Source(script, 2).Replace("kinda", "kind of", StringComparison.Ordinal);
            Card card = SameTextScenario.BestEffort("{}", new Dictionary<int, string> { [2] = text });

            WordsLawFailure failure = Assert.Single(WordsLaw.Check(card, CardText.Prepare(card, script)));

            Assert.Equal(2, failure.Paragraph);
            Assert.Contains("Paragraph 2", failure.Message, StringComparison.Ordinal);
        }
    }

    // AC9 — a dropped word fails the words law
    public sealed class ScenarioADroppedWordFailsTheWordsLaw
    {
        // Given an override missing the word GenWave
        // When  the words law runs
        [Fact]
        public void ItFailsNamingTheParagraphNumber()
        {
            ScriptFile script = SameTextScenario.Script();
            string text = "Good morning, you're listening to, and that was Juniper Hollis with Paper Lanterns. Honestly? I could play that one all day.";
            Card card = SameTextScenario.BestEffort("{}", new Dictionary<int, string> { [1] = text });

            WordsLawFailure failure = Assert.Single(WordsLaw.Check(card, CardText.Prepare(card, script)));

            Assert.Equal(1, failure.Paragraph);
            Assert.Contains("Paragraph 1", failure.Message, StringComparison.Ordinal);
        }
    }

    // AC10 — an undeclared tag fails the words law
    public sealed class ScenarioAnUndeclaredTagFailsTheWordsLaw
    {
        // Given an override using [gasp] when the card declares only [laugh]
        // When  the words law runs
        [Fact]
        public void ItFailsNamingTheTag()
        {
            ScriptFile script = SameTextScenario.Script();
            string text = SameTextScenario.Source(script, 1).Replace("{breath}", "[gasp]", StringComparison.Ordinal).Replace("{laugh}", "[laugh]", StringComparison.Ordinal);
            Card card = SameTextScenario.BestEffort(
                """{ "laugh": "[laugh]", "breath": null }""", new Dictionary<int, string> { [1] = text }, "breath");

            WordsLawFailure failure = Assert.Single(WordsLaw.Check(card, CardText.Prepare(card, script)));

            Assert.Equal("[gasp]", failure.Tag);
            Assert.Contains("[gasp]", failure.Message, StringComparison.Ordinal);
        }
    }
}
