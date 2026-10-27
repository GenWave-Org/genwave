using System.Text.Json.Nodes;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Render;

/// <summary>
/// The rules of <c>voiceoff render</c> that no story pins, driven through the real CLI over stub engines. Each nested class is one
/// run and its facts (one assertion each); the run happens in the constructor.
/// </summary>
public static class RenderCommandTests
{
    private static JsonNode RunJson(RenderRun run) =>
        JsonNode.Parse(File.ReadAllText(run.PathIn("run.json"))) ?? throw new InvalidOperationException("run.json is empty");

    private static JsonNode CardEntry(RenderRun run, string id) =>
        RunJson(run)["cards"]?[id] ?? throw new InvalidOperationException($"run.json has no card {id}");

    private static FakeDockerInspector Running() => new(new ContainerState(true, true, "10.0.0.2"));

    private static EngineStubServer OneEngineWithCards(RenderScenario scenario, params string[] cardIds) =>
        OneEngineWithCards(scenario, scenario.StartStubAsync().GetAwaiter().GetResult(), cardIds);

    private static EngineStubServer OneEngineWithCards(RenderScenario scenario, EngineStubServer stub, string[] cardIds)
    {
        scenario.AddEngine("alpha", stub.BaseUri);
        foreach (string id in cardIds)
        {
            scenario.AddCard(id, "alpha");
        }

        return stub;
    }

    public sealed class ARenderedCardInRunJson : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly JsonObject entry;

        public ARenderedCardInRunJson()
        {
            OneEngineWithCards(scenario, "card-one");
            entry = CardEntry(scenario.Run("first"), "card-one").AsObject();
        }

        [Fact]
        public void HasNoReasonProperty() => Assert.False(entry.ContainsKey("reason"));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class AWholeRunOnAFolderThatHasRunJson : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly EngineStubServer stub;
        private readonly int requestsBefore;
        private readonly RenderRun second;

        public AWholeRunOnAFolderThatHasRunJson()
        {
            stub = OneEngineWithCards(scenario, "card-one");
            scenario.Run("first");
            requestsBefore = stub.Requests.TotalRequests;
            second = scenario.Run("first");
        }

        [Fact]
        public void IsRefusedWithExit1() => Assert.Equal(1, second.Exit);

        [Fact]
        public void SendsTheEngineNothing() => Assert.Equal(requestsBefore, stub.Requests.TotalRequests);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ASingleCardAfterTheScriptChanged : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly byte[] before;
        private readonly RenderRun second;

        public ASingleCardAfterTheScriptChanged()
        {
            OneEngineWithCards(scenario, "card-one");
            RenderRun first = scenario.Run("first");
            before = File.ReadAllBytes(first.PathIn("run.json"));
            scenario.WriteScript(text => text.Replace("Honestly?", "Truthfully?", StringComparison.Ordinal));
            second = scenario.Run("first", "--card", "card-one");
        }

        [Fact]
        public void IsRefusedWithExit1() => Assert.Equal(1, second.Exit);

        [Fact]
        public void LeavesRunJsonByteIdentical() => Assert.Equal(before, File.ReadAllBytes(second.PathIn("run.json")));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ASingleCardOnAnEmptyRoot : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ASingleCardOnAnEmptyRoot()
        {
            OneEngineWithCards(scenario, "card-one", "card-two");
            run = scenario.Run("first", "--card", "card-two");
        }

