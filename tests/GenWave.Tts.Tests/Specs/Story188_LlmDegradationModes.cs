// STORY-188 — LLM degradation modes with operator pin
//
// BDD specification — xUnit (SPEC F69.1–F69.5). Implements PLAN T32's pending facts, amended per
// T32's review pass: Soft is "minimized calls" (a cadence-limited real attempt, not zero) and Soft
// -> Hard chains automatically during a sustained outage (either the throttled real attempt's own
// failures, or the independent background dependency probe) — see DegradationController's and
// DegradationGatedCopyWriter's own remarks for the mechanism.
//
// ScenarioModesExist/SadPathOperatorActions drive the real production seam
// (DegradationGatedCopyWriter wrapping a real LlmCopyWriter against a real Kestrel-backed
// MockCompletionsServer, mirroring Story119/Story123's own idiom) so "Soft uses the cheap copy
// path"/"Hard makes zero LLM calls" are proven against an LLM that WOULD succeed if it were ever
// reached — never a coincidence of the fake failing anyway. ScenarioAutomaticTransitions/
// ScenarioOperatorPin/ScenarioObservability mostly drive DegradationController directly with a fake
// clock (STORY-188 — no DateTime.Now anywhere in this feature) and a fake probe store; one fact
// (Unpinning_resumes_automatic_transitions) additionally drives the real DegradationGatedCopyWriter
// seam so the Soft -> Hard demonstration is proven against the actual playout-facing call path, not
// a hand-recorded holder failure — its MockCompletionsServer never receives a request either way,
// since the probe-driven drop fires before any attempt is made. AC coverage note: the admin-status
// visibility half of AC3/AC4 is exercised through the Host pipeline in T32's wire acceptance
// (Story188_DegradationStatusEndpoint.cs), not here.

