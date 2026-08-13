// STORY-324 — The respell oracle (SPEC F126.2 · PLAN VQ-h, T278; review round 2 hardening)
//
// BDD specification — xUnit, real production entry point. espeak-ng vendored in the api image as a
// respell→IPA oracle: the operator types "muh-KLOWD", an owner-only endpoint derives candidate IPA,
// the STORY-323 audition confirms it. Argv-only invocation (no shell — the Process.Start injection
// class is structurally absent) plus the POSIX "--" end-of-options marker (argument injection,
// CWE-88, is a SEPARATE class from shell injection — review round 2 finding F1), never on a render
// path. Entry-point discipline: scenarios drive the real route through WebApplicationFactory<Program>
// with the oracle binary faked at its adapter seam (IRespellOracle) — the exception is
// ScenarioTheRealBinaryAgreesWithTheContract, which exercises EspeakRespellOracle against the
// genuine espeak-ng process and dynamically skips (with an honest reason) on a test host that
// doesn't have it on PATH, mirroring the docker-gated integration facts elsewhere in this solution
// (dotnet test's own "Category=Integration" lane).
//
// The T280 wire acceptance (derive→audition→save→next-spoken-line in a real browser) is a
// production check, deliberately not represented here.

namespace GenWave.Host.Tests.Specs;

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Playout;
using GenWave.Host.Playout;
using GenWave.Host.Pronunciations;
using GenWave.Host.Tests.Support;
using GenWave.Orchestration;

// ── File-scoped fakes/attributes ─────────────────────────────────────────────────────────────────
// Mirrors this suite's established convention (Story238/Story254 etc.): file-scoped types cannot
// cross files, so every spec file with this need defines its own copy.

/// <summary><see cref="IRespellOracle"/> test double — records what reached
/// <see cref="DeriveAsync"/> and how often, so a fact can assert on the boundary between the
/// endpoint and the adapter without a real espeak-ng process anywhere in the test process.</summary>
file sealed class FakeRespellOracle(bool available = true, string? ipa = "mˈʌklˈoʊd") : IRespellOracle
{
    public bool IsAvailable { get; } = available;
    public string? LastRespelling { get; private set; }
    public int DeriveCallCount { get; private set; }

    public Task<string?> DeriveAsync(string respelling, CancellationToken ct)
    {
        DeriveCallCount++;
        LastRespelling = respelling;
        return Task.FromResult(ipa);
    }
}

/// <summary>Captures every Information+ log entry — level and message together — mirrors
/// Story323_AuditionsTellTheTruth's own <c>CapturingInformationLoggerProvider</c> (this suite's
/// established file-scoped-copy convention). Used by <c>ScenarioNothingSensitiveIsLogged</c> to pin
/// SPEC F126.5's "log the event, not the text" (review round 2 finding F8).</summary>
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

/// <summary>Boots the real host with <see cref="IRespellOracle"/> swapped for
/// <paramref name="oracle"/> — real routing/auth/<c>PronunciationDerivationController</c>, only the
/// adapter seam faked (this file's own header comment explains why). <paramref name="logs"/>, when
/// supplied, captures every Information+ log line the request produces (review round 2 finding F8).</summary>
file sealed class RespellOracleWebFactory(IRespellOracle oracle, CapturingInformationLoggerProvider? logs = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-respell-oracle";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        if (logs is not null)
            builder.ConfigureLogging(logging => logging.AddProvider(logs));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IRespellOracle>();
            services.AddSingleton(oracle);
        });
    }
}

/// <summary>Boots the REAL production DI graph (no fakes at all, mirrors Story238's own
/// <c>PlayoutClosureWebFactory</c>) — only <see cref="IHostedService"/> is removed, so
/// <c>PlayoutSupervisor</c> never actually starts (no live Liquidsoap connection in this test
/// process); every registration <see cref="FeatureRespellOracle.ScenarioTheOracleNeverSitsOnARenderPath"/>'s
/// closure walk inspects is the one Program.cs really ships.</summary>
file sealed class RespellClosureWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-respell-closure");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

/// <summary>Probes ONCE (cached) whether the real <c>espeak-ng</c> binary is importable from PATH on
/// THIS test host — the api image's runtime stage installs it (SPEC F126.2, PLAN T278's Dockerfile
/// change), but a bare dev/CI box running <c>dotnet test</c> outside that image may not have it.
/// </summary>
file static class EspeakNgProbe
{
    public static readonly Lazy<bool> IsOnPath = new(Probe);

    static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("espeak-ng")
            {
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return false;

            return process.WaitForExit(2000);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }
}

