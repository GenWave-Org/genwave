using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Tests.Deck;

public sealed class CardSetTests : IDisposable
{
    private static readonly EngineProfileSet Engines = EngineProfileSet.Load(RealEngines.Directory);
    private readonly CardFixtureDirectory fixture = new();

    private static string Engine => Engines.Baseline.Slug;

    private static string Shipped(string id = "reference-card") =>
        CardFixtureDirectory.CardJson(id, Engine, extraMembers: "\"asShipped\": true,");

    private InvalidDataException Fails(params (string FileName, string Json)[] files) =>
        Assert.Throws<InvalidDataException>(() => CardSet.Load(fixture.Create(files), Engines));

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void TheShippedCardsLoad()
    {
        string cards = Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff", "cards");

        CardSet set = CardSet.Load(cards, Engines);

        Assert.Equal("kokoro-as-shipped", set.AsShipped.Id);
        Assert.Contains(set.Cards, c => c.Id == "kokoro");
    }

    [Fact]
    public void ASetWithNoAsShippedCardIsRefused()
    {
        InvalidDataException failure = Fails(("a.json", CardFixtureDirectory.CardJson("plain-card", Engine)));

        Assert.Contains("asShipped", failure.Message);
    }

    [Fact]
    public void TwoAsShippedCardsAreRefusedNamingBoth()
    {
        InvalidDataException failure = Fails(("a.json", Shipped("first-card")), ("b.json", Shipped("second-card")));

        Assert.Contains("first-card", failure.Message);
        Assert.Contains("second-card", failure.Message);
    }

    [Fact]
    public void AVariantOfAMissingSiblingIsRefused()
    {
        string orphan = CardFixtureDirectory.CardJson("orphan-card", Engine, extraMembers: "\"variantOf\": \"ghost-card\",");

        InvalidDataException failure = Fails(("a.json", Shipped()), ("b.json", orphan));

        Assert.Contains("orphan-card", failure.Message);
        Assert.Contains("ghost-card", failure.Message);
    }

    [Fact]
    public void AnUnknownMemberIsRefused()
    {
        string extra = CardFixtureDirectory.CardJson("extra-card", Engine, extraMembers: "\"mood\": \"upbeat\",");

        Assert.Throws<InvalidDataException>(() => CardReader.Parse(extra, "extra.json"));
    }

    [Fact]
    public void ADuplicateKeyIsRefused()
    {
        string duplicated = CardFixtureDirectory.CardJson("dupe-card", Engine, extraMembers: "\"voice\": \"voice-b\",");

        Assert.Throws<InvalidDataException>(() => CardReader.Parse(duplicated, "dupe.json"));
    }

    [Fact]
    public void ABadIdShapeIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => CardReader.Parse(CardFixtureDirectory.CardJson("Bad_Id", Engine), "bad.json"));
    }

    [Theory]
    [InlineData("54", false, 54)]
    [InlineData("\"clone\"", true, null)]
    public void VoicesIsANumberOrTheWordClone(string voices, bool isClone, int? count)
    {
        string json = CardFixtureDirectory.CardJson("voices-card", Engine).Replace("\"voices\": 54", $"\"voices\": {voices}", StringComparison.Ordinal);

        Card card = CardReader.Parse(json, "voices.json");

        Assert.Equal(isClone, card.Facts.Voices.IsClone);
        Assert.Equal(count, card.Facts.Voices.Count);
    }

    [Fact]
    public void VoicesNamingAnythingElseIsRefused()
    {
        string json = CardFixtureDirectory.CardJson("voices-card", Engine).Replace("\"voices\": 54", "\"voices\": \"many\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => CardReader.Parse(json, "voices.json"));
    }
}
