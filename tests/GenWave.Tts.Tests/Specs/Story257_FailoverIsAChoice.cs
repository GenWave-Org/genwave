// STORY-257 — Failover is a choice, not a default (SPEC F99.2–F99.4)
//
// Dean's ruling, verbatim: "seems redundant to spin up Piper and then set it to never-use —
// we might want to rethink the whole failover setup and make it opt-in instead of the current
// always-on."
//
// This is mostly a CONFIGURATION AND PACKAGING change rather than new machinery: gh-#147
// already built the empty-chain semantics (`TtsFallbackChain.Empty` makes
// FallbackTtsSynthesizer a transparent pass-through to the primary — no health read, no
// retry, no second exception). What changes is the shipped default and whether the sidecar
// container runs at all.
//
// ⚠️ Ruled to apply to existing installs too, on the stated grounds that the demo box is
// currently the only installation. That rationale HAS AN EXPIRY DATE — it stops being true
// the first time a stranger runs a station, and must not be cited as precedent afterwards.
//
// F99.4 is the subtle one: on the piper-only topology Piper is the PRIMARY engine, not a
// fallback, so voice integrity is satisfied by Piper producing the DJ's own configured voice.
// That topology must configure it as primary rather than leaning on a chain.

namespace GenWave.Tts.Tests.Specs;

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureFailoverIsAChoice
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static TtsFallbackProfile PiperHop(string endpoint = "http://piper:5000") =>
        new() { Engine = DependencyNames.Piper, Endpoint = endpoint, Voice = "en_US-lessac-medium" };

    static TtsFallbackProfile KokoroHop(string endpoint = "http://backup-kokoro:8880") =>
        new() { Engine = DependencyNames.Kokoro, Endpoint = endpoint, Voice = "" };

    // ITtsSynthesizer, not FakeTtsSynthesizer (T148 review finding F2 widened this): most callers
    // still hand it a Fake, but ScenarioPiperOnlyIsUnaffectedInKind's voice-content fact below
    // needs the REAL PiperPrimaryTtsSynthesizer as the primary — a Fake echoes back whatever
    // voice it was called with, which proves nothing about what production actually puts on the
    // wire.
    static FallbackTtsSynthesizer BuildRouter(
        ITtsSynthesizer primary,
        IEnumerable<IFallbackProfileRenderer> hops,
        TtsFallbackOptions fallbackOptions,
        FakeDependencyHealth? health = null) =>
        new(primary, hops, health ?? new FakeDependencyHealth(), new TestOptionsMonitor<TtsFallbackOptions>(fallbackOptions),
            new CapturingLogger<FallbackTtsSynthesizer>());

    static TtsSegmentSource BuildSource(FallbackTtsSynthesizer router, string cacheRoot) =>
        new(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            router,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            NoCorrections.PronunciationProvider(),
            NoCorrections.PersonaPronunciationCache(),
            NoCorrections.PersonaPaceCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            NullLogger<TtsSegmentSource>.Instance);

    // LeadIn with no Track — PatterTemplateRenderer's documented safe fallback ("Coming up
    // next."), the same fixture shape Story256_NeverSomeoneElsesVoice's own DjBreakRequest uses.
    static SegmentRequest DjBreakRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station", PersonaName: "Rusty Strings");

    /// <summary>
    /// Runs <c>docker compose config --format json</c> against the given file stack — the
    /// Gh242/Gh310 render idiom, self-contained here rather than shared, matching those files'
    /// own precedent of not cross-referencing each other's process-invocation helpers.
    /// </summary>
    static JsonDocument RenderComposeConfig(params string[] composeFiles)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("compose");
        foreach (var file in composeFiles)
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(file);
        }
        startInfo.ArgumentList.Add("config");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");

        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["POSTGRES_PASSWORD"] = "story257-dummy",
            ["LIBRARY_DB_PASSWORD"] = "story257-dummy",
            ["STATION_DB_PASSWORD"] = "story257-dummy",
            ["ICECAST_SOURCE_PASSWORD"] = "story257-dummy",
            ["ICECAST_ADMIN_PASSWORD"] = "story257-dummy",
            ["ADMIN_PASSWORD"] = "story257-dummy",
            ["MEDIA_DIR"] = Path.GetTempPath(),
            ["PUBLIC_HOST"] = "story257.invalid",
            // Explicit-but-empty (gh-#249 idiom) — shadows both ambient COMPOSE_PROFILES and a
            // dev box's repo-root .env value, so this render sees no active profile regardless
            // of the box it runs on.
            ["COMPOSE_PROFILES"] = "",
        })
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start docker compose config");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker compose config failed (exit {process.ExitCode}): {stdErr}");

        return JsonDocument.Parse(stdOut);
    }

    // ------------------------------------------------------------------
    // HAPPY PATH
    // ------------------------------------------------------------------

    public static class ScenarioTheShippedDefaultConfiguresNoChain
    {
        [Fact]
        public static void A_fresh_install_resolves_an_empty_chain()
        {
            // Given a fresh install (no Tts:Fallback:* configured at all — the C# options
            // class's own defaults, byte-identical to what a bare appsettings.json binds)
            var chain = TtsFallbackChain.Resolve(new TtsFallbackOptions());

            // Then the effective fallback chain resolves empty.
            Assert.True(chain.IsEmpty);
        }

        [Fact]
        public static void The_shipped_compose_no_longer_sets_a_fallback_endpoint()
        {
            // The legacy flat key is what makes the chain non-empty today (TtsFallbackChain's
            // own precedence: Profiles, else legacy Endpoint, else Empty) — a repo-content
            // fact, no live stack needed, mirroring Story141's "grep/hash-assert" idiom. Checked
            // against non-comment lines only: compose.yaml's own explanatory comments legitimately
            // NAME the old key for documentation (how to opt in), which a bare substring search
            // would misread as the key still being SET.
            var assignmentLines = File.ReadAllLines(Path.Combine(RepoRoot, "compose.yaml"))
                .Where(line => !line.TrimStart().StartsWith('#'));

            Assert.DoesNotContain(assignmentLines, line => line.Contains("Tts__Fallback__Endpoint:", StringComparison.Ordinal));
            Assert.DoesNotContain(assignmentLines, line => line.Contains("Tts__Fallback__Voice:", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task An_empty_chain_is_a_transparent_pass_through()
        {
            // Given the shipped default (no Tts:Fallback:Profiles, no legacy Endpoint) and a
            // substitute engine standing by
            var primary = new FakeTtsSynthesizer();
            var substitute = new FakeProfileRenderer(DependencyNames.Piper);
            var router = BuildRouter(primary, [substitute], new TtsFallbackOptions());

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then it renders through the primary directly — gh-#147's existing contract, now
            // the default: no health read, no retry, no second exception in the log; a primary
            // failure would propagate exactly as it did before any fallback feature existed.
            Assert.NotNull(path);
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, substitute.CallCount);
        }
    }

    public static class ScenarioNoChainMeansNoSidecar
    {
        static readonly Lazy<JsonDocument> DefaultRender = new(() => RenderComposeConfig("compose.yaml"));

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_default_render_starts_no_fallback_engine_container()
        {
            // Rendered from the shipped compose files, the way the gh-#310 specs assert
            // service presence — no active COMPOSE_PROFILES, the state every fresh box starts
            // in (F99.3: a station with no fallback configured does not run the sidecar).
            Assert.False(DefaultRender.Value.RootElement.GetProperty("services").TryGetProperty("piper", out _));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public static void The_broadcast_path_is_otherwise_unchanged()
        {
            // db / icecast / engine / api / kokoro all still present — this removes a sidecar,
            // not a topology.
            var services = DefaultRender.Value.RootElement.GetProperty("services");
            foreach (var name in new[] { "db", "icecast", "engine", "api", "kokoro" })
                Assert.True(services.TryGetProperty(name, out _), $"expected {name} in the default render");
        }
    }

    public static class ScenarioOptingInRestoresSubstitution
    {
        [Fact]
        public static async Task A_configured_profile_chain_renders_on_primary_failure()
        {
            // Given an operator who configures Tts:Fallback:Profiles deliberately
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new HttpRequestException("primary down") };
            var hop = new FakeProfileRenderer(DependencyNames.Piper);
            var router = BuildRouter(primary, [hop], new TtsFallbackOptions { Profiles = [PiperHop()] });

            // When the primary fails
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the configured hop renders, exactly as the chain describes.
            Assert.NotNull(path);
            Assert.Equal(1, hop.CallCount);
        }

        [Fact]
        public static async Task The_configured_hop_order_is_honoured()
        {
            // Given a two-hop chain, both of which the primary would need in order
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new HttpRequestException("primary down") };
            var journal = new List<string>();
            var firstHop = new FakeProfileRenderer(DependencyNames.Kokoro)
            {
                CallJournal = journal,
                ThrowOnNextCall = new HttpRequestException("first hop down"),
            };
            var secondHop = new FakeProfileRenderer(DependencyNames.Piper) { CallJournal = journal };
            var router = BuildRouter(
                primary,
                [firstHop, secondHop],
                new TtsFallbackOptions { Profiles = [KokoroHop(), PiperHop()] });

            // When the primary AND the first hop both fail
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the hops were attempted in configured order, and the second one renders.
            Assert.NotNull(path);
            Assert.Equal([$"{DependencyNames.Kokoro}@http://backup-kokoro:8880", $"{DependencyNames.Piper}@http://piper:5000"], journal);
            Assert.Equal(1, secondHop.CallCount);
        }
    }

    public static class ScenarioPiperOnlyIsUnaffectedInKind
    {
        [Fact]
        public static async Task Piper_is_configured_as_the_primary_engine()
        {
            // Given the piper-only topology's shape: no fallback chain configured at all (F99.4
            // — the topology never leans on Tts:Fallback:*/Tts:EngineByKind, unlike the pre-
            // STORY-257 wiring), and a would-be substitute engine standing by
            var primary = new FakeTtsSynthesizer();   // stands in for PiperPrimaryTtsSynthesizer
            var substitute = new FakeProfileRenderer(DependencyNames.Piper);
            var router = BuildRouter(primary, [substitute], new TtsFallbackOptions());

            // When a break renders
            await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then it went straight through the primary — never through a fallback hop that
            // voice integrity would then have to refuse to use (F99.1's "right voice or no
            // speech" would reject a substitute's output outright).
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, substitute.CallCount);
        }

        [Fact]
        public static async Task The_break_airs_in_the_stations_one_piper_voice_never_a_per_dj_one()
        {
            // T148 review finding F2: this fact used to assert request.Voice == primary.LastVoice
            // against a FAKE that just echoes back whatever voice it was called with — true of the
            // fake, false of production. The real PiperPrimaryTtsSynthesizer/PiperWireProtocol
            // never puts ANY voice on the wire (no per-request selector exists on the upstream
            // piper.http_server wrapper — exactly one voice model is baked into the container at
            // start, compose.yaml's MODEL_DOWNLOAD_LINK), so a piper-only station's break airs in
            // the STATION's one configured Piper voice, never a per-DJ one. SPEC F99.4 was
            // reconciled 2026-08-14 at this task's own review to say exactly this: on this
            // topology "the DJ's own configured voice" IS the station's one Piper voice by
            // construction, and F99.1's "no other voice speaks as them" reads per-topology (see
            // docs/SPEC.md F99.4's reconciliation note).
            //
            // Given the piper-only topology (the REAL PiperPrimaryTtsSynthesizer as primary, wired
            // against a real stub server standing in for the piper sidecar) and a DJ break
            await using var piperStub = await PiperStubServer.StartAsync();
            var cacheRoot = CacheRoot();
            var primary = new PiperPrimaryTtsSynthesizer(
                new HttpClient(),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions
                {
                    PiperPrimaryEndpoint = piperStub.BaseUri.ToString(),
                    CacheRoot = cacheRoot,
                    Format = "wav",
                }));
            var substitute = new FakeProfileRenderer(DependencyNames.Piper);
            var source = BuildSource(BuildRouter(primary, [substitute], new TtsFallbackOptions()), cacheRoot);
            var request = DjBreakRequest();

            try
            {
                // When it renders
                var item = await source.RenderAsync(request, CancellationToken.None);

                // Then it airs...
                Assert.NotNull(item);
                Assert.Equal(1, piperStub.CallCount);
                // ...and the request's own voice never rode along on the wire — neither in the
                // body (a bare text/plain POST, PiperWireProtocol's whole contract) nor as a query
                // parameter. A body/query carrying the voice would red this fact.
                Assert.DoesNotContain(request.Voice, piperStub.LastBody ?? "", StringComparison.Ordinal);
                Assert.Equal("", piperStub.LastQueryString);
                Assert.Equal("text/plain", piperStub.LastContentType);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        static string CacheRoot() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    }

    // -------------------------------------------------------------------------------------
    // T148 REVIEW FINDING F1 — the primary-selection branch (TtsServiceCollectionExtensions),
    // PiperPrimaryTtsSynthesizer, and PiperWireProtocol were reachable by zero facts: the
    // reviewer deleted the whole selection and the suite stayed green. The two scenarios below
    // close that gap from opposite ends: (a) binds Tts:PiperPrimaryEndpoint through the REAL
    // configuration binder into the REAL AddGenWaveTts composition root and proves which concrete
    // primary a render actually reaches, against real (Kestrel-backed) stub servers; (b) pins
    // PiperPrimaryTtsSynthesizer/PiperWireProtocol's own wire mechanics directly, the same
    // KokoroStubServer idiom Story124_EndpointLiveRepoint.cs uses, tailored to Piper's shape via
    // PiperStubServer.
    // -------------------------------------------------------------------------------------
    public static class ScenarioPrimarySelectionWiring
    {
        static IConfiguration BuildConfig(string cacheRoot, string kokoroEndpoint, string? piperPrimaryEndpoint) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tts:Endpoint"] = kokoroEndpoint,
                ["Tts:Format"] = "wav",
                ["Tts:CacheRoot"] = cacheRoot,
                ["Tts:PiperPrimaryEndpoint"] = piperPrimaryEndpoint,
            }).Build();

        [Fact]
        public static async Task Setting_Tts_PiperPrimaryEndpoint_selects_PiperPrimaryTtsSynthesizer_as_the_primary()
        {
            // Given the real composition root, bound to a config that sets
            // Tts:PiperPrimaryEndpoint (the piper-only opt-in), against two real stub servers
            await using var kokoroStub = await KokoroStubServer.StartAsync();
            await using var piperStub = await PiperStubServer.StartAsync();
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGenWaveTts(BuildConfig(cacheRoot, kokoroStub.BaseUri.ToString(), piperStub.BaseUri.ToString()));
            using var provider = services.BuildServiceProvider();

            try
            {
                // When a render is asked for through the router the real composition root wires
                var router = provider.GetRequiredService<FallbackTtsSynthesizer>();
                await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

                // Then it reached the Piper stub, and Kokoro's stub was never touched at all.
                Assert.Equal(1, piperStub.CallCount);
                Assert.Equal(0, kokoroStub.SpeechCallCount);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task An_absent_Tts_PiperPrimaryEndpoint_selects_KokoroTtsSynthesizer_as_the_primary()
        {
            // Given the real composition root, bound to a config that leaves
            // Tts:PiperPrimaryEndpoint unset — every topology except the piper-only opt-in
            await using var kokoroStub = await KokoroStubServer.StartAsync();
            await using var piperStub = await PiperStubServer.StartAsync();
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGenWaveTts(BuildConfig(cacheRoot, kokoroStub.BaseUri.ToString(), piperPrimaryEndpoint: null));
            using var provider = services.BuildServiceProvider();

            try
            {
                // When a render is asked for through the router the real composition root wires
                var router = provider.GetRequiredService<FallbackTtsSynthesizer>();
                await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

                // Then it reached the Kokoro stub, and Piper's stub was never touched at all.
                Assert.Equal(1, kokoroStub.SpeechCallCount);
                Assert.Equal(0, piperStub.CallCount);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    public static class ScenarioPiperPrimaryWireProtocol
    {
        [Fact]
        public static async Task It_posts_the_bare_text_to_the_configured_root()
        {
            // Given a stub standing in for the piper sidecar, configured as the primary
            await using var stub = await PiperStubServer.StartAsync();
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var synthesizer = new PiperPrimaryTtsSynthesizer(
                new HttpClient(),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions
                {
                    PiperPrimaryEndpoint = stub.BaseUri.ToString(), CacheRoot = cacheRoot, Format = "wav",
                }));

            try
            {
                // When it renders
                var path = await synthesizer.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

                // Then the wire carried exactly the bare text, text/plain, at the configured root —
                // PiperWireProtocol's whole contract.
                Assert.NotNull(path);
                Assert.Equal(1, stub.CallCount);
                Assert.Equal("Coming up next", stub.LastBody);
                Assert.Equal("text/plain", stub.LastContentType);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }

        [Fact]
        public static async Task It_throws_when_resolved_with_no_piper_primary_endpoint_configured()
        {
            // Guards hand-built wiring (tests, tools) that skips TtsServiceCollectionExtensions'
            // own selection — see PiperPrimaryTtsSynthesizer's own remarks: unreachable through the
            // real composition root, which never resolves this class unless PiperPrimaryEndpoint
            // is set.
            var synthesizer = new PiperPrimaryTtsSynthesizer(
                new HttpClient(), new TestOptionsMonitor<TtsOptions>(new TtsOptions()));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => synthesizer.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None));

            Assert.Contains("PiperPrimaryEndpoint", ex.Message, StringComparison.Ordinal);
        }
    }

    // -------------------------------------------------------------------------------------
    // T148 REVIEW FINDING F3 — FallbackTtsSynthesizer used to hardcode DependencyNames.Kokoro for
    // both the cached-verdict lookup and the log {Engine} slot, regardless of which engine was
    // actually wired as the primary. Reachable bug: a piper-only station that live-PUTs
    // Tts:Fallback:Endpoint (opting into a backup chain ON TOP of its Piper primary, F99.2) had
    // its healthy Piper primary wrongly SKIPPED — absent-Kokoro's cached verdict (a piper-only
    // station never runs a Kokoro container at all) always reads unhealthy.
    // -------------------------------------------------------------------------------------
    public static class ScenarioPiperPrimaryHealthUsesItsOwnName
    {
        [Fact]
        public static async Task An_absent_kokoro_verdict_never_gates_off_a_piper_primary()
        {
            // Given a piper-only station's own shape: Kokoro's cached verdict is unhealthy (no
            // Kokoro container ever runs there to report otherwise) and a chain configured on top
            // of the primary (the live-PUT opt-in), but no verdict at all for Piper itself
            var primary = new FakeTtsSynthesizer();
            var hop = new FakeProfileRenderer(DependencyNames.Kokoro);
            var health = new FakeDependencyHealth();
            health.Set(new DependencyHealthVerdict(
                DependencyNames.Kokoro, Healthy: false, DateTimeOffset.UtcNow, "no such host", ConsecutiveFailureCount: 3));
            var router = new FallbackTtsSynthesizer(
                primary, [hop], health,
                new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Profiles = [KokoroHop()] }),
                new CapturingLogger<FallbackTtsSynthesizer>(),
                engineOverrides: null,
                primaryDependencyName: DependencyNames.Piper);

            // When a break renders
            await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the primary is tried — Kokoro's absence never gates off a healthy Piper primary.
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, hop.CallCount);
        }

        [Fact]
        public static async Task A_genuinely_unhealthy_piper_primary_is_named_correctly_in_the_skip_warn()
        {
            // Given the piper primary's OWN verdict is unhealthy this time
            var primary = new FakeTtsSynthesizer();
            var hop = new FakeProfileRenderer(DependencyNames.Kokoro);
            var health = new FakeDependencyHealth();
            health.Set(new DependencyHealthVerdict(
                DependencyNames.Piper, Healthy: false, DateTimeOffset.UtcNow, "connect failure", ConsecutiveFailureCount: 3));
            var logger = new CapturingLogger<FallbackTtsSynthesizer>();
            var router = new FallbackTtsSynthesizer(
                primary, [hop], health,
                new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Profiles = [KokoroHop()] }),
                logger,
                engineOverrides: null,
                primaryDependencyName: DependencyNames.Piper);

            // When a break renders
            await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the primary is skipped and the WARN names Piper — the log {Engine} slot travels
            // with the actual primary, never a hardcoded "Kokoro" literal.
            Assert.Equal(0, primary.CallCount);
            Assert.Equal(1, hop.CallCount);
            Assert.Contains(logger.Warnings, w => w.Contains("Piper cached verdict is unhealthy", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Warnings, w => w.Contains("Kokoro cached verdict is unhealthy", StringComparison.Ordinal));
        }
    }

    // -------------------------------------------------------------------------------------
    // T148 REVIEW FINDING F4 — PiperHealthProbe used to report "not configured" whenever the
    // fallback chain carried no piper hop. Post-T148 the piper-only overlay has no chain AT ALL
    // (Piper is the PRIMARY, not a hop, SPEC F99.4) — so the engine producing 100% of a piper-only
    // station's speech was never probed at all, a T148-introduced regression against F99.5's "an
    // operator must be able to tell the DJ is silent because the engine is down" legibility
    // promise.
    // -------------------------------------------------------------------------------------
    public static class ScenarioPiperHealthProbeCoversThePrimaryEngine
    {
        [Fact]
        public static async Task It_probes_the_piper_primary_endpoint_when_the_chain_carries_no_hop()
        {
            // Given the piper-only topology's shape: no fallback chain at all, but a piper-primary
            // endpoint configured
            var handler = new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            using var http = new HttpClient(handler);
            var fallbackOptions = new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions());
            var ttsOptions = new TestOptionsMonitor<TtsOptions>(new TtsOptions { PiperPrimaryEndpoint = "http://piper:5000" });
            var probe = new PiperHealthProbe(http, fallbackOptions, ttsOptions);

            // When it is probed
            var healthy = await probe.ProbeAsync(CancellationToken.None);

            // Then it reaches the primary endpoint, OPTIONS as ever (gh-#64's quiet-probe contract).
            Assert.True(healthy);
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Options, request.Method);
            Assert.Equal("http://piper:5000/", request.RequestUri?.ToString());
        }

        [Fact]
        public static async Task It_still_reports_not_configured_when_neither_a_chain_hop_nor_a_primary_endpoint_exists()
        {
            // Given neither shape is configured — the genuinely disabled-by-design state (F70.1)
            var handler = new FakeHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("must not call out when unconfigured"));
            using var http = new HttpClient(handler);
            var probe = new PiperHealthProbe(
                http,
                new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions()),
                new TestOptionsMonitor<TtsOptions>(new TtsOptions()));

            // When it is probed
            var healthy = await probe.ProbeAsync(CancellationToken.None);

            // Then it reports false and never calls out.
            Assert.False(healthy);
            Assert.Empty(handler.Requests);
        }
    }

    // -------------------------------------------------------------------------------------
    // T148 REVIEW FINDING F5 — [Url] rejects "", boot-crashing the very empty default
    // TtsOptions.PiperPrimaryEndpoint's own doc comment documents as legal ("Kokoro is primary,
    // unchanged"). Mirrors SettingValidator's own "empty or absolute http/https" shape for the
    // equivalent live-edit key, Tts:Fallback:Endpoint.
    // -------------------------------------------------------------------------------------
    public static class ScenarioPiperPrimaryEndpointValidation
    {
        static ValidateOptionsResult Validate(TtsOptions options) =>
            new DataAnnotationValidateOptions<TtsOptions>(name: null).Validate(null, options);

        [Fact]
        public static void An_empty_PiperPrimaryEndpoint_boots_clean()
        {
            var result = Validate(new TtsOptions { PiperPrimaryEndpoint = "" });

            Assert.True(result.Succeeded);
        }

        [Fact]
        public static void A_null_PiperPrimaryEndpoint_boots_clean()
        {
            var result = Validate(new TtsOptions { PiperPrimaryEndpoint = null });

            Assert.True(result.Succeeded);
        }

        [Fact]
        public static void A_non_absolute_http_value_fails_validation()
        {
            var result = Validate(new TtsOptions { PiperPrimaryEndpoint = "piper:5000" });

            Assert.True(result.Failed);
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioAnOptedInChainThatFails
    {
        [Fact]
        public static async Task Total_chain_failure_drops_the_break_under_voice_integrity()
        {
            // Given a configured chain whose every hop fails, same as the primary
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new HttpRequestException("primary down") };
            var hop = new FakeProfileRenderer(DependencyNames.Piper) { ThrowOnNextCall = new IOException("hop down") };
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                var router = BuildRouter(primary, [hop], new TtsFallbackOptions { Profiles = [PiperHop()] });
                var source = BuildSource(router, cacheRoot);

                // When a break comes due
                var item = await source.RenderAsync(DjBreakRequest(), CancellationToken.None);

                // Then it is dropped, never aired in some other voice — opting into substitution
                // does not opt out of F99.1.
                Assert.Null(item);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
                if (Directory.Exists(primary.OutputDirectory)) Directory.Delete(primary.OutputDirectory, recursive: true);
            }
        }
    }
}
