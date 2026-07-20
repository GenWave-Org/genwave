// STORY-124 — TTS/LLM endpoints are live-editable, location-agnostic URLs (WIRE)
//
// BDD specification — xUnit. The live-repoint half of Story124 (the F36.1/F36.2/F36.4 contract
// itself): KokoroTtsSynthesizer, KokoroVoiceLister/CachedVoiceLister, and LlmCopyWriter all read
// their configured endpoint via IOptionsMonitor.CurrentValue per call — never a boot-frozen
// HttpClient.BaseAddress — so a live repoint mid-run reaches a second stub on the very next call,
// with no api restart. TestOptionsMonitor<T>'s mutable CurrentValue stands in for a real live PUT
// to Tts:Endpoint/Llm:Endpoint/Llm:Model (Program.cs wires the real IOptionsMonitor<T> that a
// station.settings write re-binds).
//
// This file owns the component-level proof (synthesizer/lister/writer repoint correctly); the
// allowlist surface + Llm:ApiKey secrecy contract live in GenWave.Host.Tests
// (Story124_EndpointLiveness.cs), and VoicesController's OWN HttpRequestException → 502
// ProblemDetails translation is already pinned by Story097 (Host.Tests) — the fact below only
// proves the lister surfaces that same exception type against a listing-less endpoint.
// See docs/PLAN.md Epic T.

namespace GenWave.Tts.Tests.Specs;