/// <summary>Runs its target fact only when <see cref="EspeakNgProbe.IsOnPath"/> is true —
/// dynamically Skip'd with an honest reason otherwise (the docker-gated integration-fact precedent:
/// a dev/CI box without the vendored binary gets a clean, explained skip, not a red build). xUnit
/// constructs one attribute instance per decorated method at discovery time and reads
/// <see cref="Skip"/> then, so setting it in the constructor is sufficient.</summary>
file sealed class RequiresRealEspeakNgAttribute : FactAttribute
{
    public RequiresRealEspeakNgAttribute()
    {
        if (!EspeakNgProbe.IsOnPath.Value)
        {
            Skip = "espeak-ng is not on PATH on this test host — it is vendored in the api image's "
                + "runtime stage (SPEC F126.2, PLAN T278), not on a bare dev/CI box. Run inside the "
                + "built image, or apt-get/brew install espeak-ng locally, to exercise this fact.";
        }
    }
}

/// <summary>Wire shape of <c>POST /api/pronunciations/derive</c>'s success body — mirrors
/// GenWave.Host.Api.RespellDeriveResponse without depending on it directly (Story254's own
/// established idiom for this suite).</summary>
file sealed record RespellDeriveResponseBody(string Ipa);

public static class FeatureRespellOracle
{
    // Widened to the base WebApplicationFactory<Program> type: a file-local type
    // (RespellOracleWebFactory) cannot appear in a member SIGNATURE of this public type (Story254's
    // own documented compiler restriction), though it upcasts fine as an argument at each call site.
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = RespellOracleWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    // ── HAPPY PATH (real route, WebApplicationFactory) ──────────────────────

    public sealed class ScenarioARespellingDerivesCandidateIpa
    {
        [Fact]
        public async Task The_derive_endpoint_returns_candidate_ipa_for_a_respelling()
        {
            // Given espeak-ng is present (faked at the adapter seam)
            var oracle = new FakeRespellOracle(ipa: "mˈʌklˈoʊd");
            await using var factory = new RespellOracleWebFactory(oracle);
            var client = await LoggedInClientAsync(factory);

            // When the owner posts a respelling
            var response = await client.PostAsJsonAsync("/api/pronunciations/derive", new { respelling = "muh-KLOWD" });

            // Then candidate IPA returns
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<RespellDeriveResponseBody>();
            Assert.NotNull(body);
            Assert.Equal("mˈʌklˈoʊd", body!.Ipa);
            Assert.Equal("muh-KLOWD", oracle.LastRespelling);
        }

        [Fact]
        public void The_invocation_is_argv_only_with_no_shell()
        {
            // The adapter's captured invocation is a ProcessStartInfo ArgumentList — never a
            // composed shell string — with the POSIX "--" end-of-options marker inserted before
            // the respelling (review round 2 finding F1). Argv-only alone is NOT enough: espeak-ng
            // parses its own argv the way every getopt-style CLI does, so a respelling starting
            // with '-' would otherwise be another OPTION to it, not data (CWE-88) — see
            // EspeakRespellOracle's own remarks for the in-container proof this fixes. Exercises
            // BuildProcessStartInfo directly (mirrors AubioBpmAnalyzer.BuildDecodeArguments' own
            // shape): no process is ever started to prove this.
            const string metacharacterRespelling = "$(rm -rf /); echo pwned";

            var psi = EspeakRespellOracle.BuildProcessStartInfo(metacharacterRespelling);

            Assert.False(psi.UseShellExecute);
            Assert.Equal("espeak-ng", psi.FileName);
            Assert.Equal(["-q", "--ipa", "-v", "en-us", "--", metacharacterRespelling], psi.ArgumentList);
        }

