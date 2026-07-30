// gh-#147 — Tts:Fallback:* as an ordered resiliency chain
//
// BDD specification — xUnit (SPEC F70.1, F70.2, F70.3). The HARD requirement is pinned first: the
// shipped default config — compose.yaml's legacy flat Tts__Fallback__Endpoint/Voice pair;
// appsettings.json carries no Tts:Fallback section at all — must reproduce EXACTLY the pre-chain
// behavior (Kokoro primary → one Piper hop). Story190/Story191 keep running against the legacy
// flat-key shape untouched, so the whole original scenario suite doubles as the behavioral half of
// that equivalence proof; this file adds the config-resolution half plus the new chain semantics
// (order, per-hop skip/budget, legacy precedence, voice-on-the-wire, loud validation).

namespace GenWave.Tts.Tests.Specs;

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GenWave.Tts.Tests.Fakes;

public static class FeatureFallbackResiliencyChain
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    const string ShippedPiperEndpoint = "http://piper:5000";
    const string ShippedPiperVoice = "en_US-lessac-medium";

    static TtsFallbackOptions LegacyShippedOptions() =>
        new() { Endpoint = ShippedPiperEndpoint, Voice = ShippedPiperVoice };

    static TtsFallbackProfile PiperHop(
        string endpoint = ShippedPiperEndpoint, bool skipWhenUnhealthy = false, double? timeoutSeconds = null) =>
        new()
        {
            Engine = DependencyNames.Piper,
            Endpoint = endpoint,
            Voice = ShippedPiperVoice,
            SkipWhenUnhealthy = skipWhenUnhealthy,
            TimeoutSeconds = timeoutSeconds,
        };

    static TtsFallbackProfile KokoroHop(string endpoint = "http://backup-kokoro:8880", string voice = "") =>
        new() { Engine = DependencyNames.Kokoro, Endpoint = endpoint, Voice = voice };

    static FallbackTtsSynthesizer BuildRouter(
        FakeTtsSynthesizer primary,
        IEnumerable<IFallbackProfileRenderer> renderers,
        FakeDependencyHealth health,
        TtsFallbackOptions options,
        CapturingLogger<FallbackTtsSynthesizer>? logger = null) =>
        new(primary, renderers, health, new TestOptionsMonitor<TtsFallbackOptions>(options),
            logger ?? new CapturingLogger<FallbackTtsSynthesizer>());

    static DependencyHealthVerdict UnhealthyVerdict(string dependency) =>
        new(dependency, Healthy: false, DateTimeOffset.UtcNow, "connect failure", ConsecutiveFailureCount: 3);

    static IConfiguration BuildConfig(IReadOnlyDictionary<string, string?> pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

    static TtsFallbackOptions BindOptions(IConfiguration configuration)
    {
        var options = new TtsFallbackOptions();
        configuration.GetSection(TtsFallbackOptions.Section).Bind(options);
        return options;
    }

    // ------------------------------------------------------------------
    // HAPPY PATH — the gh-#147 hard requirement: bare deploy = today
    // ------------------------------------------------------------------

    public static class ScenarioDefaultConfigEquivalence
    {
        [Fact]
        public static void Shipped_compose_keys_resolve_to_exactly_the_legacy_single_piper_hop()
        {
            // Given exactly the fallback config the shipped compose.yaml deploys (the two legacy
            // flat env keys, no Profiles) bound through the real configuration binder
            var configuration = BuildConfig(new Dictionary<string, string?>
            {
                ["Tts:Fallback:Endpoint"] = ShippedPiperEndpoint,
                ["Tts:Fallback:Voice"] = ShippedPiperVoice,
            });

            // When the effective chain is resolved
            var chain = TtsFallbackChain.Resolve(BindOptions(configuration));

            // Then it is the implicit legacy chain: one piper hop, the flat keys' endpoint/voice,
            // default hop semantics (always attempted, no per-hop budget) — Kokoro primary → one
            // Piper hop, byte-for-byte today's shape (gh-#147's hard requirement).
            var hop = Assert.Single(chain.Hops);
            Assert.Equal(DependencyNames.Piper, hop.Engine);
            Assert.Equal(ShippedPiperEndpoint, hop.Endpoint);
            Assert.Equal(ShippedPiperVoice, hop.Voice);
            Assert.False(hop.SkipWhenUnhealthy);
            Assert.Null(hop.TimeoutSeconds);
        }

        [Fact]
        public static void Bare_config_with_no_fallback_section_resolves_to_the_empty_pass_through_chain()
        {
            // Given a configuration with no Tts:Fallback section at all (the shipped
            // appsettings.json outside compose) — the F70.1 disabled state
            var chain = TtsFallbackChain.Resolve(BindOptions(BuildConfig(new Dictionary<string, string?>())));

            // Then the chain is empty: FallbackTtsSynthesizer is a transparent pass-through to
            // Kokoro (Story190's own empty-endpoint spec pins the pass-through behavior itself).
            Assert.True(chain.IsEmpty);
        }

        [Fact]
        public static async Task Primary_failure_warn_keeps_the_legacy_greppable_text()
        {
            // Given the shipped legacy config and a Kokoro render that throws
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var fallback = new FakeProfileRenderer(DependencyNames.Piper);
            var logger = new CapturingLogger<FallbackTtsSynthesizer>();
            var router = BuildRouter(primary, [fallback], new FakeDependencyHealth(), LegacyShippedOptions(), logger);

            // When the render falls to the Piper hop
            await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the original warn class survives verbatim as a substring — the Loki dashboards
            // grepping it keep matching on a bare deploy (gh-#147).
            Assert.Contains(logger.Warnings, w => w.Contains("Kokoro render failed; retrying once via Piper fallback"));
        }

        [Fact]
        public static async Task Unhealthy_verdict_warn_keeps_the_legacy_greppable_text()
        {
            // Given the shipped legacy config and a cached-unhealthy Kokoro verdict
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeProfileRenderer(DependencyNames.Piper);
            var health = new FakeDependencyHealth();
            health.Set(UnhealthyVerdict(DependencyNames.Kokoro));
            var logger = new CapturingLogger<FallbackTtsSynthesizer>();
            var router = BuildRouter(primary, [fallback], health, LegacyShippedOptions(), logger);

            // When the render routes straight to the Piper hop
            await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the skip-primary warn class survives verbatim as a substring too.
            Assert.Contains(logger.Warnings, w => w.Contains("routing render straight to Piper fallback"));
        }

        [Fact]
        public static async Task Default_chain_attempts_its_piper_hop_even_when_pipers_own_verdict_is_unhealthy()
        {
            // Given the shipped legacy config, a Kokoro render that throws AND a cached-unhealthy
            // Piper verdict — pre-gh-#147, Piper was still attempted (the router never read its
            // verdict); the implicit legacy hop must not grow a probe gate on upgrade.
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var fallback = new FakeProfileRenderer(DependencyNames.Piper);
            var health = new FakeDependencyHealth();
            health.Set(UnhealthyVerdict(DependencyNames.Piper));
            var router = BuildRouter(primary, [fallback], health, LegacyShippedOptions());

            // When the render falls to the hop
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the hop is attempted regardless of its verdict — exactly today's behavior.
            Assert.NotNull(path);
            Assert.Equal(1, fallback.CallCount);
        }
    }

    // ------------------------------------------------------------------
    // HAPPY PATH — chain semantics
    // ------------------------------------------------------------------

    public static class ScenarioChainOrderExecution
    {
        [Fact]
        public static void Profiles_bind_from_the_indexed_config_array_in_order()
        {
            // Given a two-hop operator-built chain in the documented config shape
            var configuration = BuildConfig(new Dictionary<string, string?>
            {
                ["Tts:Fallback:Profiles:0:Engine"] = "kokoro",
                ["Tts:Fallback:Profiles:0:Endpoint"] = "http://backup-kokoro:8880",
                ["Tts:Fallback:Profiles:0:Voice"] = "am_michael",
                ["Tts:Fallback:Profiles:0:TimeoutSeconds"] = "20",
                ["Tts:Fallback:Profiles:1:Engine"] = "Piper",   // casing is operator-typed; normalized at resolve
                ["Tts:Fallback:Profiles:1:Endpoint"] = ShippedPiperEndpoint,
                ["Tts:Fallback:Profiles:1:SkipWhenUnhealthy"] = "true",
            });

            // When the effective chain is resolved
            var chain = TtsFallbackChain.Resolve(BindOptions(configuration));

            // Then both hops arrive in configured order with per-hop semantics intact
            Assert.Equal(2, chain.Hops.Count);
            Assert.Equal(DependencyNames.Kokoro, chain.Hops[0].Engine);
            Assert.Equal("am_michael", chain.Hops[0].Voice);
            Assert.Equal((double?)20, chain.Hops[0].TimeoutSeconds);
            Assert.Equal(DependencyNames.Piper, chain.Hops[1].Engine);
            Assert.True(chain.Hops[1].SkipWhenUnhealthy);
        }

        [Fact]
        public static async Task Hops_execute_in_configured_order_after_the_primary()
        {
            // Given a two-hop chain (remote kokoro, then piper) where the primary and the first
            // hop both throw
            var journal = new List<string>();
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var kokoroHop = new FakeProfileRenderer(DependencyNames.Kokoro)
            {
                CallJournal = journal,
                ThrowOnNextCall = new IOException("backup down"),
            };
            var piperHop = new FakeProfileRenderer(DependencyNames.Piper) { CallJournal = journal };
            var options = new TtsFallbackOptions
            {
                Profiles = [KokoroHop(), PiperHop()],
            };
            var router = BuildRouter(primary, [kokoroHop, piperHop], new FakeDependencyHealth(), options);

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the hops were attempted strictly in configured order and the last one rendered
            Assert.NotNull(path);
            Assert.Equal(new[] { "kokoro@http://backup-kokoro:8880", $"piper@{ShippedPiperEndpoint}" }, journal);
            Assert.Equal(0, kokoroHop.CallCount);   // attempted first — it threw
            Assert.Equal(1, piperHop.CallCount);
        }

        [Fact]
        public static async Task The_first_successful_hop_ends_the_chain()
        {
            // Given the same two-hop chain but a first hop that renders fine
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var kokoroHop = new FakeProfileRenderer(DependencyNames.Kokoro);
            var piperHop = new FakeProfileRenderer(DependencyNames.Piper);
            var options = new TtsFallbackOptions { Profiles = [KokoroHop(), PiperHop()] };
            var router = BuildRouter(primary, [kokoroHop, piperHop], new FakeDependencyHealth(), options);

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then execution stops at the first success — later hops are never touched
            Assert.NotNull(path);
            Assert.Equal(1, kokoroHop.CallCount);
            Assert.Equal(0, piperHop.CallCount);
        }
    }

    public static class ScenarioPerHopSemantics
    {
        [Fact]
        public static async Task A_hop_opted_into_the_probe_gate_is_skipped_on_a_cached_unhealthy_verdict()
        {
            // Given hop 1 (piper) opted into SkipWhenUnhealthy with a cached-unhealthy piper
            // verdict, hop 2 (kokoro) healthy, and a primary that throws
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var piperHop = new FakeProfileRenderer(DependencyNames.Piper);
            var kokoroHop = new FakeProfileRenderer(DependencyNames.Kokoro);
            var health = new FakeDependencyHealth();
            health.Set(UnhealthyVerdict(DependencyNames.Piper));
            var options = new TtsFallbackOptions
            {
                Profiles = [PiperHop(skipWhenUnhealthy: true), KokoroHop()],
            };
            var logger = new CapturingLogger<FallbackTtsSynthesizer>();
            var router = BuildRouter(primary, [piperHop, kokoroHop], health, options, logger);

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the gated hop is skipped with its own WARN and the next hop renders
            Assert.NotNull(path);
            Assert.Equal(0, piperHop.CallCount);
            Assert.Empty(piperHop.Profiles);   // never even attempted
            Assert.Equal(1, kokoroHop.CallCount);
            Assert.Contains(logger.Warnings, w => w.Contains("Skipping Piper fallback hop 1 of 2"));
        }

        [Fact]
        public static async Task A_hop_exceeding_its_render_budget_fails_over_to_the_next_hop()
        {
            // Given hop 1 (piper) hung forever under a 50ms per-hop budget and hop 2 (kokoro)
            // healthy, with a primary that throws
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var piperHop = new FakeProfileRenderer(DependencyNames.Piper)
            {
                DelayBeforeRender = Timeout.InfiniteTimeSpan,
            };
            var kokoroHop = new FakeProfileRenderer(DependencyNames.Kokoro);
            var options = new TtsFallbackOptions
            {
                Profiles = [PiperHop(timeoutSeconds: 0.05), KokoroHop()],
            };
            var router = BuildRouter(primary, [piperHop, kokoroHop], new FakeDependencyHealth(), options);

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the budget elapsing counted as an ordinary hop failure and the chain moved on
            Assert.NotNull(path);
            Assert.Equal(0, piperHop.CallCount);   // never completed — timed out
            Assert.Equal(1, kokoroHop.CallCount);
        }

        [Fact]
        public static async Task Caller_cancellation_is_never_rewritten_as_a_hop_timeout()
        {
            // Given a budgeted-but-hung hop and a caller that cancels while it hangs
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var piperHop = new FakeProfileRenderer(DependencyNames.Piper)
            {
                DelayBeforeRender = Timeout.InfiniteTimeSpan,
            };
            var options = new TtsFallbackOptions { Profiles = [PiperHop(timeoutSeconds: 30)] };
            var router = BuildRouter(primary, [piperHop], new FakeDependencyHealth(), options);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            // When / Then the caller's own cancellation propagates as OperationCanceledException —
            // it is never converted into a hop failure the chain would swallow.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => router.SynthesizeAsync("Coming up next", "af_heart", cancellation.Token));
        }
    }

    public static class ScenarioLegacyBackCompat
    {
        [Fact]
        public static async Task Profiles_supersede_the_flat_keys_when_both_are_present()
        {
            // Given BOTH shapes configured: legacy flat keys pointing at one piper, and a Profiles
            // chain naming only a kokoro hop
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var piperHop = new FakeProfileRenderer(DependencyNames.Piper);
            var kokoroHop = new FakeProfileRenderer(DependencyNames.Kokoro);
            var options = new TtsFallbackOptions
            {
                Endpoint = "http://legacy-piper:5000",
                Voice = ShippedPiperVoice,
                Profiles = [KokoroHop()],
            };
            var router = BuildRouter(primary, [piperHop, kokoroHop], new FakeDependencyHealth(), options);

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the operator-built chain wins outright — the legacy piper hop never exists
            Assert.NotNull(path);
            Assert.Equal(1, kokoroHop.CallCount);
            Assert.Equal(0, piperHop.CallCount);
        }

        [Fact]
        public static async Task Live_edit_of_the_legacy_endpoint_applies_to_the_very_next_render()
        {
            // Given the implicit legacy chain and a live settings edit repointing its endpoint
            // (PUT /api/settings → IOptionsMonitor, no restart — the STORY-190 contract)
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var fallback = new FakeProfileRenderer(DependencyNames.Piper);
            var monitor = new TestOptionsMonitor<TtsFallbackOptions>(LegacyShippedOptions());
            var router = new FallbackTtsSynthesizer(
                primary, [fallback], new FakeDependencyHealth(), monitor,
                new CapturingLogger<FallbackTtsSynthesizer>());

            monitor.CurrentValue = new TtsFallbackOptions { Endpoint = "http://piper-b:5000" };

            // When the next render falls to the hop
            await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the hop already carries the repointed endpoint — the chain is resolved fresh
            // per render, never frozen at boot.
            var profile = Assert.Single(fallback.Profiles);
            Assert.Equal("http://piper-b:5000", profile.Endpoint);
        }
    }

    public static class ScenarioVoiceSemantics
    {
        static (HttpClient Http, List<string> Bodies, List<string?> ContentTypes, List<Uri?> Uris) WireCapture()
        {
            var bodies = new List<string>();
            var contentTypes = new List<string?>();
            var uris = new List<Uri?>();
            var handler = new FakeHttpMessageHandler(async (request, ct) =>
            {
                uris.Add(request.RequestUri);
                contentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
                bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3, 4]),
                };
            });
            return (new HttpClient(handler), bodies, contentTypes, uris);
        }

        static TestOptionsMonitor<TtsOptions> CacheOptions(string cacheRoot) =>
            new(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });

        [Fact]
        public static async Task Kokoro_hop_sends_the_profile_voice_on_the_wire()
        {
            // Given a kokoro-kind hop with its own voice — gh-#147's honest-labeling flip: for
            // engines with a per-request selector, the profile voice is a REAL knob
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies, _, uris) = WireCapture();
                var renderer = new KokoroFallbackRenderer(http, CacheOptions(cacheRoot));

                // When it renders with a caller voice that differs from the profile voice
                await renderer.RenderAsync(
                    KokoroHop(voice: "am_michael"), "Coming up next", "af_heart", CancellationToken.None);

                // Then the PROFILE voice is what went on the wire, at the hop's own endpoint
                var body = Assert.Single(bodies);
                Assert.Contains("\"voice\":\"am_michael\"", body);
                Assert.Equal("http://backup-kokoro:8880/v1/audio/speech", Assert.Single(uris)?.ToString());
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task Kokoro_hop_forwards_the_request_voice_when_the_profile_voice_is_empty()
        {
            // Given a kokoro-kind hop with no voice of its own
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies, _, _) = WireCapture();
                var renderer = new KokoroFallbackRenderer(http, CacheOptions(cacheRoot));

                // When it renders
                await renderer.RenderAsync(KokoroHop(), "Coming up next", "af_heart", CancellationToken.None);

                // Then the caller's per-request voice goes on the wire unchanged
                Assert.Contains("\"voice\":\"af_heart\"", Assert.Single(bodies));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task Piper_hop_never_puts_any_voice_on_the_wire()
        {
            // Given a piper-kind hop whose profile carries a (display-only) voice — the upstream
            // piper.http_server wrapper has no per-request selector, so honesty means the wire
            // request is the bare text and nothing else
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var (http, bodies, contentTypes, _) = WireCapture();
                var renderer = new PiperTtsSynthesizer(http, CacheOptions(cacheRoot));

                // When it renders
                await renderer.RenderAsync(PiperHop(), "Coming up next", "af_heart", CancellationToken.None);

                // Then the body is exactly the text (text/plain — never form-encoded, which Flask
                // would consume): no profile voice, no request voice, anywhere on the wire
                Assert.Equal("Coming up next", Assert.Single(bodies));
                Assert.Equal("text/plain", Assert.Single(contentTypes));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------------
    // SAD PATH
    // ------------------------------------------------------------------

    public static class SadPathTotalChainFailure
    {
        [Fact]
        public static async Task The_last_hops_exception_propagates_unchanged()
        {
            // Given every engine down — the primary and the whole chain
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var fallback = new FakeProfileRenderer(DependencyNames.Piper)
            {
                ThrowOnNextCall = new IOException("piper down"),
            };
            var router = BuildRouter(primary, [fallback], new FakeDependencyHealth(), LegacyShippedOptions());

            // When / Then the LAST attempted engine's exception surfaces unwrapped — exactly the
            // pre-gh-#147 double-failure shape TtsSegmentSource's render-ahead catch expects
            // (Story190's SadPathBothEnginesDown pins the loud-skip half of that contract).
            var ex = await Assert.ThrowsAsync<IOException>(
                () => router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None));
            Assert.Equal("piper down", ex.Message);
        }

        [Fact]
        public static async Task A_chain_where_every_engine_is_gated_off_still_fails_loudly()
        {
            // Given a cached-unhealthy verdict gating off the primary AND the only (opted-in) hop
            // — nothing would ever be attempted
            var primary = new FakeTtsSynthesizer();
            var piperHop = new FakeProfileRenderer(DependencyNames.Piper);
            var health = new FakeDependencyHealth();
            health.Set(UnhealthyVerdict(DependencyNames.Kokoro));
            health.Set(UnhealthyVerdict(DependencyNames.Piper));
            var options = new TtsFallbackOptions { Profiles = [PiperHop(skipWhenUnhealthy: true)] };
            var router = BuildRouter(primary, [piperHop], health, options);

            // When / Then the render still fails LOUDLY (never silently returns nothing) so the
            // segment skips with a warn and music keeps playing — the never-silent posture.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None));
            Assert.Contains("without attempting a render", ex.Message);
            Assert.Equal(0, primary.CallCount);
            Assert.Equal(0, piperHop.CallCount);
        }
    }

    public static class SadPathValidation
    {
        static TtsFallbackOptions WithProfile(TtsFallbackProfile profile) => new() { Profiles = [profile] };

        [Fact]
        public static void An_unknown_engine_kind_fails_validation()
        {
            // Given a chain hop naming an engine this build has no renderer for
            var result = new TtsFallbackOptionsValidator().Validate(null, WithProfile(new TtsFallbackProfile
            {
                Engine = "espeak",
                Endpoint = "http://espeak:5002",
            }));

            // Then validation fails loudly with the offending key path in the message
            Assert.True(result.Failed);
            Assert.Contains("Tts:Fallback:Profiles:0:Engine 'espeak' is not a known engine kind", result.FailureMessage);
        }

        [Fact]
        public static void A_missing_or_relative_endpoint_fails_validation()
        {
            var result = new TtsFallbackOptionsValidator().Validate(null, WithProfile(new TtsFallbackProfile
            {
                Engine = DependencyNames.Piper,
                Endpoint = "piper:5000",   // no scheme — not absolute http/https
            }));

            Assert.True(result.Failed);
            Assert.Contains("must be an absolute http/https URL", result.FailureMessage);
        }

        [Fact]
        public static void A_non_positive_per_hop_budget_fails_validation()
        {
            var result = new TtsFallbackOptionsValidator().Validate(null, WithProfile(new TtsFallbackProfile
            {
                Engine = DependencyNames.Piper,
                Endpoint = ShippedPiperEndpoint,
                TimeoutSeconds = 0,
            }));

            Assert.True(result.Failed);
            Assert.Contains("TimeoutSeconds must be a positive number", result.FailureMessage);
        }

        [Fact]
        public static void An_empty_profiles_list_is_valid()
        {
            // The legacy flat keys are deliberately NOT policed by this validator — an operator
            // upgrading with old keys (or none) must never be broken at boot by a validator that
            // did not exist when they deployed.
            var result = new TtsFallbackOptionsValidator().Validate(null, LegacyShippedOptions());

            Assert.True(result.Succeeded);
        }

        [Fact]
        public static void A_well_formed_chain_passes_validation()
        {
            var result = new TtsFallbackOptionsValidator().Validate(null, new TtsFallbackOptions
            {
                Profiles = [KokoroHop(voice: "am_michael"), PiperHop(skipWhenUnhealthy: true, timeoutSeconds: 20)],
            });

            Assert.True(result.Succeeded);
        }

        [Fact]
        public static void An_unknown_engine_kind_fails_the_boot_loudly_through_the_real_wiring()
        {
            // Given the REAL composition (AddGenWaveTts wires TtsFallbackOptionsValidator +
            // ValidateOnStart) bound to a chain with an unknown engine kind
            var configuration = BuildConfig(new Dictionary<string, string?>
            {
                ["Tts:Fallback:Profiles:0:Engine"] = "espeak",
                ["Tts:Fallback:Profiles:0:Endpoint"] = "http://espeak:5002",
            });
            var services = new ServiceCollection();
            services.AddGenWaveTts(configuration);
            using var provider = services.BuildServiceProvider();

            // When / Then materializing the options throws OptionsValidationException — at boot,
            // ValidateOnStart surfaces exactly this failure before the station ever airs a render.
            var ex = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOptionsMonitor<TtsFallbackOptions>>().CurrentValue);
            Assert.Contains("not a known engine kind", ex.Message);
        }
    }
}
