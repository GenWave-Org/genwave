// STORY-525 — Per paragraph, same seams (gh-#614 · SPEC F217 · PLAN T663)
//
// BDD specification — xUnit. LIVE at T663. Every scenario drives the real CLI (Cli.Run) over a stub engine
// and the repo's real script; the real ffmpeg joins and loudness-matches.

using System.Text.Json.Nodes;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeaturePerParagraphSameSeams
{
    private const string CardId = "alpha-card";

    private static JsonNode ReadRunJson(RenderRun run) =>
        JsonNode.Parse(File.ReadAllText(run.PathIn("run.json"))) ?? throw new InvalidOperationException("run.json is empty");

    private static JsonNode CardEntry(RenderRun run, string id) =>
        ReadRunJson(run)["cards"]?[id] ?? throw new InvalidOperationException($"run.json has no card {id}");

    private static double DurationMs(string path) =>
        WavInfo.Read(File.ReadAllBytes(path), out _)?.DurationMs ?? throw new InvalidOperationException($"{path} is not a WAV");

    /// <summary>One as-shipped card on a stub engine, rendered once through the CLI.</summary>
    private sealed class OneCardRun : IDisposable
    {
        private readonly RenderScenario scenario = new();

        internal OneCardRun()
        {
            Stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", Stub.BaseUri);
            scenario.AddCard(CardId, "alpha");
            Run = scenario.Run("first");
        }

        internal EngineStubServer Stub { get; }

        internal RenderRun Run { get; }

        internal string EnginesDirectory => scenario.EnginesDirectory;

        internal string CardsDirectory => scenario.CardsDirectory;

        internal string ScriptPath => scenario.ScriptPath;

        internal string RawPath(int n) => Run.PathIn(Path.Combine("raw", CardId, n + ".wav"));

        internal string ClipPath => Run.PathIn(Path.Combine("cards", CardId + ".wav"));

        public void Dispose() => scenario.Dispose();
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — eight requests per card
    public sealed class ScenarioEightRequestsPerCard : IDisposable
    {
        // Given one card and a stub engine
        // When  render runs

        private readonly OneCardRun fixture = new();

        [Fact]
        public void TheStubReceivedEightSpeechRequests() => Assert.Equal(8, fixture.Stub.Requests.SpeechRequests);

        [Fact]
        public void TheRequestsArriveInParagraphOrderWithTheCardSText()
        {
            GenWave.VoiceOff.Speech.EngineProfileSet engines = GenWave.VoiceOff.Speech.EngineProfileSet.Load(fixture.EnginesDirectory);
            GenWave.VoiceOff.Deck.Card card = GenWave.VoiceOff.Deck.CardSet.Load(fixture.CardsDirectory, engines).Cards.Single(c => c.Id == CardId);
            string[] expected = GenWave.VoiceOff.Text.CardText.Prepare(card, GenWave.VoiceOff.Text.ScriptFile.Load(fixture.ScriptPath)).Select(p => p.Text).ToArray();
            Assert.Equal(expected, fixture.Stub.Requests.SpeechInputs);
        }

        public void Dispose() => fixture.Dispose();
    }

    // AC2 — the request carries the card
    public sealed class ScenarioTheRequestCarriesTheCard : IDisposable
    {
        // Given the same run
        // When  the request bodies are read

        private readonly OneCardRun fixture = new();

        private IEnumerable<JsonNode> Bodies => fixture.Stub.Requests.SpeechBodies.Select(b => JsonNode.Parse(b) ?? throw new InvalidOperationException("empty body"));

        [Fact]
        public void EachBodyCarriesTheCardSVoice() => Assert.All(Bodies, b => Assert.Equal("voice-a", b["voice"]?.GetValue<string>()));

        [Fact]
        public void EachBodyCarriesTheCardSSpeed() => Assert.All(Bodies, b => Assert.Equal(1.1, b["speed"]?.GetValue<double>()));

        [Fact]
        public void EachBodyCarriesTheCardSSettingsObject() =>
            Assert.All(Bodies, b => Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "alpha": 0.3, "nested": { "keep": [1, "two", null] } }"""), b["settings"])));

        public void Dispose() => fixture.Dispose();
    }

    // AC3 — raw paragraphs are kept
    public sealed class ScenarioRawParagraphsAreKept : IDisposable
    {
        // Given the same run
        // When  raw/<card>/ is listed

        private readonly OneCardRun fixture = new();

        [Fact]
        public void EightWavFilesNumbered1To8Exist() =>
            Assert.Equal(
                Enumerable.Range(1, 8).Select(n => n + ".wav"),
                Directory.GetFiles(fixture.Run.PathIn(Path.Combine("raw", CardId))).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal));

        public void Dispose() => fixture.Dispose();
    }

    // AC4 — the paragraphs are joined
    public sealed class ScenarioTheParagraphsAreJoined : IDisposable
    {
        // Given the same run
        // When  cards/<card>.wav is read

        private readonly OneCardRun fixture = new();

        [Fact]
        public void ItExists() => Assert.True(File.Exists(fixture.ClipPath));

        [Fact]
        public void ItsDurationIsAtLeastTheSumOfTheEightPartsPlusSevenGaps()
        {
            double parts = Enumerable.Range(1, 8).Sum(n => DurationMs(fixture.RawPath(n)));
            Assert.True(DurationMs(fixture.ClipPath) >= parts + 7 * 500, $"clip {DurationMs(fixture.ClipPath)} ms, parts {parts} ms");
        }

        public void Dispose() => fixture.Dispose();
    }

    // AC5 — the clip is loudness-matched
    public sealed class ScenarioTheClipIsLoudnessMatched : IDisposable
    {
        // Given the same run
        // When  the clip's loudness is measured

        private readonly OneCardRun fixture = new();

        [Fact]
        public async Task IntegratedLoudnessIsWithin1LUOfMinus16()
        {
            var measured = await new GenWave.Loudness.FfmpegLoudnessAnalyzer().AnalyzeAsync(fixture.ClipPath, CancellationToken.None);
            Assert.InRange(measured.IntegratedLufs, -17, -15);
        }

        public void Dispose() => fixture.Dispose();
    }

    // AC6 — renders.jsonl records every paragraph
    public sealed class ScenarioRendersJsonlRecordsEveryParagraph : IDisposable
    {
        // Given the same run
        // When  renders.jsonl is read

        private readonly OneCardRun fixture = new();
        private readonly string[] lines;

        public ScenarioRendersJsonlRecordsEveryParagraph() => lines = File.ReadAllLines(fixture.Run.PathIn("renders.jsonl"));

        [Fact]
        public void ItHasEightLinesForTheCard() =>
            Assert.Equal(8, lines.Count(l => JsonNode.Parse(l)?["card"]?.GetValue<string>() == CardId));

        [Fact]
        public void EachLineCarriesCardNCharsWallMsAudioMsAndOk() =>
            Assert.All(lines, l => Assert.Empty(
                new[] { "audioMs", "card", "chars", "n", "ok", "wallMs" }.Except((JsonNode.Parse(l)?.AsObject() ?? throw new InvalidOperationException("empty line")).Select(p => p.Key))));

        [Fact]
        public void TheLinesAreNumbered1To8InOrder() =>
            Assert.Equal(Enumerable.Range(1, 8), lines.Select(l => JsonNode.Parse(l)?["n"]?.GetValue<int>() ?? 0));

        public void Dispose() => fixture.Dispose();
    }

    // AC7 — run.json records the run
    public sealed class ScenarioRunJsonRecordsTheRun : IDisposable
    {
        // Given the same run
        // When  run.json is read

        private readonly OneCardRun fixture = new();

        [Fact]
        public void ItCarriesTheScriptHash() =>
            Assert.Equal(
                GenWave.VoiceOff.Text.ScriptFile.Load(Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "tools", "VoiceOff", "script", "script.json")).Hash,
                ReadRunJson(fixture.Run)["scriptHash"]?.GetValue<string>());

        [Fact]
        public void ItListsEachCardWithItsHost() => Assert.Equal("thor-cpu", CardEntry(fixture.Run, CardId)["host"]?.GetValue<string>());

        [Fact]
        public void ItMarksTheCardRendered() => Assert.True(CardEntry(fixture.Run, CardId)["rendered"]?.GetValue<bool>());

        public void Dispose() => fixture.Dispose();
    }

    // AC8 — one card re-renders in place
    public sealed class ScenarioOneCardReRendersInPlace : IDisposable
    {
        // Given a finished run and render --card for one card
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly RenderRun first;
        private readonly RenderRun second;
        private readonly byte[] otherBefore;
        private readonly string[] otherLinesBefore;
        private readonly string cleared;

        public ScenarioOneCardReRendersInPlace()
        {
            EngineStubServer stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddCard("card-one", "alpha");
            scenario.AddCard("card-two", "alpha");
            first = scenario.Run("first");
            otherBefore = File.ReadAllBytes(first.PathIn(Path.Combine("cards", "card-two.wav")));
            otherLinesBefore = OwnLines(first, "card-two");
            cleared = "cleared";
            File.WriteAllText(first.PathIn(Path.Combine("cards", "card-one.wav")), cleared);
            second = scenario.Run("first", "--card", "card-one");
        }

        [Fact]
        public void ThatCardSClipIsRewritten() =>
            Assert.True(DurationMs(second.PathIn(Path.Combine("cards", "card-one.wav"))) > 0);

        [Fact]
        public void EveryOtherCardSClipIsByteIdenticalToBefore() =>
            Assert.Equal(otherBefore, File.ReadAllBytes(second.PathIn(Path.Combine("cards", "card-two.wav"))));

        [Fact]
        public void TheOtherCardKeepsItsRendersLinesAsWritten() => Assert.Equal(otherLinesBefore, OwnLines(second, "card-two"));

        [Fact]
        public void TheRewrittenCardHasEightLines() => Assert.Equal(8, OwnLines(second, "card-one").Length);

        private static string[] OwnLines(RenderRun run, string card) =>
            File.ReadAllLines(run.PathIn("renders.jsonl")).Where(l => JsonNode.Parse(l)?["card"]?.GetValue<string>() == card).ToArray();

        [Fact]
        public void BothCardsAreStillInRunJson() => Assert.Equal(["card-one", "card-two"], ReadRunJson(second)["cards"]?.AsObject().Select(p => p.Key));

        [Fact]
        public void TheRerunExitsZero() => Assert.Equal(0, second.Exit);

        public void Dispose() => scenario.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC9 — a failed container gate is a card
    public sealed class ScenarioAFailedContainerGateIsACard : IDisposable
    {
        // Given a card whose container is not running
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;
        private readonly EngineStubServer stub;

        public ScenarioAFailedContainerGateIsACard()
        {
            stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddCard(CardId, "alpha");
            run = scenario.RunWith(new FakeDockerInspector(new GenWave.VoiceOff.Speech.ContainerState(true, false, "10.0.0.2")), CancellationToken.None, "first");
        }

        [Fact]
        public void TheCardIsRecordedRenderedFalse() => Assert.False(CardEntry(run, CardId)["rendered"]?.GetValue<bool>());

        [Fact]
        public void TheReasonSaysContainer() =>
            Assert.StartsWith("container: ", CardEntry(run, CardId)["reason"]?.GetValue<string>(), StringComparison.Ordinal);

        [Fact]
        public void TheRunExits0() => Assert.Equal(0, run.Exit);

        [Fact]
        public void NoRequestReachedTheEngine() => Assert.Equal(0, stub.Requests.TotalRequests);

        public void Dispose() => scenario.Dispose();
    }

    // AC10 — a failed paragraph is a card
    public sealed class ScenarioAFailedParagraphIsACard : IDisposable
    {
        // Given a stub returning 500 on paragraph 5
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioAFailedParagraphIsACard()
        {
            EngineStubServer stub = scenario.StartStubAsync(failSpeechRequest: 5).GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddCard("card-one", "alpha");
            scenario.AddCard("card-two", "alpha");
            run = scenario.Run("first");
        }

        [Fact]
        public void TheCardIsRecordedRenderedFalse() => Assert.False(CardEntry(run, "card-one")["rendered"]?.GetValue<bool>());

        [Fact]
        public void TheReasonNamesParagraph5() =>
            Assert.Contains("paragraph 5 failed", CardEntry(run, "card-one")["reason"]?.GetValue<string>(), StringComparison.Ordinal);

        [Fact]
        public void TheNextCardStillRenders() => Assert.True(CardEntry(run, "card-two")["rendered"]?.GetValue<bool>());

        [Fact]
        public void TheFailedParagraphIsARecordedFailure() =>
            Assert.Contains(File.ReadAllLines(run.PathIn("renders.jsonl")), l => l.Contains("\"n\":5", StringComparison.Ordinal) && l.Contains("\"ok\":false", StringComparison.Ordinal) && l.Contains("\"error\"", StringComparison.Ordinal));

        [Fact]
        public void TheFailedCardLeavesNoClip() => Assert.False(File.Exists(run.PathIn(Path.Combine("cards", "card-one.wav"))));

        [Fact]
        public void TheRunExits0() => Assert.Equal(0, run.Exit);

        public void Dispose() => scenario.Dispose();
    }

    // AC11 — a words-law failure sends nothing
    public sealed class ScenarioAWordsLawFailureSendsNothing : IDisposable
    {
        // Given a card whose override changes a word
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;
        private readonly EngineStubServer stub;

        public ScenarioAWordsLawFailureSendsNothing()
        {
            stub = scenario.StartStubAsync().GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri);
            scenario.AddCard("card-one", "alpha");
            scenario.AddCard("card-two", "alpha", n =>
            {
                n["voice"] = "voice-bad";
                n["overrides"] = new JsonObject { ["3"] = "Other words entirely." };
            });
            run = scenario.Run("first");
        }

        [Fact]
        public void TheCardIsRecordedRenderedFalse() => Assert.False(CardEntry(run, "card-two")["rendered"]?.GetValue<bool>());

        [Fact]
        public void TheReasonNamesTheParagraph() =>
            Assert.Contains("Paragraph 3", CardEntry(run, "card-two")["reason"]?.GetValue<string>(), StringComparison.Ordinal);

        [Fact]
        public void TheStubReceivedZeroRequestsForThatCard() => Assert.DoesNotContain("voice-bad", stub.Requests.SpeechVoices);

        [Fact]
        public void TheOtherCardStillRendered() => Assert.Equal(8, stub.Requests.SpeechRequests);

        public void Dispose() => scenario.Dispose();
    }

    // AC12 — a timeout is a card
    public sealed class ScenarioATimeoutIsACard : IDisposable
    {
        // Given a stub that never answers and a 1 s timeout
        // When  render runs

        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ScenarioATimeoutIsACard()
        {
            EngineStubServer stub = scenario.StartStubAsync(speechDelay: TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri, timeoutSeconds: 1);
            scenario.AddCard(CardId, "alpha");
            run = scenario.Run("first");
        }

        [Fact]
        public void TheCardIsRecordedRenderedFalse() => Assert.False(CardEntry(run, CardId)["rendered"]?.GetValue<bool>());

        [Fact]
        public void TheReasonSaysTimeout() =>
            Assert.Contains("timeout", CardEntry(run, CardId)["reason"]?.GetValue<string>(), StringComparison.Ordinal);

        [Fact]
        public void TheRunExits0() => Assert.Equal(0, run.Exit);

        public void Dispose() => scenario.Dispose();
    }
}
