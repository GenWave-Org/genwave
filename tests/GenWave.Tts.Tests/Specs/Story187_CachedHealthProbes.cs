// STORY-187 — Cached dependency health probes
//
// BDD specification — xUnit (SPEC F70.2). Implemented PLAN T31 (/build-loop).

using System.Net;
using GenWave.Tts.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureCachedDependencyHealthProbes
{
    public static class ScenarioBackgroundCadence
    {
        [Fact]
        public static async Task Reads_return_cached_snapshots_between_probe_intervals()
        {
            // Given the probe service running against a healthy dependency, probing on a short
            // cadence (so the test finishes fast without faking time)
            var probe = new FakeDependencyProbe("healthy-dep", healthy: true);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);

            using var cts = new CancellationTokenSource();
            var runTask = prober.RunAsync(TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(5), cts.Token);

            // When verdicts are read repeatedly while the background loop keeps ticking on its
            // own cadence
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            var beforeReads = probe.CallCount;
            for (var i = 0; i < 50; i++)
            {
                _ = store.GetVerdict(probe.DependencyName);
            }
            var afterReads = probe.CallCount;

            await cts.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => runTask);

            // Then reads return the cached snapshot, and probe calls happened only on the
            // configured interval — nowhere near once per read (50 reads, far fewer probe calls)
            Assert.NotNull(store.GetVerdict(probe.DependencyName));
            Assert.True(beforeReads > 0, "expected at least one probe cycle to have run in 150ms at a 30ms interval");
            Assert.True(beforeReads < 50, $"expected cadence-gated probing, not once per read; got {beforeReads} calls");
            Assert.Equal(beforeReads, afterReads);   // the 50 reads themselves added zero further probe calls
        }
    }

    public static class ScenarioSynchronousDecision
    {
        [Fact]
        public static async Task Unhealthy_primary_verdict_selects_fallback_without_network_call()
        {
            // Given a cached unhealthy verdict for the primary TTS engine, produced by exactly
            // one probe cycle
            var probe = new FakeDependencyProbe(DependencyNames.Kokoro, healthy: false);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);
            await prober.RunCycleAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

            IDependencyHealth reader = store;

            // When the render path reads the cached verdict — repeatedly, as a render decision
            // (T34) would on every render, never just once
            DependencyHealthVerdict? verdict = null;
            for (var i = 0; i < 25; i++)
            {
                verdict = reader.GetVerdict(DependencyNames.Kokoro);
            }

            // Then the fallback-worthy unhealthy verdict comes back, with zero further probe
            // (i.e. network) calls beyond the one cycle that produced it
            Assert.NotNull(verdict);
            Assert.False(verdict.Healthy);
            Assert.Equal(1, probe.CallCount);
        }
    }

    public static class SadPathProbeFailure
    {
        [Fact]
        public static async Task Probe_timeout_becomes_an_unhealthy_verdict_and_service_survives()
        {
            // Given a dependency that hangs past its probe timeout
            var probe = new FakeDependencyProbe("slow-dep", healthy: true, hang: true);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);

            // When the next verdict is produced (one cycle, a short timeout)
            await prober.RunCycleAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

            // Then it reports unhealthy with the failure reason...
            var verdict = store.GetVerdict("slow-dep");
            Assert.NotNull(verdict);
            Assert.False(verdict.Healthy);
            Assert.Contains("timed out", verdict.Reason, StringComparison.OrdinalIgnoreCase);

            // ...and the probe service keeps running: a second cycle still completes cleanly,
            // never throwing out of RunCycleAsync
            await prober.RunCycleAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            var secondVerdict = store.GetVerdict("slow-dep");
            Assert.NotNull(secondVerdict);
            Assert.False(secondVerdict.Healthy);
            Assert.Equal(2, secondVerdict.ConsecutiveFailureCount);
        }
    }

    // ---------------------------------------------------------------------
    // The concrete Ollama/Kokoro probes T31 ships behind IDependencyProbe (SPEC F70.2) — every
    // fact here runs against a fake HttpMessageHandler; no test reaches the network.
    // ---------------------------------------------------------------------

    public static class ScenarioConcreteProbes
    {
        [Fact]
        public static async Task Ollama_probe_reports_not_configured_without_any_http_call_when_endpoint_is_empty()
        {
            // Given Llm:Endpoint is empty — LLM disabled by design (F34.2)
            var handler = new FakeHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("must not call out when unconfigured"));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "" });
            var probe = new OllamaHealthProbe(http, optionsMonitor);

            // When it is probed
            var healthy = await probe.ProbeAsync(CancellationToken.None);

            // Then it reports false (not-configured) and never calls out
            Assert.False(healthy);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public static async Task Ollama_probe_gets_the_lightest_documented_endpoint()
        {
            // Given a configured Ollama endpoint that answers 200
            var handler = new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "http://ollama:11434" });
            var probe = new OllamaHealthProbe(http, optionsMonitor);

            // When it is probed
            var healthy = await probe.ProbeAsync(CancellationToken.None);

            // Then it reports healthy and hit /api/version — not /api/tags (no model listing)
            Assert.True(healthy);
            var request = Assert.Single(handler.Requests);
            Assert.NotNull(request.RequestUri);
            Assert.Equal("/api/version", request.RequestUri.AbsolutePath);
        }

        [Fact]
        public static async Task Kokoro_probe_gets_the_health_endpoint()
        {
            // Given a configured Kokoro endpoint that answers 200
            var handler = new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<TtsOptions>(new TtsOptions { Endpoint = "http://kokoro:8880" });
            var probe = new KokoroHealthProbe(http, optionsMonitor);

            // When it is probed
            var healthy = await probe.ProbeAsync(CancellationToken.None);

            // Then it reports healthy and hit kokoro-fastapi's dedicated /health route
            Assert.True(healthy);
            var request = Assert.Single(handler.Requests);
            Assert.NotNull(request.RequestUri);
            Assert.Equal("/health", request.RequestUri.AbsolutePath);
        }

        [Fact]
        public static async Task Probe_throws_on_non_success_status_so_the_prober_can_record_a_reason()
        {
            // Given Kokoro answers unhealthy
            var handler = new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<TtsOptions>(new TtsOptions { Endpoint = "http://kokoro:8880" });
            var probe = new KokoroHealthProbe(http, optionsMonitor);

            // When/Then it throws — the driver, not the probe, decides the verdict's reason text
            await Assert.ThrowsAsync<HttpRequestException>(() => probe.ProbeAsync(CancellationToken.None));
        }
    }
}
