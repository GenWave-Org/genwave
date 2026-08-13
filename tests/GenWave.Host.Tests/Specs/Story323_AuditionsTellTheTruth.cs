// STORY-323 — Auditions tell the truth (SPEC F126.1/.4 · PLAN VQ-h, T274)
//
// BDD specification — xUnit, real production entry point. The seam audit's finding: POST
// /api/tts/preview deliberately bypassed TtsSegmentSource and rendered WITHOUT pronunciation
// rules — an audition surface that lied about the thing it auditions, and the exact surface the
// IPA-UX ruling leans on. Entry-point discipline: every scenario below drives the real route
// through WebApplicationFactory<Program> (the Story186/Story254 idiom) with only the outbound
// Kokoro HTTP transport faked (Story297's "transport swap, per typed client — never the whole
// factory" idiom, one layer deeper than Story186's own "fake the whole ITtsSynthesizer" shape):
// NormalizingTtsSynthesizer, FallbackTtsSynthesizer, KokoroTtsSynthesizer, and
// PronunciationRuleHitReporter all stay real, so a fact here proves the actual production render,
// not merely that the controller called SOME synthesizer with SOME context.
//
// AC3 — the fitness law (no production call site invokes the context-less SynthesizeAsync overload
// outside the normalizer/fallback relays) — deliberately lives in GenWave.Architecture.Tests as
// T277, not in this file: one home per law, beside the F105 laws it joins.
//
// PRECONDITION (T142 review, rounds 1-2; PLAN T274): T142's "previews excluded" (F97.5/AC6) held
// ONLY because the preview path carried Rules = [] by construction — the reporter itself was
// (and remains) unconditional in the sense that it always executes; what changed is that a
// non-empty Rules list can now reach it from a preview. ScenarioAuditionsAreExcludedFromRuleHitObservability
// below is the fact that must go RED the moment rules are wired onto the preview's context without
// an explicit exclusion, and GREEN once TtsRenderContext.IsAudition + PronunciationRuleHitReporter's
// isAudition gate are both in place — see PronunciationRuleHitReporter's own remarks for the
// mechanism and the F126.5 ruling on what "auditions log at Information" actually names.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using GenWave.Tts;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

// ── In-process fakes ─────────────────────────────────────────────────────────────────────────────
// Mirrors Story186_CorrectionsObservability's and Story254_PronunciationRulesSurface's own fakes
// (file-scoped there too, redefined here rather than shared — established convention for this suite).

file sealed class AuditionsConfigurationProvider : ConfigurationProvider
{
    public void SetAndReload(string key, string value)
    {
        Set(key, value);
        OnReload();
    }
}