        [Fact]
        public void ListsOnlyThatCardInRunJson() => Assert.Equal(["card-two"], RunJson(run)["cards"]?.AsObject().Select(p => p.Key));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class AnEngineThatStopsAnswering : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public AnEngineThatStopsAnswering()
        {
            EngineStubServer stub = scenario.StartStubAsync(speechDelay: TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            scenario.AddEngine("alpha", stub.BaseUri, timeoutSeconds: 1);
            foreach (string id in new[] { "card-1", "card-2", "card-3", "card-4", "card-5" })
            {
                scenario.AddCard(id, "alpha");
            }

            run = scenario.Run("first");
        }

        [Fact]
        public void TheRunStillExits0() => Assert.Equal(0, run.Exit);

        [Theory]
        [InlineData("card-1")]
        [InlineData("card-2")]
        [InlineData("card-3")]
        public void TheFirstThreeCardsFailWithATimeout(string id) =>
            Assert.Contains("timeout", CardEntry(run, id)["reason"]?.GetValue<string>(), StringComparison.Ordinal);

        [Theory]
        [InlineData("card-4")]
        [InlineData("card-5")]
        public void TheRemainingCardsAreNotRendered(string id) => Assert.False(CardEntry(run, id)["rendered"]?.GetValue<bool>());

        [Theory]
        [InlineData("card-4")]
        [InlineData("card-5")]
        public void TheRemainingCardsAreRecordedStopped(string id) =>
            Assert.Equal(CardRunner.StoppedReason, CardEntry(run, id)["reason"]?.GetValue<string>());

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ACancelledRun : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ACancelledRun()
        {
            EngineStubServer stub = scenario.StartStubAsync(speechDelay: TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            OneEngineWithCards(scenario, stub, ["card-one"]);
            using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(1));
            run = scenario.RunWith(Running(), cancel.Token, "first");
        }

        [Fact]
        public void ExitsWith130() => Assert.Equal(130, run.Exit);

        [Fact]
        public void KeepsRunJson() => Assert.True(File.Exists(run.PathIn("run.json")));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ABrokenEngineProfile : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ABrokenEngineProfile()
        {
            scenario.AddEngineFile("alpha.json", EngineProfileJson.With(n => n.Remove("license")));
            scenario.AddCardFile("card-one.json", CardFixtureDirectory.CardJson("card-one", "alpha"));
            run = scenario.Run("first");
        }

        [Fact]
        public void ExitsWith1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void NamesTheFile() => Assert.Contains("alpha.json", run.Error, StringComparison.Ordinal);

        [Fact]
        public void WritesNoRunFolder() => Assert.False(Directory.Exists(scenario.Root));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ABrokenCard : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ABrokenCard()
        {
            OneEngineWithCards(scenario, "card-one");
            scenario.AddCardFile("broken-card.json", "{ this is not json");
            run = scenario.Run("first");
        }

        [Fact]
        public void ExitsWith1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void NamesTheFile() => Assert.Contains("broken-card", run.Error, StringComparison.Ordinal);

        [Fact]
        public void WritesNoRunFolder() => Assert.False(Directory.Exists(scenario.Root));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ABrokenScript : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun run;

        public ABrokenScript()
        {
            OneEngineWithCards(scenario, "card-one");
            scenario.WriteScript(_ => "{ this is not json");
            run = scenario.Run("first");
        }

        [Fact]
        public void ExitsWith1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void NamesTheFile() => Assert.Contains(Path.GetFileName(scenario.ScriptPath), run.Error, StringComparison.Ordinal);

        [Fact]
        public void WritesNoRunFolder() => Assert.False(Directory.Exists(scenario.Root));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ARootThatCannotBeCreated : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly string rootFile = Path.Combine(Path.GetTempPath(), "voiceoff-rootfile-" + Guid.NewGuid().ToString("N"));
        private readonly RenderRun run;

        public ARootThatCannotBeCreated()
        {
            OneEngineWithCards(scenario, "card-one");
            File.WriteAllText(rootFile, "not a folder");
            run = scenario.RunAt(rootFile, Running(), CancellationToken.None, "first");
        }

        [Fact]
        public void ExitsWith1() => Assert.Equal(1, run.Exit);

        [Fact]
        public void SaysSoInPlainWords() => Assert.StartsWith("Cannot create the run folder", run.Error, StringComparison.Ordinal);

        public void Dispose()
        {
            File.Delete(rootFile);
            scenario.Dispose();
        }
    }

    public sealed class ACorruptRendersLogOnASingleCardRun : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun second;
        private readonly byte[] clipBefore;
        private readonly byte[] runJsonBefore;

        public ACorruptRendersLogOnASingleCardRun()
        {
            OneEngineWithCards(scenario, "card-one", "card-two");
            RenderRun first = scenario.Run("first");
            File.WriteAllText(first.PathIn("renders.jsonl"), "not json\nnot json either\n");
            clipBefore = File.ReadAllBytes(first.PathIn(Path.Combine("cards", "card-one.wav")));
            runJsonBefore = File.ReadAllBytes(first.PathIn("run.json"));
            second = scenario.Run("first", "--card", "card-one");
        }

        [Fact]
        public void ExitsWith1() => Assert.Equal(1, second.Exit);

        [Fact]
        public void NamesTheFile() => Assert.Contains("renders.jsonl", second.Error, StringComparison.Ordinal);

        [Fact]
        public void LeavesTheCardSClipByteIdentical() =>
            Assert.Equal(clipBefore, File.ReadAllBytes(second.PathIn(Path.Combine("cards", "card-one.wav"))));

        [Fact]
        public void LeavesRunJsonByteIdentical() => Assert.Equal(runJsonBefore, File.ReadAllBytes(second.PathIn("run.json")));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class AReRenderCancelledBeforeItFinishes : IDisposable
    {
        private readonly RenderScenario scenario = new();
        private readonly RenderRun second;

        public AReRenderCancelledBeforeItFinishes()
        {
            OneEngineWithCards(scenario, "card-one", "card-two");
            scenario.Run("first");
            // Same folder, same root; the engine file now points at a stub that never answers.
            EngineStubServer silent = scenario.StartStubAsync(speechDelay: TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            string engineFile = Path.Combine(scenario.EnginesDirectory, "alpha.json");
            JsonNode engine = JsonNode.Parse(File.ReadAllText(engineFile)) ?? throw new InvalidOperationException("alpha.json is empty");
            engine["endpoint"] = silent.BaseUri.AbsoluteUri;
            File.WriteAllText(engineFile, engine.ToJsonString());
            using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(1));
            second = scenario.RunWith(Running(), cancel.Token, "first", "--card", "card-one");
        }

        [Fact]
        public void ExitsWith130() => Assert.Equal(130, second.Exit);

        [Fact]
        public void RecordsTheCardNotRendered() => Assert.False(CardEntry(second, "card-one")["rendered"]?.GetValue<bool>());

        [Fact]
        public void GivesTheDidNotFinishReason() =>
            Assert.Equal(CardRunner.UnfinishedReason, CardEntry(second, "card-one")["reason"]?.GetValue<string>());

        [Fact]
        public void LeavesNoClip() => Assert.False(File.Exists(second.PathIn(Path.Combine("cards", "card-one.wav"))));

        [Fact]
        public void LeavesTheOtherCardRendered() => Assert.True(CardEntry(second, "card-two")["rendered"]?.GetValue<bool>());

        public void Dispose() => scenario.Dispose();
    }
}
