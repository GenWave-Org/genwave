// STORY-539 — The 8k floor and what the DJ remembers on a break (gh-#293 gh-#920 · SPEC F218 · PLAN T699 T701)
//
// BDD specification — xUnit. AC1–AC3, AC6, AC7 here; AC4/AC5 (recall) in tests/GenWave.Memory.Tests/Specs/Story539_TheRecallBudget.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

public static class FeatureTheEightKFloor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — Under 8k is red and memory is off
    public sealed class ScenarioUnder8kIsRedAndMemoryIsOff : IAsyncLifetime
    {
        // entry point: ILlmContextWindow probe against a fake /api/ps, then LlmCopyWriter.WriteAsync
        // Given a fake /api/ps reporting context_length 4096
        // When  the probe runs
        LlmContextWindow window = Arrange.Unrefreshed();

        public async Task InitializeAsync() => window = await Arrange.Probed(Arrange.ResidentWindow(4096));

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLLMTileIsRedWithContextWindow40968192MemoryOff() =>
            Assert.Equal("context window 4096 < 8192, memory off", window.MemoryOffReason);

        // The recent-lines block stays on (F218.37). Passes trivially until T701 adds memory blocks;
        // T701 must rewire this fact to build the prompt under MemoryBudget.Off.
        [Fact]
        public void TheNextPromptHasNoWhatYouRememberBlock() =>
            Assert.DoesNotContain("What you remember", Arrange.Prompt());
    }

    // AC2 — 8k is green and memory is on
    public sealed class Scenario8kIsGreenAndMemoryIsOn
    {
        // Given a fake /api/ps reporting 8192
        // When  the probe runs

        [Fact(Skip = "pending: T701 — see docs/PLAN.md")]
        public void TheLLMTileIsGreen() => Assert.Fail("pending: T701 — the LLM tile is green");

        [Fact(Skip = "pending: T701 — see docs/PLAN.md")]
        public void TheNextPromptHasAWhatYouRememberBlock() => Assert.Fail("pending: T701 — the next prompt has a \"What you remember\" block");
    }

    // AC3 — A declared window stands in for non-Ollama endpoints
    public sealed class ScenarioADeclaredWindowStandsInForNonOllamaEndpoints : IAsyncLifetime
    {
        // Given an endpoint with no /api/ps and Llm:ContextTokens = 16384
        // When  the probe runs
        LlmContextWindow window = Arrange.Unrefreshed();

        public async Task InitializeAsync() => window = await Arrange.Probed(psBody: null, contextTokens: 16384);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheWindowIs16384() => Assert.Equal(16384, window.Tokens);

        [Fact]
        public void MemoryIsOn() => Assert.True(window.Budget.MemoryOn);
    }

    // AC6 — Memories are colour, and the gate knows them
    public sealed class ScenarioMemoriesAreColourAndTheGateKnowsThem
    {
        // Given a recalled episode "read the weather: high of 4 °C with sleet, Tuesday"
        // When  the copy says "like Tuesday's sleet"

        [Fact(Skip = "pending: T701 — see docs/PLAN.md")]
        public void TheTruthGatePasses() => Assert.Fail("pending: T701 — the truth gate passes");

        [Fact(Skip = "pending: T701 — see docs/PLAN.md")]
        public void ThePromptSaysTheDJMayReferToMemoriesNeverMust() => Assert.Fail("pending: T701 — the prompt says the DJ may refer to memories, never must");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC7 — Unknown and undeclared is off
    public sealed class ScenarioUnknownAndUndeclaredIsOff : IAsyncLifetime
    {
        // Given no /api/ps and Llm:ContextTokens unset
        // When  the probe runs
        LlmContextWindow window = Arrange.Unrefreshed();

        public async Task InitializeAsync() => window = await Arrange.Probed(psBody: null);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheTileIsRedWithContextWindowUnknownMemoryOff() =>
            Assert.Equal("context window unknown, memory off", window.MemoryOffReason);

        [Fact]
        public async Task PatterStillGenerates()
        {
            var copy = await QuirkSpecHarness.WriterFor(QuirkSpecHarness.Card([], null), new NoQuirks())
                .WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);

            Assert.Equal("Coming up next a classic from the vault.", copy.Text);
        }
    }

    // AC7b — The model is loaded at most once per endpoint and model (keep_alive must not be reset every cycle)
    public sealed class ScenarioTheModelIsLoadedAtMostOncePerTarget : IAsyncLifetime
    {
        readonly FakeOllama notResident = new();
        readonly FakeOllama evicted = new();
        readonly FakeOllama neverListed = new();
        readonly FakeOllama retargeted = new();
        readonly FakeOllama untagged = new();
        LlmContextWindow loadedWindow = Arrange.Unrefreshed();
        LlmContextWindow evictedWindow = Arrange.Unrefreshed();
        LlmContextWindow neverListedWindow = Arrange.Unrefreshed();
        LlmContextWindow retargetedWindow = Arrange.Unrefreshed();
        LlmContextWindow untaggedWindow = Arrange.Unrefreshed();

        public async Task InitializeAsync()
        {
            // Given a model that is not resident: the load makes it appear in /api/ps
            notResident.LoadMakesResident(4096);
            loadedWindow = Arrange.Window(notResident);
            await loadedWindow.RefreshAsync(CancellationToken.None);

            // Given a model that reported 8192 and was then evicted
            evicted.Resident(8192);
            evictedWindow = Arrange.Window(evicted);
            await evictedWindow.RefreshAsync(CancellationToken.None);
            evicted.Evict();
            await evictedWindow.RefreshAsync(CancellationToken.None);
            await evictedWindow.RefreshAsync(CancellationToken.None);

            // Given /api/ps never lists the model
            neverListedWindow = Arrange.Window(neverListed);
            for (var i = 0; i < 4; i++)
                await neverListedWindow.RefreshAsync(CancellationToken.None);

            // Given a window that learned 8192, then the model changes and is not resident
            retargeted.Resident(8192);
            retargetedWindow = Arrange.Window(retargeted);
            await retargetedWindow.RefreshAsync(CancellationToken.None);
            retargeted.Evict();
            retargeted.Options.CurrentValue = new LlmOptions { Endpoint = Arrange.Endpoint, Model = "other" };
            await retargetedWindow.RefreshAsync(CancellationToken.None);

            // Given Llm:Model has no tag and Ollama lists it as :latest
            untagged.Resident(8192, name: "m:latest");
            untaggedWindow = Arrange.Window(untagged);
            await untaggedWindow.RefreshAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ANonResidentModelIsLoadedOnce() => Assert.Equal(1, notResident.GeneratePosts);

        [Fact]
        public void ThenItsWindowIsRead() => Assert.Equal(4096, loadedWindow.Tokens);

        [Fact]
        public void AnEvictedModelIsNotReloaded() => Assert.Equal(0, evicted.GeneratePosts);

        [Fact]
        public void AnEvictedModelKeepsItsLastWindow() => Assert.Equal(8192, evictedWindow.Tokens);

        [Fact]
        public void AModelNeverListedIsLoadedExactlyOnceAcrossRefreshes() => Assert.Equal(1, neverListed.GeneratePosts);

        [Fact]
        public void ANewModelDropsTheCachedWindow() => Assert.Null(retargetedWindow.Tokens);

        [Fact]
        public void ANewModelIsLoadedAfresh() => Assert.Equal(1, retargeted.GeneratePosts);

        [Fact]
        public void ATaglessModelMatchesItsLatestTag() => Assert.Equal(8192, untaggedWindow.Tokens);

        [Fact]
        public void ATaglessModelNeedsNoLoad() => Assert.Equal(0, untagged.GeneratePosts);
    }

    sealed class FakeOllama : HttpMessageHandler
    {
        string? psBody;
        string? bodyAfterLoad;
        public int GeneratePosts { get; private set; }
        public TestOptionsMonitor<LlmOptions> Options { get; } =
            new(new LlmOptions { Endpoint = Arrange.Endpoint, Model = "m" });

        public void Resident(int contextLength, string name = "m") => psBody = Arrange.ResidentWindow(contextLength, name);

        public void LoadMakesResident(int contextLength) => bodyAfterLoad = Arrange.ResidentWindow(contextLength);

        public void Evict() => psBody = null;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/generate")
            {
                GeneratePosts++;
                psBody = bodyAfterLoad ?? psBody;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/ps")
            {
                return Task.FromResult(psBody is null
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"models":[]}""") }
                    : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(psBody) });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    sealed class NoQuirks : IQuirkSelector
    {
        public Task<QuirkSelection> SelectAsync(
            long? personaId, PersonaCard? card, ShiftKey? shiftKey, bool spend, CancellationToken ct) =>
            Task.FromResult(QuirkSelection.None);
    }

    static class Arrange
    {
        public const string Endpoint = "http://fake.test:11434";

        public static string ResidentWindow(int contextLength, string name = "m") =>
            $$"""{"models":[{"name":"{{name}}","model":"{{name}}","size_vram":1,"context_length":{{contextLength}}}]}""";

        public static LlmContextWindow Window(FakeOllama ollama) =>
            new(new OllamaContextClient(new SingleHandlerHttpClientFactory(ollama)), ollama.Options, NullLogger<LlmContextWindow>.Instance);

        public static LlmContextWindow Unrefreshed() => Window(psBody: null, contextTokens: null);

        public static async Task<LlmContextWindow> Probed(string? psBody, int? contextTokens = null)
        {
            var window = Window(psBody, contextTokens);
            await window.RefreshAsync(CancellationToken.None);
            return window;
        }

        static LlmContextWindow Window(string? psBody, int? contextTokens) =>
            new(new OllamaContextClient(new SingleHandlerHttpClientFactory(new CannedChatHandler(_ => "", psBody))),
                new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = Endpoint, Model = "m", ContextTokens = contextTokens }),
                NullLogger<LlmContextWindow>.Instance);

        /// <summary>The on-air system prompt for one break.</summary>
        public static string Prompt()
        {
            string? prompt = null;
            QuirkSpecHarness.WriterFor(
                    QuirkSpecHarness.Card([], null), new NoQuirks(),
                    onSystemPrompt: p => { prompt = p; return Task.CompletedTask; })
                .WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None).GetAwaiter().GetResult();
            return prompt ?? throw new InvalidOperationException("No completion request was captured");
        }
    }
}