file sealed class AuditionsConfigurationSource(AuditionsConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

/// <summary><see cref="IStationSettingsStore"/> test double standing in for a live Postgres
/// <c>station.settings</c> table — drives a REAL live reload of the app's own
/// <see cref="IConfiguration"/>, exactly like Story186/Story254's own settings-store doubles, so a
/// <c>PUT /api/settings</c> write is visible to the very next preview with no process restart.</summary>
file sealed class AuditionsSettingsStore : IStationSettingsStore
{
    readonly AuditionsConfigurationProvider provider = new();

    public AuditionsSettingsStore(IConfiguration configuration)
    {
        ((IConfigurationBuilder)configuration).Add(new AuditionsConfigurationSource(provider));
    }

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not on the station settings allowlist.", nameof(key));

        provider.SetAndReload(key, value?.ToString() ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Answers a fixed active persona card (or none) on every call — the narrowest double for
/// <see cref="ActivePersonaPronunciationRulesCache"/>/<see cref="ActivePersonaPaceCache"/> to
/// resolve through, mirrors Story254's own <c>FakePersonaAccessor</c>.</summary>
file sealed class FakePersonaAccessor(PersonaCard? card) : IActivePersonaAccessor
{
    public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    public Task<PersonaCard?> ResolveCardAsync(CancellationToken ct) => Task.FromResult(card);
}

/// <summary>Captures every Information+ log entry — level, category, and message together — mirrors
/// Story186_CorrectionsObservability's <c>CapturingDebugLoggerProvider</c>, floored at Information
/// (not Debug) since this suite's own facts are all negative ("no hit line"), never pinning a level
/// regression the way Story186's own positive fact does.</summary>
file sealed class CapturingInformationLoggerProvider : ILoggerProvider
{
    readonly List<(LogLevel Level, string Message)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries { get { lock (entries) return entries.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    void Add(LogLevel level, string message) { lock (entries) entries.Add((level, message)); }

    sealed class Logger(CapturingInformationLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add(logLevel, $"[{category}] {formatter(state, exception)}");
        }
    }
}

/// <summary>
/// Boots the real host with the fakes above swapped in. <b>Transport swap, per typed client — never
/// the whole factory</b> (Story297_ContextTickerWire's own F1 fix, applied here one layer deeper):
/// <see cref="kokoroHandler"/> replaces ONLY <see cref="KokoroTtsSynthesizer"/>'s outbound HTTP
/// transport via <c>services.AddHttpClient&lt;KokoroTtsSynthesizer&gt;().ConfigurePrimaryHttpMessageHandler</c>
/// — every other seam on the render path (<see cref="NormalizingTtsSynthesizer"/>,
/// <see cref="FallbackTtsSynthesizer"/>, <see cref="PronunciationRuleHitReporter"/>,
/// <see cref="PronunciationRuleProvider"/>, <see cref="ActivePersonaPronunciationRulesCache"/>,
/// <see cref="ActivePersonaPaceCache"/>) stays real DI, so a fact here proves the ACTUAL production
/// render pipeline, not a stand-in for it.
/// </summary>
file sealed class AuditionsWebFactory(
    FakeHttpMessageHandler kokoroHandler, PersonaCard? activeCard = null,
    CapturingInformationLoggerProvider? logs = null, bool adminEnabled = true)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-aud1t";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        // Both flags are read at request time (IOptionsMonitor), so UseSetting is early enough —
        // mirrors Story166_AdminKillSwitch's own KillSwitchWebFactory.
        builder.UseSetting("Admin:Enabled", adminEnabled ? "true" : "false");

        if (logs is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(sp =>
                new AuditionsSettingsStore(sp.GetRequiredService<IConfiguration>()));

            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakePersonaAccessor(activeCard));

            services.AddHttpClient<KokoroTtsSynthesizer>().ConfigurePrimaryHttpMessageHandler(() => kokoroHandler);
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureAuditionsTellTheTruth
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = AuditionsWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    /// <summary>
    /// A Kokoro double that answers every request with a well-formed (if content-free) 200 — the
    /// shape <see cref="KokoroTtsSynthesizer"/> needs to write a file and return — and records each
    /// request's raw JSON body as it arrives, in <see cref="Bodies"/>. Reading the body HERE, inside
    /// the responder, rather than later off <see cref="FakeHttpMessageHandler.Requests"/>, is
    /// deliberate: <see cref="KokoroTtsSynthesizer"/> disposes its <c>StringContent</c> in a
    /// <c>using</c> block immediately after <c>PostAsync</c> returns, so a caller that tries to
    /// re-read <c>Requests[i].Content</c> after the fact hits
    /// <see cref="ObjectDisposedException"/> — mirrors GenWave.Tts.Tests'
    /// Story253_RulesTheOperatorCanTrust's own <c>requests.Add(... ReadAsStringAsync ...)</c> idiom,
    /// captured inside the same callback for the identical reason.
    /// </summary>
    static (FakeHttpMessageHandler Handler, List<string> Bodies) KokoroOkHandler()
    {
        var bodies = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        });
        return (handler, bodies);
    }

    static async Task SeedStationRuleAsync(HttpClient client, string pattern, string word, string ipa) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "Tts:Pronunciations", value = $$"""[{"pattern":"{{pattern}}","word":"{{word}}","ipa":"{{ipa}}"}]""" },
        })).StatusCode);

    /// <summary>Parses the JSON body of the <paramref name="index"/>-th captured Kokoro request and
    /// returns its <c>input</c> field — the exact markup-annotated text
    /// <see cref="KokoroSpeechMarkup"/> composed (SPEC F96.2's <c>[word](/ipa/)</c> wire shape).</summary>
    static string RequestInput(IReadOnlyList<string> bodies, int index = 0)
    {
        using var doc = JsonDocument.Parse(bodies[index]);
        var input = doc.RootElement.GetProperty("input").GetString();
        Assert.NotNull(input);
        return input;
    }

    /// <summary>The <c>speed</c> field of the <paramref name="index"/>-th captured Kokoro request —
    /// the OpenAI-compatible speaking-rate field (SPEC F98.1-F98.2).</summary>
    static double RequestSpeed(IReadOnlyList<string> bodies, int index = 0)
    {
        using var doc = JsonDocument.Parse(bodies[index]);
        return doc.RootElement.GetProperty("speed").GetDouble();
    }

    static PersonaCard CardWithPronunciation(string pattern, string word, string ipa, double pace = 1.0) =>
        new(
            PersonaCard.CurrentSchemaVersion, "Test Persona", "", "", [],
            new VoiceSpec("kokoro", "af_heart", pace, "en"), EnergyDisposition: 0, [], [],
            Pronunciations: [new ContextPronunciationRule(pattern, word, ipa)]);

    // ── PRECONDITION (T142 review; PLAN T274) ────────────────────────────────────────────────────

    public static class ScenarioAuditionsAreExcludedFromRuleHitObservability
    {
        [Fact]
        public static async Task A_saved_rule_matching_the_preview_text_increments_no_counter()
        {
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);
            await SeedStationRuleAsync(client, "MacLeod", "MacLeod", "macleodIpa");

            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart" });

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Empty(factory.Services.GetRequiredService<PronunciationRuleHitStats>().Snapshot());
        }

        [Fact]
        public static async Task A_saved_rule_matching_the_preview_text_emits_no_hit_line()
        {
            var (handler, _) = KokoroOkHandler();
            var logs = new CapturingInformationLoggerProvider();
            await using var factory = new AuditionsWebFactory(handler, logs: logs);
            var client = await LoggedInClientAsync(factory);
            await SeedStationRuleAsync(client, "MacLeod", "MacLeod", "macleodIpa");

            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart" });

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.DoesNotContain(
                logs.Entries, e => e.Message.Contains("Pronunciation rule fired", StringComparison.Ordinal));
        }
    }

    // ── HAPPY PATH (real route, WebApplicationFactory) ──────────────────────────────────────────

    public static class ScenarioThePreviewRendersThroughTheResolvedRules
    {
        [Fact]
        public static async Task A_saved_rule_matching_the_preview_text_reaches_the_engine_request()
        {
            // Given a saved pronunciation rule matching the preview text
            var (handler, bodies) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);
            await SeedStationRuleAsync(client, "MacLeod", "MacLeod", "macleodIpa");

            // When POST /api/tts/preview renders through the real route
            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart" });

            // Then the fake engine's captured request carries the rule's IPA markup — the context
            // overload, not the context-less bypass.
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Contains("[MacLeod](/macleodIpa/)", RequestInput(bodies));
        }

        [Fact]
        public static async Task The_merge_is_the_same_station_union_persona_resolution_the_air_chain_uses()
        {
            // A station rule AND a persona-card rule for two DISTINCT words in the same text —
            // proving the union, not merely that one source works.
            var card = CardWithPronunciation("Nova", "Nova", "novaIpa");
            var (handler, bodies) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler, activeCard: card);
            var client = await LoggedInClientAsync(factory);
            await SeedStationRuleAsync(client, "Zenith", "Zenith", "zenithIpa");

            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Zenith meets Nova tonight.", voice = "af_heart" });

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var input = RequestInput(bodies);
            Assert.True(input.Contains("[Zenith](/zenithIpa/)") && input.Contains("[Nova](/novaIpa/)"));
        }
    }

    public static class ScenarioThePreviewSoundsLikeAir
    {
        [Fact]
        public static async Task The_preview_carries_the_active_personas_resolved_pace()
        {
            // T140's review noted every preview rendered at the engine default (1.0) — T274 rules
            // that, with rules now carried, carrying the persona's real pace too is the consistent
            // posture: the audition should sound like air.
            var card = CardWithPronunciation("Nova", "Nova", "novaIpa", pace: 0.85);
            var (handler, bodies) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler, activeCard: card);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Hello there.", voice = "af_heart" });

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Equal(0.85, RequestSpeed(bodies));
        }
    }

    public static class ScenarioACandidateRuleLayersOverTheMerge
    {
        [Fact]
        public static async Task An_unsaved_candidate_rule_applies_on_top_of_the_resolved_merge()
        {
            // Given a preview request carrying an unsaved candidate rule
            var (handler, bodies) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);

            // When the render runs
            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new[] { new { pattern = "MacLeod", word = "MacLeod", ipa = "candidateIpa" } },
            });

            // Then the candidate applies on top of the resolved merge — the editor auditions the
            // exact rule being authored, before saving.
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Contains("[MacLeod](/candidateIpa/)", RequestInput(bodies));
        }

        [Fact]
        public static async Task A_candidate_shadowing_a_saved_rule_wins_for_this_render_only()
        {
            var (handler, bodies) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);
            await SeedStationRuleAsync(client, "MacLeod", "MacLeod", "stationIpa");

            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new[] { new { pattern = "MacLeod", word = "MacLeod", ipa = "candidateIpa" } },
            });

            // Layering means the candidate pre-empts the same (pattern, word) identity — the saved
            // station ipa never reaches the engine for this one render.
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var input = RequestInput(bodies);
            Assert.True(input.Contains("/candidateIpa/") && !input.Contains("/stationIpa/"));
        }

        [Fact]
        public static async Task A_candidate_shadowing_the_active_personas_rule_wins_for_this_render_only()
        {
            // T274 round-2 review finding R3: only the station-collision case was pinned above —
            // a candidate must ALSO pre-empt a PERSONA (card) rule sharing its identity, not
            // merely a station one, since the candidate layers OVER the resolved station∪persona
            // merge as a whole (STORY-323 AC2), not over the station half alone.
            var card = CardWithPronunciation("MacLeod", "MacLeod", "personaIpa");
            var (handler, bodies) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler, activeCard: card);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new[] { new { pattern = "MacLeod", word = "MacLeod", ipa = "candidateIpa" } },
            });

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var input = RequestInput(bodies);
            Assert.True(input.Contains("/candidateIpa/") && !input.Contains("/personaIpa/"));
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────────────────────────────

    public static class ScenarioAMalformedCandidateFailsTheRequestNotTheStation
    {
        [Fact]
        public static async Task A_blank_pattern_400s_naming_the_field()
        {
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new[] { new { pattern = "", word = (string?)null, ipa = "x" } },
            });

            Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
            var problem = await preview.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.NotNull(problem);
            Assert.Contains(problem.Errors.Keys, key => key.Contains("pattern", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public static async Task No_render_runs_for_a_rejected_candidate()
        {
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new[] { new { pattern = "", word = (string?)null, ipa = "x" } },
            });

            Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
            Assert.Empty(handler.Requests);
        }

        // A literal `[null]` — System.Text.Json happily binds a null element into
        // IReadOnlyList<TtsPreviewCandidateRule> regardless of its non-nullable element type (NRT
        // is compile-time only); dereferencing it unguarded is an unhandled 500 (T274 review
        // finding F1). Both a single null and a null mixed among valid entries must 400 naming the
        // offending element's own index, never crash the request or silently drop the element.

        [Fact]
        public static async Task A_null_candidate_element_400s_naming_its_index()
        {
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new object?[] { null },
            });

            Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
            var problem = await preview.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.NotNull(problem);
            Assert.Contains(problem.Errors.Keys, key => key == "candidateRules[0]");
        }

        [Fact]
        public static async Task A_null_candidate_among_valid_candidates_400s_naming_its_own_index()
        {
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new object?[]
                {
                    new { pattern = "MacLeod", word = "MacLeod", ipa = "candidateIpa" },
                    null,
                },
            });

            Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
            var problem = await preview.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.NotNull(problem);
            Assert.Contains(problem.Errors.Keys, key => key == "candidateRules[1]");
        }

        [Fact]
        public static async Task No_render_runs_for_a_null_candidate()
        {
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync("/api/tts/preview", new
            {
                text = "Say MacLeod now.",
                voice = "af_heart",
                candidateRules = new object?[] { null },
            });

            Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
            Assert.Empty(handler.Requests);
        }
    }

    public static class ScenarioThePreviewStaysOwnerOnly
    {
        [Fact]
        public static async Task An_unauthenticated_caller_gets_the_existing_admin_surface_answer()
        {
            // No new exposure: the same policy posture the route already had.
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public static async Task A_spectator_scoped_deployment_gets_a_404_not_a_401()
        {
            // The admin plane does not exist for a spectator-facing/demo deployment
            // (Admin:Enabled=false, SPEC F61.1-F61.3) — SurfaceGateMiddleware 404s before auth ever
            // runs, the Story166_AdminKillSwitch posture. That story's own sweep already covers
            // every /api/* route generically; this is this endpoint's OWN pin (T274 review finding
            // F8) so a future refactor that accidentally exempts just this one route is still
            // caught locally, not only by the blanket sweep.
            var (handler, _) = KokoroOkHandler();
            await using var factory = new AuditionsWebFactory(handler, adminEnabled: false);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // ── OBSERVABILITY (SPEC F126.5, T274 review finding F2) ─────────────────────────────────────────

    public static class ScenarioTheAuditionEventItselfIsLogged
    {
        [Fact]
        public static async Task A_preview_request_logs_one_audition_event_line_at_information()
        {
            var (handler, _) = KokoroOkHandler();
            var logs = new CapturingInformationLoggerProvider();
            await using var factory = new AuditionsWebFactory(handler, logs: logs);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart" });

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Contains(
                logs.Entries,
                e => e.Level == LogLevel.Information
                    && e.Message.Contains("TTS audition requested", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task The_audition_event_line_never_carries_the_preview_text()
        {
            // F2's own "no rule text, no operator text" requirement — a concrete leak-prevention
            // pin, not just a claim in a doc comment.
            var (handler, _) = KokoroOkHandler();
            var logs = new CapturingInformationLoggerProvider();
            await using var factory = new AuditionsWebFactory(handler, logs: logs);
            var client = await LoggedInClientAsync(factory);

            await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart" });

            Assert.DoesNotContain(
                logs.Entries, e => e.Message.Contains("Say MacLeod now.", StringComparison.Ordinal));
        }

        [Fact]
        public static async Task A_crlf_bearing_voice_cannot_forge_a_second_log_entry()
        {
            // CodeQL cs/log-forging (T274 round-2 review finding R1): Voice is wire-controlled —
            // a raw embedded CRLF must not split this line into two, one of them forged. Scoped to
            // the "TTS audition requested" message specifically (not just any Information entry) —
            // an unscoped "some entry has no newline" check would pass vacuously off an unrelated
            // framework log line even with LogSanitize.Strip removed entirely (mirrors Story186's
            // own log-forging fact).
            var (handler, _) = KokoroOkHandler();
            var logs = new CapturingInformationLoggerProvider();
            await using var factory = new AuditionsWebFactory(handler, logs: logs);
            var client = await LoggedInClientAsync(factory);

            var preview = await client.PostAsJsonAsync(
                "/api/tts/preview", new { text = "Say MacLeod now.", voice = "af_heart\r\nFORGED line" });

            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Contains(
                logs.Entries,
                e => e.Level == LogLevel.Information
                    && e.Message.Contains("TTS audition requested", StringComparison.Ordinal)
                    && !e.Message.Contains('\n'));
        }
    }
}