        [Fact]
        public async Task The_endpoint_is_owner_only()
        {
            await using var factory = new RespellOracleWebFactory(new FakeRespellOracle());
            var client = factory.CreateClient(); // no login

            var response = await client.PostAsJsonAsync("/api/pronunciations/derive", new { respelling = "muh-KLOWD" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    public sealed class ScenarioTheOracleNeverSitsOnARenderPath
    {
        [Fact]
        public async Task No_on_air_render_reaches_the_oracle_adapter()
        {
            // The F90.8 DI-closure-walk idiom (shared helper — review round 2 finding F6: this used
            // to be a private, byte-identical copy of Story238_ShelfCannotTouchAir's own walk).
            // Boots the REAL production graph (RespellClosureWebFactory, no fakes) and walks
            // outward from the two real tick-path types — PlayoutFeederService and
            // PlayoutSupervisor — through every constructor parameter, resolved via the SAME live
            // container, recursively. Neither EspeakRespellOracle nor any GenWave.Host.Pronunciations
            // type may appear anywhere in that closure.
            await using var factory = new RespellClosureWebFactory();
            var services = factory.Services;

            var closure = PlayoutDependencyClosure.Collect(services);

            var offenders = closure
                .Where(type => type == typeof(EspeakRespellOracle)
                    || typeof(IRespellOracle).IsAssignableFrom(type)
                    || (type.Namespace ?? "").StartsWith("GenWave.Host.Pronunciations", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(offenders);

            // Sanity: prove the walk actually reached real playout dependencies, not an
            // empty/broken graph that would trivially pass the assertion above for the wrong
            // reason.
            Assert.Contains(closure, type => type == typeof(PlayoutFeeder));
            Assert.Contains(closure, type => type == typeof(Orchestrator));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnImageWithoutEspeakDegradesToHiding
    {
        [Fact]
        public async Task The_derive_endpoint_answers_501_when_the_binary_is_absent()
        {
            // The assist hides (T279's UI half keys off this); raw-IPA authoring and the
            // STORY-323 audition loop stand alone.
            var oracle = new FakeRespellOracle(available: false);
            await using var factory = new RespellOracleWebFactory(oracle);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/pronunciations/derive", new { respelling = "muh-KLOWD" });

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            // The IsAvailable pre-check short-circuits before ever calling the adapter — cached
            // absence, never a per-request probe.
            Assert.Equal(0, oracle.DeriveCallCount);
        }
    }

    public sealed class ScenarioInputIsCappedAndInert
    {
        [Fact]
        public async Task An_over_length_respelling_400s()
        {
            var oracle = new FakeRespellOracle();
            await using var factory = new RespellOracleWebFactory(oracle);
            var client = await LoggedInClientAsync(factory);

            var tooLong = new string('a', 201);
            var response = await client.PostAsJsonAsync("/api/pronunciations/derive", new { respelling = tooLong });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, oracle.DeriveCallCount);
        }

        [Fact]
        public async Task Shell_metacharacters_reach_the_adapter_as_inert_argv_data()
        {
            // "$(rm -rf /)" is a weird respelling, not a command — captured verbatim as one
            // argument.
            const string weirdRespelling = "$(rm -rf /) `whoami` && echo pwned";
            var oracle = new FakeRespellOracle(ipa: "irrelevant-to-this-fact");
            await using var factory = new RespellOracleWebFactory(oracle);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/pronunciations/derive", new { respelling = weirdRespelling });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(weirdRespelling, oracle.LastRespelling);
        }

        [Fact]
        public async Task A_leading_dash_respelling_400s_before_reaching_the_adapter()
        {
            // Defence-in-depth (review round 2 finding F1) — the ACTUAL fix is the "--" marker
            // EspeakRespellOracle.BuildProcessStartInfo inserts (see its own real-binary proof
            // below), but this endpoint refuses an option-shaped respelling outright, before the
            // oracle is ever called.
            var oracle = new FakeRespellOracle();
            await using var factory = new RespellOracleWebFactory(oracle);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync(
                "/api/pronunciations/derive", new { respelling = "-f/etc/hostname" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, oracle.DeriveCallCount);
        }
    }

    // ── OUTPUT NORMALIZATION (review round 2 finding F2) ─────────────────────

    public sealed class ScenarioOracleOutputIsNormalized
    {
        [Fact]
        public void Internal_whitespace_including_newlines_collapses_to_single_spaces()
        {
            // espeak-ng's own --ipa output is one line PER CLAUSE, not one line per call — proven
            // in-container: a comma/period in the respelling makes it print multiple lines. A bare
            // .Trim() only strips the ends; NormalizeIpa collapses every internal whitespace run,
            // including embedded newlines, to a single space, and trims the ends too.
            var normalized = EspeakRespellOracle.NormalizeIpa("  həlˈoʊ\nwˈɜːld.\n  ðɪs ɪz ə tˈɛst  \n");

            Assert.Equal("həlˈoʊ wˈɜːld. ðɪs ɪz ə tˈɛst", normalized);
            Assert.DoesNotContain('\n', normalized);
        }
    }

    // ── NOTHING SENSITIVE IS LOGGED (SPEC F126.5, review round 2 finding F8) ─

    public sealed class ScenarioNothingSensitiveIsLogged
    {
        [Fact]
        public async Task The_log_capture_contains_neither_the_respelling_nor_the_derived_ipa()
        {
            const string respelling = "muh-KLOWD-canary-9f3e";
            const string derivedIpa = "mˈʌklˈoʊd-canary-ipa-7b2c";
            var oracle = new FakeRespellOracle(ipa: derivedIpa);
            var logs = new CapturingInformationLoggerProvider();
            await using var factory = new RespellOracleWebFactory(oracle, logs);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/pronunciations/derive", new { respelling });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(respelling, StringComparison.Ordinal));
            Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(derivedIpa, StringComparison.Ordinal));
        }
    }

    // ── REAL BINARY (integration, dynamically skipped without espeak-ng on PATH) ────────────────

    public sealed class ScenarioTheRealBinaryAgreesWithTheContract
    {
        [RequiresRealEspeakNg, Trait("Category", "Integration")]
        public async Task TheRealEspeakNgBinaryDerivesIpaForARespelling()
        {
            // No fake anywhere here — the actual adapter against the actual binary, proving the
            // documented invocation shape (espeak-ng -q --ipa -v en-us -- <respelling>) really is
            // what ships in the api image (SPEC F126.2, PLAN T278's Dockerfile change).
            var oracle = new EspeakRespellOracle(NullLogger<EspeakRespellOracle>.Instance);

            var ipa = await oracle.DeriveAsync("muh-KLOWD", CancellationToken.None);

            Assert.NotNull(ipa);
            Assert.NotEmpty(ipa!);
            Assert.True(oracle.IsAvailable);
        }

        [RequiresRealEspeakNg, Trait("Category", "Integration")]
        public async Task An_option_shaped_respelling_is_spoken_as_text_never_file_contents()
        {
            // CWE-88 proof (review round 2 finding F1), against the REAL binary and a REAL file on
            // disk: WITHOUT the "--" end-of-options marker BuildProcessStartInfo inserts, a
            // respelling of "-f<path>" makes espeak-ng READ AND SPEAK that file's content instead
            // of treating the argument as literal text — proven in-container while diagnosing this
            // finding. A sentinel file's own phonemization is captured independently; if the
            // "-f<path>" form leaked the file's content, the two derivations would be identical.
            var oracle = new EspeakRespellOracle(NullLogger<EspeakRespellOracle>.Instance);
            var sentinelPath = Path.Combine(Path.GetTempPath(), $"genwave-respell-injection-{Guid.NewGuid():N}.txt");
            const string sentinelText = "unmistakable canary phrase";
            await File.WriteAllTextAsync(sentinelPath, sentinelText);
            try
            {
                var sentinelIpa = await oracle.DeriveAsync(sentinelText, CancellationToken.None);
                var injectionAttemptIpa = await oracle.DeriveAsync($"-f{sentinelPath}", CancellationToken.None);

                Assert.NotNull(sentinelIpa);
                Assert.NotNull(injectionAttemptIpa);
                // If "-f<path>" were honored as espeak-ng's own -f option, it would read the FILE's
                // content (the sentinel phrase) instead of the argument text, and the two
                // derivations would agree. They must not.
                Assert.NotEqual(sentinelIpa, injectionAttemptIpa);
            }
            finally
            {
                File.Delete(sentinelPath);
            }
        }

        [RequiresRealEspeakNg, Trait("Category", "Integration")]
        public async Task A_comma_bearing_respelling_derives_no_newline_in_the_output()
        {
            // Review round 2 finding F2, against the REAL binary: espeak-ng's own --ipa output is
            // one line PER CLAUSE — a comma/period produces multiple lines, proven in-container —
            // and EspeakRespellOracle.NormalizeIpa must collapse them before this method ever
            // returns.
            var oracle = new EspeakRespellOracle(NullLogger<EspeakRespellOracle>.Instance);

            var ipa = await oracle.DeriveAsync("hello, world. this is a test", CancellationToken.None);

            Assert.NotNull(ipa);
            Assert.DoesNotContain('\n', ipa);
            Assert.DoesNotContain('\r', ipa);
        }
    }
}
