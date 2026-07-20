// STORY-189 — LLM calls single-flight with detailed failure logs (gh-#36)
//
// BDD specification — xUnit (SPEC F69.6-F69.7).
//
// AC1 proves single-flight at the real boundary LlmCopyWriter calls through
// (IHttpClientFactory -> HttpClient -> HttpMessageHandler): a handler-level in-flight counter,
// not the house MockCompletionsServer, because what's under test here is LlmCopyWriter's OWN
// gate rather than anything a Kestrel stub does — a plain HttpMessageHandler double is the
// smallest thing that proves it.
//
// AC2 reuses the house MockCompletionsServer idiom (STORY-119) — the failure-detail warn needs
// a real non-2xx response to inspect, not a concurrency counter.

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Http;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureLlmSingleFlight
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest BackAnnounceRequest() =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static LlmOptions Options(string endpoint, int timeoutSeconds = 5) => new()
    {
        Endpoint = endpoint,
        Model = "test-model",
        TimeoutSeconds = timeoutSeconds,
        MaxCopyChars = 450,
    };

    public static class ScenarioSerializedGenerations
    {
        [Fact]
        public static async Task Concurrent_copy_renders_execute_sequentially()
        {
            // Given two concurrent copy render requests against the SAME LlmCopyWriter instance
            // (the gate is per-instance, and this writer is a DI singleton in production, so one
            // instance is exactly what both the on-air and preview seams actually share)
            var handler = new ConcurrencyTrackingHandler();
            var writer = new LlmCopyWriter(
                new TemplateCopyWriter(new PatterTemplateRenderer()),
                new SingleHandlerHttpClientFactory(handler),
                new TestOptionsMonitor<LlmOptions>(Options("http://mock-llm.invalid")),
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor(),
                new CapturingLogger<LlmCopyWriter>(),
                TimeProvider.System);

            // When their backend calls are traced (the handler counts overlapping SendAsync calls
            // — each call holds its slot for a short delay so an un-gated pair would overlap it)
            await Task.WhenAll(
                writer.WriteAsync(LeadInRequest(), CancellationToken.None),
                writer.WriteAsync(BackAnnounceRequest(), CancellationToken.None));

            // Then the generations execute sequentially, never concurrently (F69.6)
            Assert.Equal(1, handler.MaxObservedConcurrency);
        }
    }

    public sealed class SadPathFailureDetail : IAsyncLifetime
    {
        MockCompletionsServer mock = null!;

        public async Task InitializeAsync() => mock = await MockCompletionsServer.StartAsync(MockCompletionsMode.Fail);

        public async Task DisposeAsync() => await mock.DisposeAsync();

        [Fact]
        public async Task Failure_warning_includes_exception_status_and_context()
        {
            // Given an LLM call failing with a status code (500)
            mock.FailStatusCode = 500;
            var logger = new CapturingLogger<LlmCopyWriter>();
            var writer = new LlmCopyWriter(
                new TemplateCopyWriter(new PatterTemplateRenderer()),
                new FakeHttpClientFactory(),
                new TestOptionsMonitor<LlmOptions>(Options(mock.BaseUri.ToString())),
                new LlmCopyStatusHolder(),
                new FakeActivePersonaAccessor(),
                logger,
                TimeProvider.System);
            var request = LeadInRequest();

            // When the warning is logged
            await writer.WriteAsync(request, CancellationToken.None);

            // Then it includes the exception/status (500) and the call context — segment kind,
            // station identity, model, elapsed ms (F69.7)
            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("500", warning);
            Assert.Contains(request.Kind.ToString(), warning);
            Assert.Contains(request.StationId, warning);
            Assert.Contains("model", warning, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("elapsed", warning, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Counts overlapping <see cref="SendAsync"/> invocations rather than serving from a real
    /// server — the smallest double that proves <see cref="LlmCopyWriter"/>'s own single-flight
    /// gate, not anything a stub server's request pipeline happens to do. Each call holds its slot
    /// for a short delay specifically so a second, un-gated call would overlap it and be caught.
    /// </summary>
    sealed class ConcurrencyTrackingHandler : HttpMessageHandler
    {
        readonly object gate = new();
        int inFlight;

        public int MaxObservedConcurrency { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                inFlight++;
                if (inFlight > MaxObservedConcurrency)
                    MaxObservedConcurrency = inFlight;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        choices = new[] { new { message = new { content = "Great tune coming up, stay tuned." } } },
                    }),
                };
            }
            finally
            {
                lock (gate)
                {
                    inFlight--;
                }
            }
        }
    }

    /// <summary>Hands every client the SAME shared handler (never disposed by the client) so
    /// <see cref="ConcurrencyTrackingHandler"/>'s counters observe every call this writer makes.</summary>
    sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
