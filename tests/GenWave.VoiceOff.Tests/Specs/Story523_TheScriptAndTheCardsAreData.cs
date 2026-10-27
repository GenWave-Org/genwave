// STORY-523 — The script and the cards are data (gh-#614 · SPEC F217 · PLAN T661)
//
// BDD specification — xUnit. AC1–AC6 and AC8–AC11 are live (T661); AC7 and the set-wide card checks are live (T670).

using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheScriptAndTheCardsAreData
{
    private static string ToolDirectory => Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff");

    private static string ShippedCardsDirectory => Path.Combine(ToolDirectory, "cards");

    private static string ShippedScriptPath => Path.Combine(ToolDirectory, "script", "script.json");

    private static EngineProfileSet Engines => EngineProfileSet.Load(RealEngines.Directory);

    private static string EngineSlug => Engines.Baseline.Slug;

    private static string Reference(string engine) =>
        CardFixtureDirectory.CardJson("reference-card", engine, extraMembers: "\"asShipped\": true,");

    private static string Tuned(string engine, string host = "thor-cpu", string cues = """{ "laugh": "Haha!" }""") =>
        CardFixtureDirectory.CardJson("tuned-card", engine, host, cues, extraMembers: "\"variantOf\": \"reference-card\",");

    private static InvalidDataException LoadFailure(CardFixtureDirectory fixture, params (string FileName, string Json)[] files) =>
        Assert.Throws<InvalidDataException>(() => CardSet.Load(fixture.Create(files), Engines));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the script loads
    public sealed class ScenarioTheScriptLoads
    {
        // Given tools/VoiceOff/script/script.json
        // When  it is loaded
        private readonly ScriptFile script = ScriptFile.Load(ShippedScriptPath);

        [Fact]
        public void ItHoldsEightParagraphs()
        {
            Assert.Equal(8, script.Paragraphs.Count);
        }

        [Fact]
        public void ItsVersionIsV2()
        {
            Assert.Equal("v3", script.Version);
        }

        [Fact]
        public void EveryParagraphHasA1BasedNumberAndNonEmptyText()
        {
            Assert.Equal(Enumerable.Range(1, 8), script.Paragraphs.Select(p => p.Number));
            Assert.All(script.Paragraphs, p => Assert.False(string.IsNullOrWhiteSpace(p.Text)));
        }
    }

    // AC2 — the script hash is stable
    public sealed class ScenarioTheScriptHashIsStable
    {
        // Given the script file
        // When  it is loaded twice

        [Fact]
        public void BothLoadsReportTheSameSHA256Hash()
        {
            string first = ScriptFile.Load(ShippedScriptPath).Hash;
            string second = ScriptFile.Load(ShippedScriptPath).Hash;

            Assert.Matches("^[0-9a-f]{64}$", first);
            Assert.Equal(first, second);
        }
    }

    // AC3 — the script hash moves with the text
    public sealed class ScenarioTheScriptHashMovesWithTheText
    {
        // Given the script with one character changed
        // When  it is loaded

        [Fact]
        public void TheHashDiffersFromTheShippedHash()
        {
            string json = File.ReadAllText(ShippedScriptPath);
            string changed = json.Replace("Good morning", "Good mornin");

            Assert.NotEqual(json, changed);
            Assert.NotEqual(ScriptFile.Load(ShippedScriptPath).Hash, ScriptFile.Parse(changed, "changed.json").Hash);
        }
    }

    // AC4 — the cue vocabulary covers the script
    public sealed class ScenarioTheCueVocabularyCoversTheScript
    {
        // Given the seven cues breath, laugh, um, cough, giggle, sigh and pause
        // When  the script's cue tokens are listed
        private readonly ScriptFile script = ScriptFile.Load(ShippedScriptPath);

        [Fact]
        public void EveryTokenIsOneOfTheSeven()
        {
            string[] seven = ["breath", "laugh", "um", "cough", "giggle", "sigh", "pause"];

            Assert.True(CueVocabulary.Script.SetEquals(seven));
            Assert.NotEmpty(script.CueTokens());
            Assert.All(script.CueTokens(), token => Assert.Contains(token, seven));
        }

        [Fact]
        public void NoOtherTokenShapeAppears()
        {
            string leftover = string.Concat(script.Paragraphs.Select(p => CueVocabulary.Token().Replace(p.Text, string.Empty)));

            Assert.DoesNotContain('{', leftover);
            Assert.DoesNotContain('}', leftover);
        }
    }

    // AC5 — a card loads
    public sealed class ScenarioACardLoads : IDisposable
    {
        // Given a card file with engine, host, voice, speed, settings, cues, overrides, facts and why
        // When  it is loaded
        private readonly CardFixtureDirectory fixture = new();
        private readonly Card card;

        public ScenarioACardLoads()
        {
            string directory = fixture.Create(("reference-card.json", Reference(EngineSlug)));
            card = CardSet.Load(directory, Engines).Cards.Single();
        }

        public void Dispose() => fixture.Dispose();

        [Fact]
        public void ItsEngineNamesAProfileInEngines()
        {
            Assert.Contains(Engines.Profiles, p => p.Slug == card.Engine);
        }

        [Fact]
        public void ItsHostIsOneOfThorGpuThorCpuRunesGpu()
        {
            Assert.Equal(CardHost.ThorCpu, card.Host);
        }

        [Fact]
        public void ItsSettingsObjectIsKeptVerbatim()
        {
            Assert.Equal("""{"alpha":0.3,"nested":{"keep":[1,"two",null]}}""", card.Settings.ToJsonString());
        }

        [Fact]
        public void ItsFactsCarryACannotList()
        {
            Assert.Equal(["breath", "sigh"], card.Facts.Cannot);
        }

        [Fact]
        public void ItsWhyIsNonEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Why));
        }
    }

    // AC6 — a variant names its sibling
    public sealed class ScenarioAVariantNamesItsSibling : IDisposable
    {
        // Given a card with variantOf
        // When  the card set is loaded
        private readonly CardFixtureDirectory fixture = new();
        private readonly CardSet set;

        public ScenarioAVariantNamesItsSibling() =>
            set = CardSet.Load(fixture.Create(("reference-card.json", Reference(EngineSlug)), ("tuned-card.json", Tuned(EngineSlug))), Engines);

        public void Dispose() => fixture.Dispose();

        [Fact]
        public void TheSiblingIsACardInTheSameSet()
        {
            Card variant = set.Cards.Single(c => c.VariantOf is not null);

            Assert.Contains(set.Cards, c => c.Id == variant.VariantOf);
        }
    }

    // AC7 — the shipped card set is complete
    public sealed class ScenarioTheShippedCardSetIsComplete
    {
        // Given tools/VoiceOff/cards
        // When  the set is loaded
        private readonly CardSet set = CardSet.Load(ShippedCardsDirectory, Engines);

        [Fact]
        public void ThereIsAtLeastOneCardPerProfileInEngines()
        {
            string[] uncovered = Engines.Profiles.Select(p => p.Slug).Where(slug => set.Cards.All(c => c.Engine != slug)).ToArray();

            Assert.Empty(uncovered);
        }

        [Fact]
        public void ExactlyOneCardIsMarkedAsShipped()
        {
            Assert.Single(set.Cards, c => c.AsShipped);
        }

        [Fact]
        public void TheFiveVariantCardsNamedInPROJECTMdArePresent()
        {
            string[] variants = ["chatterbox-original", "cosyvoice-normaliser-on", "parler-mini-expresso", "dia2", "vibevoice-7b"];

            Assert.Equal(variants.Order(StringComparer.Ordinal), set.Cards.Where(c => c.VariantOf is not null).Select(c => c.Id).Order(StringComparer.Ordinal));
        }
    }

    // AC7b — every shipped card says what it cannot do
    public sealed class ScenarioEveryShippedCardDeclaresWhatItCannotDo
    {
        // Given the shipped cards
        // When  each card's dropped cues and cannot facts are compared
        private static readonly string[] Seven = ["breath", "laugh", "um", "cough", "giggle", "sigh", "pause"];

        private readonly IReadOnlyList<Card> cards = CardSet.Load(ShippedCardsDirectory, Engines).Cards;

        [Fact]
        public void EveryDroppedCueIsInCannot()
        {
            Assert.All(cards, card =>
            {
                string[] dropped = Seven.Where(cue => !card.Cues.TryGetValue(cue, out string? tag) || tag is null).ToArray();

                Assert.Empty(dropped.Except(card.Facts.Cannot));
            });
        }

        [Fact]
        public void EveryCannotEntryIsOneOfTheSeven()
        {
            Assert.All(cards, card => Assert.Empty(card.Facts.Cannot.Except(Seven)));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — an unknown engine is refused
    public sealed class ScenarioAnUnknownEngineIsRefused
    {
        // Given a card naming an engine with no profile
        // When  the set is loaded
        private readonly InvalidDataException failure;

        public ScenarioAnUnknownEngineIsRefused()
        {
            using CardFixtureDirectory fixture = new();
            failure = LoadFailure(fixture, ("reference-card.json", Reference(EngineSlug)), ("tuned-card.json", Tuned("no-such-engine")));
        }

        [Fact]
        public void LoadingFailsNamingTheCardId()
        {
            Assert.Contains("tuned-card", failure.Message);
            Assert.Contains("no-such-engine", failure.Message);
        }
    }

    // AC9 — an unknown host is refused
    public sealed class ScenarioAnUnknownHostIsRefused
    {
        // Given a card with host laptop
        // When  the set is loaded
        private readonly InvalidDataException failure;

        public ScenarioAnUnknownHostIsRefused()
        {
            using CardFixtureDirectory fixture = new();
            failure = LoadFailure(fixture, ("reference-card.json", Reference(EngineSlug)), ("tuned-card.json", Tuned(EngineSlug, host: "laptop")));
        }

        [Fact]
        public void LoadingFailsNamingTheCardIdAndTheHost()
        {
            Assert.Contains("tuned-card", failure.Message);
            Assert.Contains("laptop", failure.Message);
        }
    }

    // AC10 — an unknown cue key is refused
    public sealed class ScenarioAnUnknownCueKeyIsRefused
    {
        // Given a card whose cues map a key hiccup
        // When  the set is loaded
        private readonly InvalidDataException failure;

        public ScenarioAnUnknownCueKeyIsRefused()
        {
            using CardFixtureDirectory fixture = new();
            failure = LoadFailure(fixture, ("reference-card.json", Reference(EngineSlug)), ("tuned-card.json", Tuned(EngineSlug, cues: """{ "hiccup": "Hic!" }""")));
        }

        [Fact]
        public void LoadingFailsNamingTheKey()
        {
            Assert.Contains("hiccup", failure.Message);
        }
    }

    // AC11 — a duplicate id is refused
    public sealed class ScenarioADuplicateIdIsRefused
    {
        // Given two card files sharing an id
        // When  the set is loaded
        private readonly InvalidDataException failure;

        public ScenarioADuplicateIdIsRefused()
        {
            using CardFixtureDirectory fixture = new();
            failure = LoadFailure(fixture, ("a.json", Reference(EngineSlug)), ("b.json", Reference(EngineSlug)));
        }

        [Fact]
        public void LoadingFailsNamingTheId()
        {
            Assert.Contains("reference-card", failure.Message);
        }
    }

    // AC12 — an id that is not strict kebab case is refused
    public sealed class ScenarioAnIdThatIsNotStrictKebabCaseIsRefused
    {
        // Given card ids with a doubled, leading or trailing hyphen
        // When  each card is read
        private static InvalidDataException ReadFailure(string id) =>
            Assert.Throws<InvalidDataException>(() => CardReader.Parse(CardFixtureDirectory.CardJson(id, EngineSlug), "bad.json"));

        [Fact]
        public void ADoubledHyphenIsRefusedNamingTheCardId() =>
            Assert.Contains("double--hyphen", ReadFailure("double--hyphen").Message, StringComparison.Ordinal);

        [Fact]
        public void ALeadingHyphenIsRefused() =>
            Assert.Contains("kebab", ReadFailure("-leading").Message, StringComparison.Ordinal);

        [Fact]
        public void ATrailingHyphenIsRefused() =>
            Assert.Contains("kebab", ReadFailure("trailing-").Message, StringComparison.Ordinal);
    }
}