using Xunit;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureLlmDegradationModes
{
    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest BackAnnounceRequest() =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static DateTimeOffset BaseInstant => new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A controller plus its own dependencies, exposed individually so a fact can drive the exact
    /// signal it cares about (record a failure, set a probe verdict, advance the clock, flip the
    /// pin) without reaching into private state.
    /// </summary>
    static (DegradationController Controller, LlmCopyStatusHolder Holder, FakeDependencyHealth Health,
        FakeTimeProvider Clock, TestOptionsMonitor<LlmOptions> LlmOptions,
        TestOptionsMonitor<DegradationOptions> DegradationOptions) BuildController(
        string endpoint = "https://llm.example/v1", string pin = "auto",
        int threshold = 3, int cooldownSeconds = 60)
    {
        var holder = new LlmCopyStatusHolder();
        var health = new FakeDependencyHealth();
        var clock = new FakeTimeProvider(BaseInstant);
        var llmOptions = new TestOptionsMonitor<LlmOptions>(new LlmOptions
        {
            Endpoint = endpoint,
            DegradationPin = pin,
        });
        var degradationOptions = new TestOptionsMonitor<DegradationOptions>(new DegradationOptions
        {
            ConsecutiveFailureThreshold = threshold,
            CooldownSeconds = cooldownSeconds,
        });
        var controller = new DegradationController(
            health, holder, llmOptions, degradationOptions, clock, new CapturingLogger<DegradationController>());
        return (controller, holder, health, clock, llmOptions, degradationOptions);
    }

    /// <summary>
    /// The real playout-side copy-writer chain (SPEC F69.1, F69.4): DegradationGatedCopyWriter
    /// wrapping a real LlmCopyWriter (talking to <paramref name="endpoint"/>) and a real
    /// TemplateCopyWriter, driven by a controller pinned to <paramref name="pin"/> — the exact
    /// production shape TtsServiceCollectionExtensions wires behind ISegmentCopyWriter. Shares its
    /// controller's own fake clock/degradation options with the writer's Soft cadence gate (see
    /// DegradationGatedCopyWriter's own remarks) so a fact can advance one clock and see both the
    /// mode machinery and the cadence window move together, deterministically.
    /// </summary>
    static (DegradationGatedCopyWriter Writer, TemplateCopyWriter Template, DegradationController Controller,
        LlmCopyStatusHolder Holder, FakeDependencyHealth Health, FakeTimeProvider Clock,
        TestOptionsMonitor<LlmOptions> LlmOptions) BuildGatedWriter(
        Uri endpoint, string pin, int cooldownSeconds = 60)
    {
        var (controller, holder, health, clock, llmOptions, degradationOptions) =
            BuildController(endpoint.ToString(), pin, cooldownSeconds: cooldownSeconds);
        llmOptions.CurrentValue.Model = "test-model";
        llmOptions.CurrentValue.TimeoutSeconds = 5;
        llmOptions.CurrentValue.MaxCopyChars = 450;

        var template = new TemplateCopyWriter(new PatterTemplateRenderer());
        var llmWriter = new LlmCopyWriter(
            template, new FakeHttpClientFactory(), llmOptions, holder, new FakeActivePersonaAccessor(),
            new CapturingLogger<LlmCopyWriter>());
        var writer = new DegradationGatedCopyWriter(controller, llmWriter, template, degradationOptions, clock);
        return (writer, template, controller, holder, health, clock, llmOptions);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — modes exist, music never stops (F69.1)
    // ---------------------------------------------------------------------

    public static class ScenarioModesExist
    {
        [Fact]
        public static async Task Playout_completes_a_full_segment_cycle_in_every_mode()
        {
            // Given each mode Normal, Soft, and Hard in turn
            await using var mock = await MockCompletionsServer.StartAsync();
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                foreach (var pin in new[] { "normal", "soft", "hard" })
                {
                    var (gated, _, _, _, _, _, _) = BuildGatedWriter(mock.BaseUri, pin);
                    var synth = new FakeTtsSynthesizer();
                    var ttsOptions = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
                    var source = new TtsSegmentSource(
                        gated, synth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(),
                        NoCorrections.Provider(), ttsOptions,
                        new CapturingLogger<TtsSegmentSource>());

                    try
                    {
                        // When the playout loop runs a full segment cycle (lead-in + back-announce
                        // — the two LLM-eligible kinds a real unit renders around one track, SPEC
                        // F34.2)
                        var leadIn = await source.RenderAsync(LeadInRequest(), CancellationToken.None);
                        var backAnnounce = await source.RenderAsync(BackAnnounceRequest(), CancellationToken.None);

                        // Then music selection and playout complete in every mode (F69.1) — both
                        // segments render to a real MediaItem, never dropped, in Normal/Soft/Hard alike.
                        Assert.NotNull(leadIn);
                        Assert.NotNull(backAnnounce);
                    }
                    finally
                    {
                        if (Directory.Exists(synth.OutputDirectory))
                            Directory.Delete(synth.OutputDirectory, recursive: true);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task Soft_uses_the_cheap_copy_path()
        {
            // Given Soft mode, with an LLM that WOULD succeed if it were ever reached, and this
            // cooldown window's one allowed real attempt already consumed (SPEC F69.1 "minimized
            // calls" — Soft permits exactly one real attempt per cooldown window, see
            // DegradationGatedCopyWriter's own cadence remarks; that single attempt is proven
            // separately by Soft_allows_exactly_one_real_llm_attempt_per_cooldown_window below)
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.ReplyContent = "An LLM-authored line that must never reach the air for an ordinary Soft segment.";
            var (gated, template, _, _, _, _, _) = BuildGatedWriter(mock.BaseUri, pin: "soft");
            var request = LeadInRequest();
            await gated.WriteAsync(request, CancellationToken.None); // consumes the window's one real attempt
            var requestsAfterFirstAttempt = mock.RequestCount;

            // When a further ordinary segment is written, still inside the same cooldown window
            var copy = await gated.WriteAsync(request, CancellationToken.None);

            // Then copy comes from the template/canned path (F69.1) — the mock's reply never
            // reaches the air for this segment, and no further request reaches the mock beyond the
            // one already-consumed attempt.
            var templateCopy = await template.WriteAsync(request, CancellationToken.None);
            Assert.Equal(templateCopy.Text, copy.Text);
            Assert.Equal(requestsAfterFirstAttempt, mock.RequestCount);
        }

        [Fact]
        public static async Task Soft_allows_exactly_one_real_llm_attempt_per_cooldown_window()
        {
            // Given Soft mode with a short cooldown window
            await using var mock = await MockCompletionsServer.StartAsync();
            var (gated, _, _, _, _, clock, _) = BuildGatedWriter(mock.BaseUri, pin: "soft", cooldownSeconds: 30);
            var request = LeadInRequest();

            // When three segments are written — two inside the same window, then a third after the
            // window has elapsed
            await gated.WriteAsync(request, CancellationToken.None); // window 1's one real attempt
            await gated.WriteAsync(request, CancellationToken.None); // still window 1 — template only
            clock.Advance(TimeSpan.FromSeconds(31));
            await gated.WriteAsync(request, CancellationToken.None); // window 2's one real attempt

            // Then exactly one real call reached the mock per elapsed cooldown window — two calls
            // total, never zero and never more than one per window (SPEC F69.1 "minimized calls",
            // T32 review finding)
            Assert.Equal(2, mock.RequestCount);
        }

        [Fact]
        public static async Task Hard_makes_zero_llm_calls()
        {
            // Given Hard mode, with an LLM that WOULD succeed if it were ever reached
            await using var mock = await MockCompletionsServer.StartAsync();
            var (gated, _, _, _, _, _, _) = BuildGatedWriter(mock.BaseUri, pin: "hard");

            // When a full segment cycle runs (lead-in + back-announce)
            await gated.WriteAsync(LeadInRequest(), CancellationToken.None);
            await gated.WriteAsync(BackAnnounceRequest(), CancellationToken.None);

            // Then zero LLM calls are made across the cycle (F69.1) — Hard has no cadence exception.
            Assert.Equal(0, mock.RequestCount);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — automatic drop and recovery (F69.2)
    // ---------------------------------------------------------------------

    public static class ScenarioAutomaticTransitions
    {
        [Fact]
        public static void Consecutive_failures_drop_the_mode_one_step()
        {
            // Given N consecutive LLM failures per the configured threshold
            var (controller, holder, _, clock, _, _) = BuildController(threshold: 3);
            for (var i = 0; i < 3; i++)
                holder.Record(LlmAttemptOutcome.Failed, clock.GetUtcNow());

            // When the controller evaluates
            var status = controller.Evaluate();

            // Then the mode drops one step (F69.2)
            Assert.Equal(DegradationMode.Soft, status.Mode);
            Assert.Contains("consecutive", status.Cause, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static void Probe_success_plus_cooldown_raises_the_mode_one_step()
        {
            // Given a mode already dropped one step by consecutive failures
            var (controller, holder, health, clock, _, _) = BuildController(threshold: 3, cooldownSeconds: 60);
            for (var i = 0; i < 3; i++)
                holder.Record(LlmAttemptOutcome.Failed, clock.GetUtcNow());
            Assert.Equal(DegradationMode.Soft, controller.Evaluate().Mode);

            // ... plus a cached probe success and an elapsed cooldown
            health.Set(new DependencyHealthVerdict(
                DependencyNames.Ollama, Healthy: true, clock.GetUtcNow(), Reason: null, ConsecutiveFailureCount: 0));
            clock.Advance(TimeSpan.FromSeconds(61));

            // When the controller evaluates
            var status = controller.Evaluate();

            // Then the mode raises one step (F69.2)
            Assert.Equal(DegradationMode.Normal, status.Mode);
            Assert.Contains("healthy", status.Cause, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static void Sustained_probe_unhealthy_drops_soft_to_hard_after_cooldown()
        {
            // Given a mode already dropped to Soft by consecutive failures, unpinned — Soft's own
            // real-call attempts are cadence-throttled (DegradationGatedCopyWriter), so this proves
            // the SECOND, independent signal: the background dependency probe.
            var (controller, holder, health, clock, _, _) = BuildController(threshold: 3, cooldownSeconds: 60);
            for (var i = 0; i < 3; i++)
                holder.Record(LlmAttemptOutcome.Failed, clock.GetUtcNow());
            Assert.Equal(DegradationMode.Soft, controller.Evaluate().Mode);

            // ... plus a cached probe verdict that stays unhealthy (a sustained outage, not a
            // single failed call) held past the cooldown
            health.Set(new DependencyHealthVerdict(
                DependencyNames.Ollama, Healthy: false, clock.GetUtcNow(), Reason: "connection refused", ConsecutiveFailureCount: 5));
            clock.Advance(TimeSpan.FromSeconds(61));

            // When the controller evaluates
            var status = controller.Evaluate();

            // Then the mode drops one further step to Hard (F69.2 — Soft is not a one-way trap;
            // drops chain during a sustained outage) with a cause naming the probe
            Assert.Equal(DegradationMode.Hard, status.Mode);
            Assert.Contains("probe", status.Cause, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static void Not_configured_probe_reason_never_drives_the_probe_drop()
        {
            // Given a mode already dropped to Soft by consecutive failures, unpinned, with the LLM
            // itself configured (a non-empty Llm:Endpoint — otherwise the dedicated not-configured
            // branch would already report Hard on its own, never reaching this path at all)
            var (controller, holder, health, clock, _, _) = BuildController(threshold: 3, cooldownSeconds: 60);
            for (var i = 0; i < 3; i++)
                holder.Record(LlmAttemptOutcome.Failed, clock.GetUtcNow());
            Assert.Equal(DegradationMode.Soft, controller.Evaluate().Mode);

            // ... plus a stale "not configured" Ollama verdict held past cooldown (e.g. left over
            // from before Llm:Endpoint was set) — a disabled-by-design reading, never an outage
            health.Set(new DependencyHealthVerdict(
                DependencyNames.Ollama, Healthy: false, clock.GetUtcNow(),
                Reason: DependencyHealthProber.NotConfiguredReason, ConsecutiveFailureCount: 5));
            clock.Advance(TimeSpan.FromSeconds(61));

            // When the controller evaluates
            var status = controller.Evaluate();

            // Then the mode stays at Soft — a "not configured" reading never triggers the
            // probe-driven drop (T32 review finding)
            Assert.Equal(DegradationMode.Soft, status.Mode);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — operator pin (F69.3)
    // ---------------------------------------------------------------------

    public static class ScenarioOperatorPin
    {
        [Fact]
        public static void Pinned_mode_ignores_failures_and_recoveries()
        {
            // Given the pin setting active (held to Hard)
            var (controller, holder, health, clock, _, _) = BuildController(pin: "hard", threshold: 3, cooldownSeconds: 60);
            Assert.Equal(DegradationMode.Hard, controller.Evaluate().Mode);

            // When failures or recoveries occur
            for (var i = 0; i < 10; i++)
                holder.Record(LlmAttemptOutcome.Failed, clock.GetUtcNow());
            health.Set(new DependencyHealthVerdict(
                DependencyNames.Ollama, Healthy: true, clock.GetUtcNow(), Reason: null, ConsecutiveFailureCount: 0));
            clock.Advance(TimeSpan.FromSeconds(120));

            // Then the mode stays pinned (F69.3)
            var status = controller.Evaluate();
            Assert.Equal(DegradationMode.Hard, status.Mode);
            Assert.True(status.Pinned);
        }

        [Fact]
        public static async Task Unpinning_resumes_automatic_transitions()
        {
            // Given a pinned mode (held to Soft) ... driven through the production shape
            // (DegradationGatedCopyWriter, not a hand-recorded holder failure) so the demonstration
            // below proves the real playout-facing seam, not just the controller in isolation.
            await using var mock = await MockCompletionsServer.StartAsync();
            var (gated, _, controller, _, health, clock, llmOptions) = BuildGatedWriter(mock.BaseUri, pin: "soft");
            var pinnedStatus = controller.Evaluate();
            Assert.Equal(DegradationMode.Soft, pinnedStatus.Mode);
            Assert.True(pinnedStatus.Pinned);

            // ... removed
            llmOptions.CurrentValue.DegradationPin = "auto";
            var unpinnedStatus = controller.Evaluate();
            Assert.False(unpinnedStatus.Pinned);
            Assert.Equal(DegradationMode.Soft, unpinnedStatus.Mode); // unpinning alone changes nothing

            // When a sustained outage is observed by the independent background health probe (never
            // a real call outcome — this is the probe-driven half of F69.2's "Soft is not a one-way
            // trap" fix) and the cooldown elapses ...
            health.Set(new DependencyHealthVerdict(
                DependencyNames.Ollama, Healthy: false, clock.GetUtcNow(), Reason: "connection refused", ConsecutiveFailureCount: 3));
            clock.Advance(TimeSpan.FromSeconds(61));

            // ... and the very next playout render runs through the real gated-writer seam
            await gated.WriteAsync(LeadInRequest(), CancellationToken.None);

            // Then auto transitions resumed (F69.3) and chained the mode one further step to Hard
            // (F69.2), observed on the controller the render itself just re-evaluated.
            var status = controller.Evaluate();
            Assert.Equal(DegradationMode.Hard, status.Mode);
            Assert.False(status.Pinned);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — every transition logged with its cause (F69.5)
    // ---------------------------------------------------------------------

    public static class ScenarioObservability
    {
        [Fact]
        public static void Every_transition_is_logged_with_its_cause()
        {
            // Given any mode transition, auto or pinned
            var holder = new LlmCopyStatusHolder();
            var clock = new FakeTimeProvider(BaseInstant);
            var llmOptions = new TestOptionsMonitor<LlmOptions>(new LlmOptions
            {
                Endpoint = "https://llm.example/v1",
                DegradationPin = "auto",
            });
            var degradationOptions = new TestOptionsMonitor<DegradationOptions>(new DegradationOptions
            {
                ConsecutiveFailureThreshold = 3,
                CooldownSeconds = 60,
            });
            var logger = new CapturingLogger<DegradationController>();
            var controller = new DegradationController(
                new FakeDependencyHealth(), holder, llmOptions, degradationOptions, clock, logger);

            // An auto drop ...
            for (var i = 0; i < 3; i++)
                holder.Record(LlmAttemptOutcome.Failed, clock.GetUtcNow());
            controller.Evaluate();

            // ... and a pin
            llmOptions.CurrentValue.DegradationPin = "hard";
            controller.Evaluate();

            // When logs are read
            // Then the transition and its cause are present (F69.5)
            Assert.Contains(logger.Messages, m => m.Contains("consecutive", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(logger.Messages, m => m.Contains("pinned", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — operator actions never gated (F69.4)
    // ---------------------------------------------------------------------

    public static class SadPathOperatorActions
    {
        [Fact]
        public static async Task Explicit_operator_render_is_attempted_even_in_hard_mode()
        {
            // Given Hard mode active
            await using var mock = await MockCompletionsServer.StartAsync();
            mock.Mode = MockCompletionsMode.Fail;
            var (controller, holder, _, _, llmOptions, _) = BuildController(mock.BaseUri.ToString(), pin: "hard");
            Assert.Equal(DegradationMode.Hard, controller.Evaluate().Mode);

            // The operator-explicit seam (IPersonaPreviewWriter) is registered directly against
            // LlmCopyWriter in production (TtsServiceCollectionExtensions) — never through
            // DegradationGatedCopyWriter — so this constructs it the same way, with no controller
            // in sight, to prove the "never gated" property holds structurally, not by a runtime check.
            IPersonaPreviewWriter previewWriter = new LlmCopyWriter(
                new TemplateCopyWriter(new PatterTemplateRenderer()), new FakeHttpClientFactory(), llmOptions,
                holder, new FakeActivePersonaAccessor(), new CapturingLogger<LlmCopyWriter>());

            // When an operator triggers an explicit preview/test render
            var result = await previewWriter.WritePreviewAsync(LeadInRequest(), personaOverride: null, CancellationToken.None);

            // Then the LLM call is attempted — it reached the mock even though the controller
            // reports Hard — and the failure is reported honestly (F69.4), never silently
            // substituted with template copy.
            Assert.Equal(1, mock.RequestCount);
            Assert.IsType<PersonaPreviewResult.Failed>(result);
        }
    }
}
