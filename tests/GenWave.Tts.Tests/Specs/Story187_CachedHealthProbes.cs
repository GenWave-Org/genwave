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
    /// <summary>
    /// A cadence with the debounce OFF (threshold 1 — flip on the first failure), which is the
    /// behavior every pre-gh-#125 fact in this file was written against. The AC5 debounce facts
    /// live in <see cref="ScenarioProbeFlapDebounce"/> and set their own threshold explicitly.
    /// </summary>
    static DependencyProbeCadence Undebounced(TimeSpan perProbeTimeout) =>
        new(TimeSpan.FromMilliseconds(30), perProbeTimeout, UnhealthyThreshold: 1);

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
            var runTask = prober.RunAsync(() => Undebounced(TimeSpan.FromSeconds(5)), cts.Token);

            // When the loop has ticked on its own cadence for a while, is then stopped, and
            // verdicts are read repeatedly. The stop comes BEFORE the read burst: with the loop
            // still running, a 30ms tick landing mid-burst adds a legitimate probe call and fails
            // the zero-delta assert below on a slow runner (observed on CI: 6 vs 7) — stopped,
            // "reads add zero probe calls" is deterministic instead of a race against the timer.
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            await cts.CancelAsync();
            // ThrowsAnyAsync, not ThrowsAsync: which await the cancellation interrupts is a race
            // (PeriodicTimer tick vs Task.Delay), and the loser surfaces TaskCanceledException —
            // an OperationCanceledException subclass the exact-type assert rejected on slow CI.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            var beforeReads = probe.CallCount;
            for (var i = 0; i < 50; i++)
            {
                _ = store.GetVerdict(probe.DependencyName);
            }
            var afterReads = probe.CallCount;

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
            await prober.RunCycleAsync(Undebounced(TimeSpan.FromSeconds(5)), CancellationToken.None);

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

            // When the next verdict is produced (one cycle, a short timeout, debounce off)
            await prober.RunCycleAsync(Undebounced(TimeSpan.FromMilliseconds(50)), CancellationToken.None);

            // Then it reports unhealthy with the failure reason...
            var verdict = store.GetVerdict("slow-dep");
            Assert.NotNull(verdict);
            Assert.False(verdict.Healthy);
            Assert.Contains("timed out", verdict.Reason, StringComparison.OrdinalIgnoreCase);

            // ...and the probe service keeps running: a second cycle still completes cleanly,
            // never throwing out of RunCycleAsync
            await prober.RunCycleAsync(Undebounced(TimeSpan.FromMilliseconds(50)), CancellationToken.None);
            var secondVerdict = store.GetVerdict("slow-dep");
            Assert.NotNull(secondVerdict);
            Assert.False(secondVerdict.Healthy);
            Assert.Equal(2, secondVerdict.ConsecutiveFailureCount);
        }
    }

    // ---------------------------------------------------------------------
    // SPEC F70.2 AC5 (gh-#125) — a transient probe failure must not flip the verdict. Kokoro
    // serves /health from the event loop it renders on and blocks it for the whole render, so it
    // misses isolated probes while perfectly alive; flipping on the first miss routed live patter
    // to the Piper fallback ~25×/day on the demo box for no reason.
    // ---------------------------------------------------------------------

    public static class ScenarioProbeFlapDebounce
    {
        static DependencyProbeCadence Debounced(int threshold) =>
            new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(50), threshold);

        [Fact]
        public static async Task One_failed_probe_below_the_threshold_leaves_the_verdict_healthy()
        {
            // Given a dependency that has answered healthy at least once, then starts hanging
            var probe = new FakeDependencyProbe("flappy-dep", healthy: true);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);
            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);
            probe.Hang = true;

            // When exactly one probe fails, under a threshold of 2
            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);

            // Then the published verdict is still healthy — nothing reroutes off the primary
            var verdict = store.GetVerdict("flappy-dep");
            Assert.NotNull(verdict);
            Assert.True(verdict.Healthy);

            // ...but the miss is recorded, so the next failure in a row can flip it
            Assert.Equal(1, verdict.ConsecutiveFailureCount);

            // ...and a healthy verdict never carries a reason (the F70.2 invariant holds through
            // the debounce: the reason is dropped, not smuggled into a healthy snapshot)
            Assert.Null(verdict.Reason);
        }

        [Fact]
        public static async Task The_threshold_th_consecutive_failure_flips_the_verdict_with_its_reason()
        {
            // Given a dependency that hangs on every probe
            var probe = new FakeDependencyProbe("dead-dep", healthy: true, hang: true);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);

            // When it fails twice in a row, under a threshold of 2
            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);
            Assert.True(store.GetVerdict("dead-dep")!.Healthy, "first failure must not flip the verdict");

            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);

            // Then the verdict flips unhealthy and carries the failure reason — a genuinely dead
            // dependency is still caught, within threshold × interval
            var verdict = store.GetVerdict("dead-dep");
            Assert.NotNull(verdict);
            Assert.False(verdict.Healthy);
            Assert.Equal(2, verdict.ConsecutiveFailureCount);
            Assert.Contains("timed out", verdict.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static async Task A_healthy_probe_between_two_failures_resets_the_streak()
        {
            // Given a dependency that fails, recovers, then fails again — the exact gh-#125 shape,
            // where isolated render-length stalls are minutes apart and never consecutive
            var probe = new FakeDependencyProbe("intermittent-dep", healthy: true, hang: true);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);

            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);
            probe.Hang = false;
            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);
            probe.Hang = true;

            // When the second isolated failure lands
            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);

            // Then the verdict is STILL healthy: the intervening success reset the streak, so two
            // non-consecutive misses never accumulate into a flip
            var verdict = store.GetVerdict("intermittent-dep");
            Assert.NotNull(verdict);
            Assert.True(verdict.Healthy);
            Assert.Equal(1, verdict.ConsecutiveFailureCount);
        }

        [Fact]
        public static async Task A_threshold_of_one_preserves_the_original_flip_on_first_failure()
        {
            // Given the debounce explicitly disabled (threshold 1)
            var probe = new FakeDependencyProbe("dep", healthy: true, hang: true);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);

            // When one probe fails
            await prober.RunCycleAsync(Debounced(threshold: 1), CancellationToken.None);

            // Then it flips immediately — the pre-gh-#125 contract, still available to an operator
            // who wants the twitchiest possible detection
            var verdict = store.GetVerdict("dep");
            Assert.NotNull(verdict);
            Assert.False(verdict.Healthy);
        }

        [Fact]
        public static async Task A_deliberately_unconfigured_dependency_is_never_debounced()
        {
            // Given a probe reporting "disabled by design" (ProbeAsync returns false — e.g. an
            // empty Llm:Endpoint, SPEC F34.2) rather than failing
            var probe = new FakeDependencyProbe("unconfigured-dep", healthy: false);
            var store = new DependencyHealthStore();
            var prober = new DependencyHealthProber([probe], store, NullLogger<DependencyHealthProber>.Instance);

            // When one cycle runs under a threshold that WOULD debounce a failure
            await prober.RunCycleAsync(Debounced(threshold: 2), CancellationToken.None);

            // Then it is published immediately: not-configured is a deterministic declaration the
            // probe repeats every cycle, not a flap, and F69.2's probe-driven drop must not read
            // an unconfigured dependency as healthy for an interval
            var verdict = store.GetVerdict("unconfigured-dep");
            Assert.NotNull(verdict);
            Assert.False(verdict.Healthy);
            Assert.Equal(DependencyHealthProber.NotConfiguredReason, verdict.Reason);
        }
    }

    public static class ScenarioLiveCadence
    {
        // Virtual-time constants: values are arbitrary (nothing sleeps for them), chosen
        // human-readable. The probe hangs, so every cycle ends via PerProbeTimeout.
        static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
        static readonly TimeSpan PerProbeTimeout = TimeSpan.FromSeconds(2);

        // Real-time bound on waiting for the loop's thread-pool continuations to settle after an
        // Advance. Generous on purpose: it only caps the pathological case — the normal path
        // completes in milliseconds, and a starved runner can only be SLOW here, never elapse
        // extra cycles (virtual time moves solely via Advance).
        static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);

        [Fact]
        public static async Task Each_cycle_re_reads_the_cadence_so_an_edit_needs_no_restart()
        {
            // Given a running prober whose cadence delegate is backed by a mutable value — the
            // IOptionsMonitor stand-in (gh-#125: the knobs are allowlisted and Live, so a settings
            // PUT must reach the very next probe with no api restart). The loop runs on a
            // FakeTimeProvider (gh-#171): the old shape raced a wall-clock Task.Delay against the
            // real 20ms loop, and on a starved runner the delay overslept, extra timeout cycles
            // fit inside it, and threshold 5 flipped before the assert read it. Here cycles elapse
            // ONLY on Advance, so the failure count is bounded by the advances we make.
            var probe = new FakeDependencyProbe("dep", healthy: true, hang: true);
            var store = new DependencyHealthStore();
            var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
            var prober = new DependencyHealthProber(
                [probe], store, NullLogger<DependencyHealthProber>.Instance, time);

            var threshold = 100;
            DependencyProbeCadence Current() =>
                new(Interval, PerProbeTimeout, Volatile.Read(ref threshold));

            using var cts = new CancellationTokenSource();
            var runTask = prober.RunAsync(Current, cts.Token);

            // When a handful of cycles fail under the high threshold — at most a few dozen
            // advances can ever happen here, so the count stays far below 100 by construction
            // and the debounced verdict cannot have flipped, deterministically
            var accrued = await AdvanceUntil(time,
                () => (store.GetVerdict("dep")?.ConsecutiveFailureCount ?? 0) >= 3);
            Assert.True(accrued, "expected the hanging probe to accrue timeout failures");
            Assert.True(store.GetVerdict("dep")?.Healthy,
                "threshold 100 must debounce every failure accrued so far");

            // When the threshold is lowered underneath the running loop. The cycle already in
            // flight may have captured the old cadence at its start; the one after it must see 1.
            Volatile.Write(ref threshold, 1);

            // Then a following cycle applies the new value and flips the verdict — no restart.
            // Under threshold 100 the flip can ONLY come from a cycle that re-read the cadence.
            var flipped = await AdvanceUntil(time,
                () => store.GetVerdict("dep")?.Healthy == false);

            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

            Assert.True(flipped, "expected the lowered threshold to apply without restarting the loop");
        }

        /// <summary>
        /// Advances virtual time in <see cref="PerProbeTimeout"/> steps until
        /// <paramref name="condition"/> holds, giving the loop's continuations a short real-time
        /// window to settle between steps. Each step is enough virtual time to complete at least
        /// one full cycle (per-probe timeout + interval tick), and the step count is bounded, so
        /// a spec using this can never elapse an unbounded number of cycles.
        /// </summary>
        static async Task<bool> AdvanceUntil(
            Microsoft.Extensions.Time.Testing.FakeTimeProvider time, Func<bool> condition)
        {
            const int MaxSteps = 12;
            for (var step = 0; step < MaxSteps; step++)
            {
                if (await WaitUntil(condition, TimeSpan.FromMilliseconds(200)))
                {
                    return true;
                }

                time.Advance(PerProbeTimeout);
            }

            return await WaitUntil(condition, SettleBudget);
        }

        static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan budget)
        {
            var deadline = DateTimeOffset.UtcNow + budget;
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            return condition();
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

        [Fact]
        public static async Task Piper_probe_reports_not_configured_without_any_http_call_when_endpoint_is_empty()
        {
            // Given Tts:Fallback:Endpoint is empty — Piper not deployed (F70.1)
            var handler = new FakeHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("must not call out when unconfigured"));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Endpoint = "" });
            var probe = new PiperHealthProbe(http, optionsMonitor);

            // When it is probed
            var healthy = await probe.ProbeAsync(CancellationToken.None);

            // Then it reports false (not-configured) and never calls out
            Assert.False(healthy);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public static async Task Piper_probe_reports_healthy_on_the_no_text_500_a_real_piper_server_always_returns()
        {
            // Given a configured Piper endpoint that answers 500 — piper.http_server's ONE route
            // always 500s absent a `?text=` query, even when the process is perfectly healthy
            // (verified against the real artibex/piper-http image; see this probe's own remarks)
            var handler = new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Endpoint = "http://piper:5000" });
            var probe = new PiperHealthProbe(http, optionsMonitor);

            // When it is probed
            var healthy = await probe.ProbeAsync(CancellationToken.None);

            // Then it reports healthy anyway — reachability, not status code, is the signal here —
            // and hit the root path (no dedicated health route exists on this wrapper)
            Assert.True(healthy);
            var request = Assert.Single(handler.Requests);
            Assert.NotNull(request.RequestUri);
            Assert.Equal("/", request.RequestUri.AbsolutePath);
        }

        [Fact]
        public static async Task Piper_probe_uses_OPTIONS_so_a_healthy_server_logs_nothing()
        {
            // Given a configured Piper endpoint (gh-#64: GET/HEAD on piper.http_server's route
            // execute the handler and log a ValueError traceback per probe — 360 error lines/hour
            // of telemetry noise from a healthy server; Flask answers OPTIONS without invoking the
            // handler at all, verified against the pinned artibex/piper-http digest 2026-07-21)
            var handler = new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Endpoint = "http://piper:5000" });
            var probe = new PiperHealthProbe(http, optionsMonitor);

            // When it is probed
            await probe.ProbeAsync(CancellationToken.None);

            // Then the request method is OPTIONS — the one verb that keeps piper's error log quiet
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Options, request.Method);
        }

        [Fact]
        public static async Task Piper_probe_throws_when_the_endpoint_is_unreachable_so_the_prober_can_record_a_reason()
        {
            // Given Piper is unreachable (connection refused, DNS failure, ...)
            var handler = new FakeHttpMessageHandler((_, _) =>
                throw new HttpRequestException("connection refused"));
            using var http = new HttpClient(handler);
            var optionsMonitor = new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Endpoint = "http://piper:5000" });
            var probe = new PiperHealthProbe(http, optionsMonitor);

            // When/Then it throws — the driver, not the probe, decides the verdict's reason text
            await Assert.ThrowsAsync<HttpRequestException>(() => probe.ProbeAsync(CancellationToken.None));
        }
    }
}
