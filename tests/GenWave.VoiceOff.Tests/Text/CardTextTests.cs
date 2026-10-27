using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Tests.Support;
using GenWave.Tts;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Tests.Text;

public sealed class CardTextTests
{
    private readonly ScriptFile script = SameTextScenario.Script();

    [Theory]
    [InlineData("kokoro-as-shipped")]
    [InlineData("kokoro")]
    public void EachShippedCardPassesTheWordsLawOverEveryParagraph(string cardId)
    {
        Card card = SameTextScenario.ShippedCard(cardId);

        IReadOnlyList<ParagraphText> prepared = CardText.Prepare(card, script);

        Assert.Equal(8, prepared.Count);
        Assert.Empty(WordsLaw.Check(card, prepared));
    }

    [Fact]
    public void TheBestEffortKokoroCardPrintsThePhoneParagraphRaw() =>
        Assert.Contains("1-800-555-0199", SameTextScenario.Prepared(SameTextScenario.ShippedCard("kokoro"), script, 2).Text, StringComparison.Ordinal);

    [Fact]
    public void AStandInWordMovedAwayFromItsCueFailsTheLaw()
    {
        Card card = SameTextScenario.BestEffort("""{ "um": "Um," }""");
        ParagraphText p = SameTextScenario.Prepared(card, script, 3);
        ParagraphText moved = p with { Text = p.Text.Replace("Um, ", string.Empty, StringComparison.Ordinal) };

        Assert.Equal(3, Assert.Single(WordsLaw.Check(card, [moved])).Paragraph);
    }

    [Fact]
    public void AnOverrideThatDropsTheDollarSignsFailsTheLawNamingParagraph3()
    {
        string dollarless = SameTextScenario.Source(script, 3).Replace("{um} ", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal);
        Card card = SameTextScenario.BestEffort(
            """{ "um": null }""", new Dictionary<int, string> { [3] = dollarless }, "um");

        Assert.Equal(3, Assert.Single(WordsLaw.Check(card, [SameTextScenario.Prepared(card, script, 3)])).Paragraph);
    }

    [Fact]
    public void TheAsShippedMorningExclamationHasNoSpaceBeforeIt()
    {
        Card card = SameTextScenario.ShippedCard("kokoro-as-shipped");
        ScriptParagraph p7 = script.Paragraphs[6];
        string expected = SpeechText.Normalize(CardText.ReplaceCues(p7.Text, new Dictionary<string, string?>()), AsShippedStation.Corrections);

        Assert.DoesNotContain(" !", CardText.Prepare(card, p7).Expected, StringComparison.Ordinal);
        Assert.Equal(expected, CardText.Prepare(card, p7).Expected);
    }

    [Fact]
    public void TheRealWordUmInParagraph6IsNotMistakenForAStandIn()
    {
        Card card = SameTextScenario.BestEffort("""{ "um": "Um," }""");

        Assert.Empty(WordsLaw.Check(card, [SameTextScenario.Prepared(card, script, 6)]));
    }
}
