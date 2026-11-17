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

        string prompt = "";

        public async Task InitializeAsync()
        {
            window = await Arrange.Probed(Arrange.ResidentWindow(4096));
            prompt = await Arrange.PromptAsync(window.Budget);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLLMTileIsRedWithContextWindow40968192MemoryOff() =>
            Assert.Equal("context window 4096 < 8192, memory off", window.MemoryOffReason);

        // The recent-lines block stays on (F218.37). The recall source WOULD return rows: only the 4096
        // window keeps them out (memory-on evidence is Scenario8kIsGreenAndMemoryIsOn: the tile reason is null,
        // so recall is taken at 8192; prompt rows are no longer the signal, F218.40-F218.42).
        [Fact]
        public void TheNextPromptHasNoWhatYouRememberBlock() =>
            Assert.DoesNotContain("What you remember", prompt);
    }

    // AC2 — 8k is green and memory is on
    public sealed class Scenario8kIsGreenAndMemoryIsOn : IAsyncLifetime
    {
        // Given a fake /api/ps reporting 8192
        // When  the probe runs
        LlmContextWindow window = Arrange.Unrefreshed();

        string prompt = "";

        public async Task InitializeAsync()
        {
            window = await Arrange.Probed(Arrange.ResidentWindow(8192));
            prompt = await Arrange.PromptAsync(window.Budget);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheLLMTileIsGreen() => Assert.Null(window.MemoryOffReason);

        [Fact]
        public void TheNextPromptHasNoWhatYouRememberBlock() =>
            // Retired by F218.40-F218.42: only a callback ask carries a memory now.
            Assert.DoesNotContain("What you remember", prompt);
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
    public sealed class ScenarioAContextBreakCarriesNoMemory : IAsyncLifetime
    {
        // Given a context (weather) break - the gate only checks a fact block there, so the fact is not vacuous
        // (on a LeadIn there is no fact block and it would pass anyway) - and a recall source holding the episode "read the weather: high of 4 °C with sleet, Tuesday"
        // When  the copy says only what the context facts say
        //
        // T736 amendment (F218.40/F218.42): this fact used to pass because the recalled episode joined the gate's facts,
        // so "like Tuesday's sleet at 4 degrees" cleared the number check. A context segment never carries a memory any
        // more (only a LeadIn/BackAnnounce callback ask does, and its text joins the facts only there), so the copy now
        // passes on the context facts alone, and the episode is in neither the prompt nor the facts.
        const string Copy = "Cloudy at 12 out there.";
        const string Episode = "read the weather: high of 4 °C with sleet, Tuesday";
        SegmentCopy aired = new("", false);
        string prompt = "";

        public async Task InitializeAsync()
        {
            var prompts = new List<string>();
            var writer = QuirkSpecHarness.WriterFor(
                QuirkSpecHarness.Card([], null), new NoQuirks(), httpOverride: QuirkSpecHarness.CapturingHttp(prompts, Copy),
                contextWindow: new FixedWindow(MemoryBudget.Standard), memoryRecall: new FixedRecall(Episode));
            aired = await writer.WriteAsync(Arrange.ContextRequest("Weather: 12 degrees and cloudy."), CancellationToken.None);
            prompt = prompts[0];
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheTruthGatePasses() => Assert.Equal(Copy, aired.Text);

        [Fact]
        public void ThePromptNoLongerCarriesTheMayReferLine() =>
            // Retired by F218.42.
            Assert.DoesNotContain("You may refer to these if it fits", prompt);

        [Fact]
        public void TheContextPromptCarriesNoMemory() => Assert.DoesNotContain("sleet", prompt);
    }

    // F218.22 - rows are only spent when the prompt will carry them
    // Given memory on (8192) and a counting recall source, each scenario starts from one condition.
    static (LlmCopyWriter Writer, CountingRecall Recall) Arrange8k(PersonaCard card, bool throwing = false)
    {
        var recall = new CountingRecall(throwing);
        var writer = QuirkSpecHarness.WriterFor(
            card, new NoQuirks(),
            contextWindow: new FixedWindow(MemoryBudget.Standard), memoryRecall: recall);
        return (writer, recall);
    }

    public sealed class ScenarioASoullessQuirklessPersonaSpendsNothing : IAsyncLifetime
    {
        // Given a persona with no soul and no quirks
        // When  a break is written
        CountingRecall recall = new(false);

        public async Task InitializeAsync()
        {
            PersonaCard card = QuirkSpecHarness.Card([], null) with { Soul = "" };
            var (writer, taken) = Arrange8k(card);
            recall = taken;
            await writer.WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void RecallIsNeverTaken() => Assert.Equal(0, recall.Takes);
    }

    public sealed class ScenarioAPersonaWithASoulDoesSpend : IAsyncLifetime
    {
        // Given a persona with a soul
        // When  a break is written
        CountingRecall recall = new(false);

        public async Task InitializeAsync()
        {
            var (writer, taken) = Arrange8k(QuirkSpecHarness.Card([], null));
            recall = taken;
            await writer.WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void RecallIsTakenOnce() => Assert.Equal(1, recall.Takes);
    }

    public sealed class ScenarioAPreviewNeverSpends : IAsyncLifetime
    {
        // Given a persona with a soul
        // When  a preview is written
        CountingRecall recall = new(false);

        public async Task InitializeAsync()
        {
            var (writer, taken) = Arrange8k(QuirkSpecHarness.Card([], null));
            recall = taken;
            await writer.WritePreviewAsync(QuirkSpecHarness.Request(null), personaOverride: null, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void RecallIsNeverTaken() => Assert.Equal(0, recall.Takes);
    }

    public sealed class ScenarioAnAnnouncementNeverSpends : IAsyncLifetime
    {
        // Given a persona with a soul
        // When  an announcement is written
        CountingRecall recall = new(false);

        public async Task InitializeAsync()
        {
            var (writer, taken) = Arrange8k(QuirkSpecHarness.Card([], null));
            recall = taken;
            await writer.WriteAnnouncementAsync(QuirkSpecHarness.Request(null), "The station is moving.", CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void RecallIsNeverTaken() => Assert.Equal(0, recall.Takes);
    }

    public sealed class ScenarioARecallFaultStillAirsLlmCopy : IAsyncLifetime
    {
        // Given a recall source that throws
        // When  a break is written
        CountingRecall recall = new(true);
        SegmentCopy copy = new("", false);

        public async Task InitializeAsync()
        {
            var (writer, taken) = Arrange8k(QuirkSpecHarness.Card([], null), throwing: true);
            recall = taken;
            copy = await writer.WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void RecallWasAttemptedOnce() => Assert.Equal(1, recall.Takes);

        [Fact]
        public void TheLlmCopyAired() => Assert.Equal("Coming up next a classic from the vault.", copy.Text);
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

        SegmentCopy copy = new("", false);

        public async Task InitializeAsync()
        {
            window = await Arrange.Probed(psBody: null);
            copy = await QuirkSpecHarness.WriterFor(QuirkSpecHarness.Card([], null), new NoQuirks())
                .WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheTileIsRedWithContextWindowUnknownMemoryOff() =>
            Assert.Equal("context window unknown, memory off", window.MemoryOffReason);

        [Fact]
        public void PatterStillGenerates() => Assert.Equal("Coming up next a classic from the vault.", copy.Text);
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

    sealed class FixedWindow(MemoryBudget budget) : ILlmContextWindow
    {
        public int? Tokens => budget.MemoryOn ? MemoryBudget.MinContextTokens : null;
        public MemoryBudget Budget => budget;
        public string? MemoryOffReason => null;
    }

    sealed class FixedRecall(string episode) : IMemoryRecallSource
    {
        public Task<MemoryPacket> TakeAsync(long personaId, MemoryBudget budget, CancellationToken ct) =>
            Task.FromResult(new MemoryPacket([], [new RecalledMemory(1, "episode", episode, null)], [], []));

        public Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct) =>
            TakeAsync(personaId, budget, ct);
    }

    sealed class CountingRecall(bool throws) : IMemoryRecallSource
    {
        public int Takes { get; private set; }

        public Task<MemoryPacket> TakeAsync(long personaId, MemoryBudget budget, CancellationToken ct) =>
            throw new InvalidOperationException("the writer no longer takes rows (T736)");

        public Task<MemoryPacket> NeverMentionAsync(long personaId, MemoryBudget budget, CancellationToken ct)
        {
            Takes++;
            return throws
                ? throw new InvalidOperationException("recall store down")
                : Task.FromResult(new MemoryPacket([], [new RecalledMemory(1, "episode", "won the pub quiz", null)], [], []));
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

        public static SegmentRequest ContextRequest(string facts) =>
            new(SegmentKind.ContextSegment, "af_heart", "GenWave", Track: null, DateTimeOffset.UtcNow, "test-station",
                PersonaName: null, CounterpartName: null, ContextFacts: facts)
            {
                PersonaId = QuirkSpecHarness.PersonaId,
            };

        /// <summary>The on-air system prompt for one break; the recall source would return an episode whenever memory is on.</summary>
        public static async Task<string> PromptAsync(MemoryBudget budget)
        {
            string? prompt = null;
            await QuirkSpecHarness.WriterFor(
                    QuirkSpecHarness.Card([], null), new NoQuirks(),
                    onSystemPrompt: p => { prompt = p; return Task.CompletedTask; },
                    contextWindow: new FixedWindow(budget), memoryRecall: new FixedRecall("won the pub quiz on a Tuesday"))
                .WriteAsync(QuirkSpecHarness.Request(null), CancellationToken.None);
            return prompt ?? throw new InvalidOperationException("No completion request was captured");
        }
    }
}