using System.Text.Json;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureEndpointLiveRepoint
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static string NewCacheRoot() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    // ---------------------------------------------------------------------
    // HAPPY PATH — a live repoint takes effect on the very next call
    // ---------------------------------------------------------------------

    public sealed class ScenarioTtsRepoint : IAsyncLifetime
    {
        KokoroStubServer stubA = null!;
        KokoroStubServer stubB = null!;

        public async Task InitializeAsync()
        {
            stubA = await KokoroStubServer.StartAsync();
            stubB = await KokoroStubServer.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await stubA.DisposeAsync();
            await stubB.DisposeAsync();
        }

        [Fact]
        public async Task TtsRepointRoutesTheNextSynthesisToTheNewEndpoint()
        {
            // PUT Tts:Endpoint → second stub receives the next render; no restart (F36.1–F36.2, AC2).
            var cacheRoot = NewCacheRoot();
            var monitor = new TestOptionsMonitor<TtsOptions>(
                new TtsOptions { Endpoint = stubA.BaseUri.ToString(), CacheRoot = cacheRoot, Format = "wav" });
            var synthesizer = new KokoroTtsSynthesizer(new HttpClient(), monitor);

            await synthesizer.SynthesizeAsync("hello there", "af_heart", CancellationToken.None);
            Assert.Equal(1, stubA.SpeechCallCount);

            // The live repoint — mirrors what a PUT /api/settings write to Tts:Endpoint does to the
            // real IOptionsMonitor<TtsOptions> via the station.settings overlay reload.
            monitor.CurrentValue = new TtsOptions { Endpoint = stubB.BaseUri.ToString(), CacheRoot = cacheRoot, Format = "wav" };

            // Different text than above so a cache hit on the (text,voice) hash can't mask a
            // same-endpoint call — the render must genuinely reach the network again.
            await synthesizer.SynthesizeAsync("a second render", "af_heart", CancellationToken.None);

            Assert.True(stubA.SpeechCallCount == 1 && stubB.SpeechCallCount == 1);
        }
    }

    public sealed class ScenarioLlmRepoint : IAsyncLifetime
    {
        MockCompletionsServer mockA = null!;
        MockCompletionsServer mockB = null!;

        public async Task InitializeAsync()
        {
            mockA = await MockCompletionsServer.StartAsync();
            mockB = await MockCompletionsServer.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await mockA.DisposeAsync();
            await mockB.DisposeAsync();
        }

        [Fact]
        public async Task LlmRepointRoutesTheNextBlurbToTheNewEndpointAndModel()
        {
            // (F36.2, AC3) — both Llm:Endpoint and Llm:Model repoint together, exactly like a
            // single PUT batch would.
            mockA.ReplyContent = "Copy from the old endpoint.";
            mockB.ReplyContent = "Copy from the new endpoint.";

            var monitor = new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = mockA.BaseUri.ToString(),
                Model = "model-a",
                TimeoutSeconds = 5,
                MaxCopyChars = 450,
            });
            var writer = new LlmCopyWriter(
                new TemplateCopyWriter(new PatterTemplateRenderer()),
                new FakeHttpClientFactory(),
                monitor,
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor(),
                new CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System);

            var before = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);
            Assert.Equal(mockA.ReplyContent, before.Text);

            // The live repoint.
            monitor.CurrentValue = new LlmOptions
            {
                Endpoint = mockB.BaseUri.ToString(),
                Model = "model-b",
                TimeoutSeconds = 5,
                MaxCopyChars = 450,
            };

            var after = await writer.WriteAsync(LeadInRequest(), CancellationToken.None);

            using var doc = JsonDocument.Parse(mockB.Requests[0].Body);
            var modelSentToB = doc.RootElement.GetProperty("model").GetString();

            Assert.True(
                after.Text == mockB.ReplyContent && mockA.RequestCount == 1 && modelSentToB == "model-b");
        }
    }

    public sealed class ScenarioVoicesRepoint : IAsyncLifetime
    {
        KokoroStubServer stubA = null!;
        KokoroStubServer stubB = null!;

        public async Task InitializeAsync()
        {
            stubA = await KokoroStubServer.StartAsync(["af_heart"]);
            stubB = await KokoroStubServer.StartAsync(["af_bella", "am_adam"]);
        }

        public async Task DisposeAsync()
        {
            await stubA.DisposeAsync();
            await stubB.DisposeAsync();
        }

        [Fact]
        public async Task VoicesProxyFollowsTheRepointedEndpoint()
        {
            // GET /api/voices queries the configured endpoint (F36.4, AC4). The short TTL cache
            // (F29.4) must NOT keep serving stub A's list once repointed — a fresh, well-within-TTL
            // repoint is a deliberate cache miss (CachedVoiceLister stamps the cached entry with the
            // endpoint it came from).
            var monitor = new TestOptionsMonitor<TtsOptions>(new TtsOptions { Endpoint = stubA.BaseUri.ToString() });
            var lister = new CachedVoiceLister(
                new KokoroVoiceLister(new HttpClient(), monitor), monitor, TimeSpan.FromMinutes(5));

            var before = await lister.ListVoicesAsync(CancellationToken.None);
            Assert.Equal(new[] { "af_heart" }, before);

            monitor.CurrentValue = new TtsOptions { Endpoint = stubB.BaseUri.ToString() };

            var after = await lister.ListVoicesAsync(CancellationToken.None);
            Assert.Equal(new[] { "af_bella", "am_adam" }, after);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — repointed to an endpoint that doesn't support voice listing
    // ---------------------------------------------------------------------

    public sealed class ScenarioVoicesAgainstAListingLessEndpoint : IAsyncLifetime
    {
        KokoroStubServer stubWithNoVoicesRoute = null!;

        // Constructing with no voiceIds argument never maps GET /v1/audio/voices at all — a GET
        // against it 404s, the same shape an older Kokoro build without voice-listing support would
        // produce (distinct from "endpoint unreachable", which Story097/Host.Tests already covers).
        public async Task InitializeAsync() => stubWithNoVoicesRoute = await KokoroStubServer.StartAsync();

        public async Task DisposeAsync() => await stubWithNoVoicesRoute.DisposeAsync();

        [Fact]
        public async Task VoicesAgainstAListingLessEndpointDegradesPerF295()
        {
            // 502 → the shipped free-text fallback story holds (F36.4, AC4). KokoroVoiceLister's
            // EnsureSuccessStatusCode surfaces the 404 as HttpRequestException — the exact type
            // VoicesController's catch clause translates to 502 ProblemDetails (pinned end-to-end,
            // controller included, by Story097's ScenarioKokoroUnreachable).
            var monitor = new TestOptionsMonitor<TtsOptions>(
                new TtsOptions { Endpoint = stubWithNoVoicesRoute.BaseUri.ToString() });
            var lister = new KokoroVoiceLister(new HttpClient(), monitor);

            var act = async () => await lister.ListVoicesAsync(CancellationToken.None);

            await Assert.ThrowsAsync<HttpRequestException>(act);
        }
    }
}
