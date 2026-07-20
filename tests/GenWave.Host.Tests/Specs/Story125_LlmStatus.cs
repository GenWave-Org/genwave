// STORY-125 — LLM health visible on the dashboard (WIRE, api half)
//
// BDD specification — xUnit. GET /api/status gains the llm aggregate from config + the
// in-memory last-attempt holder + the active-persona accessor. NO active health-polling: an idle
// station sends the LLM zero requests. ScenarioStatusAggregate constructs StatusController
// directly with fakes (mirrors Story084's own idiom — no live stack required).
// ScenarioNoActiveHealthPolling drives the REAL production DI graph through
// WebApplicationFactory<Program> (mirrors Story084's StatusApiWebFactory) with the real
// LlmCopyWriter/LlmOptions wired exactly as Program.cs registers them and Llm:Endpoint genuinely
// enabled — the strongest honest form available at this level: not "the controller's constructor
// happens to omit IHttpClientFactory" (true, but a static fact a future refactor could silently
// break) but a runtime count of IHttpClientFactory.CreateClient calls across N real polls, proven
// zero. The tile's UI half is jest (dashboard-llm-tile.spec.tsx).
//
// See docs/PLAN.md Epic T.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────

/// <summary>No dependency has ever been probed — every lookup returns null, same as a fresh boot.</summary>
file sealed class FakeDependencyHealth : IDependencyHealth
{
    public DependencyHealthVerdict? GetVerdict(string dependencyName) => null;
}

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read.</summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// <see cref="IHttpClientFactory"/> double that counts every <see cref="CreateClient"/> call — the
/// no-polling proof's instrument (ScenarioNoActiveHealthPolling): registered in place of the real
/// factory so a poll that ever dialed the LLM would show up here, with nothing else in the request
/// path (hosted services removed, IMediaCatalog/IActivePersonaAccessor faked) that could confound
/// the count.
/// </summary>
file sealed class CountingHttpClientFactory : IHttpClientFactory
{
    int createClientCallCount;

    public int CreateClientCallCount => createClientCallCount;

    public HttpClient CreateClient(string name)
    {
        Interlocked.Increment(ref createClientCallCount);
        return new HttpClient();
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the no-polling proof: brings up the REAL
/// production DI graph (Program.cs's own <c>LlmCopyWriter</c>/<c>LlmOptions</c> registrations,
/// untouched) with a genuinely enabled <c>Llm:Endpoint</c> — set via environment variables rather
/// than <c>ConfigureAppConfiguration</c> because <c>LlmOptions</c>' <c>ValidateOnStart()</c> and
/// every other config read in Program.cs run against whatever the configuration root already holds
/// by the time <c>WebApplicationFactory</c>'s own hooks are visible (mirrors Story084's
/// <c>StatusApiWebFactory</c>: env vars are process-global and already loaded into the
/// configuration provider by build time, a <c>ConfigureWebHost</c>-registered override is not).
/// <see cref="IMediaCatalog"/>/<see cref="IActivePersonaAccessor"/> are faked purely so this test
/// never needs a live Postgres — <see cref="IHttpClientFactory"/> is the ONE seam this scenario
/// actually cares about and is left real everywhere except itself.
/// </summary>
file sealed class LlmStatusWebFactory : WebApplicationFactory<Program>
{
    const string LibraryConnVar = "ConnectionStrings__Library";
    const string AdminPasswordVar = "Admin__Password";
    const string LlmEndpointVar = "Llm__Endpoint";
    const string LlmModelVar = "Llm__Model";
    internal const string Password = "test-password-x7z";

    internal CountingHttpClientFactory HttpClientFactory { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections, and no orchestrator
            // tick that could otherwise render a segment (and legitimately dial the LLM) during
            // this test.
            services.RemoveAll<IHostedService>();

            // Replace IMediaCatalog with the controllable fake (the real MediaRepository requires
            // a live Postgres and must not be resolved during this test) — mirrors Story084's
            // StatusApiWebFactory.
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));

            // Replace IActivePersonaAccessor for the same reason: the real implementation's
            // IPersonaStore constructor dependency would otherwise force a Postgres data source to
            // build against Station's (unset here) connection string.
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());

            // The instrument: swap the real IHttpClientFactory for the counting stub. LlmCopyWriter
            // itself stays the real Program.cs registration — only its HTTP seam is replaced.
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(HttpClientFactory);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevLibrary  = Environment.GetEnvironmentVariable(LibraryConnVar);
        var prevAdmin    = Environment.GetEnvironmentVariable(AdminPasswordVar);
        var prevEndpoint = Environment.GetEnvironmentVariable(LlmEndpointVar);
        var prevModel    = Environment.GetEnvironmentVariable(LlmModelVar);
        Environment.SetEnvironmentVariable(LibraryConnVar, "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable(AdminPasswordVar, Password);
        // Genuinely enabled (SPEC F34.2) — the negative this scenario proves is meaningful only if
        // the writer is actually wired to dial out, not merely disabled by an empty endpoint.
        Environment.SetEnvironmentVariable(LlmEndpointVar, "https://llm.invalid/v1");
        Environment.SetEnvironmentVariable(LlmModelVar, "gpt-4o-mini");
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LibraryConnVar, prevLibrary);
            Environment.SetEnvironmentVariable(AdminPasswordVar, prevAdmin);
            Environment.SetEnvironmentVariable(LlmEndpointVar, prevEndpoint);
            Environment.SetEnvironmentVariable(LlmModelVar, prevModel);
        }
    }
}

// ── Shared helpers ──────────────────────────────────────────────────────────

public static class FeatureLlmStatus
{
    static StationOptions BuildStationOptions() => new()
    {
        Id    = "test-station",
        Name  = "Test Station",
        Voice = "en-us",
        Scope = new StationScopeOptions { LibraryIds = [1L] },
        SafeScope = new StationScopeOptions { LibraryIds = [1L] },
    };

    static StatusController BuildController(
        LlmOptions? llmOptions = null,
        LlmCopyStatusHolder? statusHolder = null,
        Persona? activePersona = null)
    {
        var resolvedLlmOptions = llmOptions ?? new LlmOptions();
        var resolvedStatusHolder = statusHolder ?? new LlmCopyStatusHolder();
        var llmOptionsMonitor = new FakeOptionsMonitor<LlmOptions>(resolvedLlmOptions);

        // STORY-188's degradation aggregate rides the same StatusController.Get response — this
        // suite's own scenarios only assert the pre-existing llm.* fields, so a controller with no
        // real dependency probes/failures (Normal, unpinned) is enough to keep those unaffected.
        var degradationController = new DegradationController(
            new FakeDependencyHealth(),
            resolvedStatusHolder,
            llmOptionsMonitor,
            new FakeOptionsMonitor<DegradationOptions>(new DegradationOptions()),
            TimeProvider.System,
            NullLogger<DegradationController>.Instance);

        return new(
            new FakeMediaCatalog(ready: null),
            new FakeOptionsMonitor<StationOptions>(BuildStationOptions()),
            llmOptionsMonitor,
            resolvedStatusHolder,
            degradationController,
            new FakeActivePersonaAccessor { Persona = activePersona },
            new ProcessStartTime(new DateTimeOffset(2026, 7, 11, 9, 30, 0, TimeSpan.Zero)))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    /// <summary>Serializes an <see cref="OkObjectResult"/>'s value and parses it back as JSON — the
    /// same shape the wire would carry, without spinning up a full HTTP pipeline.</summary>
    static JsonElement AsJson(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value)).RootElement;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the aggregate tells the truth
    // ---------------------------------------------------------------------

    public sealed class ScenarioStatusAggregate
    {
        [Fact]
        public async Task DisabledConfigReportsEnabledFalse()
        {
            // Llm:Endpoint "" → llm.enabled false, model/outcome null-ish (F34.8, AC1).
            var controller = BuildController(llmOptions: new LlmOptions { Endpoint = "", Model = "gpt-4o-mini" });

            var result = await controller.Get(CancellationToken.None);

            var llm = AsJson(result).GetProperty("llm");
            Assert.False(llm.GetProperty("enabled").GetBoolean());
            Assert.Equal(JsonValueKind.Null, llm.GetProperty("model").ValueKind);
            Assert.Equal(JsonValueKind.Null, llm.GetProperty("activePersona").ValueKind);
            Assert.Equal(JsonValueKind.Null, llm.GetProperty("lastOutcome").ValueKind);
            Assert.Equal(JsonValueKind.Null, llm.GetProperty("lastAttemptAt").ValueKind);
        }

        [Fact]
        public async Task EnabledConfigReportsModelAndActivePersona()
        {
            // llm.model == Llm:Model; llm.activePersona == accessor's name or null (F34.8, AC1).
            var now = DateTime.UtcNow;
            var persona = new Persona(3, "Neon Nightowl", "Spins vinyl til dawn.", "moody, late-night", "af_sky", now, now);
            var controller = BuildController(
                llmOptions: new LlmOptions { Endpoint = "https://llm.example/v1", Model = "gpt-4o-mini" },
                activePersona: persona);

            var result = await controller.Get(CancellationToken.None);

            var llm = AsJson(result).GetProperty("llm");
            Assert.True(llm.GetProperty("enabled").GetBoolean());
            Assert.Equal("gpt-4o-mini", llm.GetProperty("model").GetString());
            Assert.Equal("Neon Nightowl", llm.GetProperty("activePersona").GetString());
        }

        [Fact]
        public async Task LastOutcomeAndTimestampComeFromTheStatusHolder()
        {
            // ok/failed + lastAttemptAt round-trip through the endpoint (F34.8, AC1).
            var holder = new LlmCopyStatusHolder();
            var attemptedAt = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero);
            holder.Record(LlmAttemptOutcome.Failed, attemptedAt);
            var controller = BuildController(
                llmOptions: new LlmOptions { Endpoint = "https://llm.example/v1", Model = "gpt-4o-mini" },
                statusHolder: holder);

            var result = await controller.Get(CancellationToken.None);

            var llm = AsJson(result).GetProperty("llm");
            Assert.Equal("failed", llm.GetProperty("lastOutcome").GetString());
            Assert.Equal(attemptedAt, llm.GetProperty("lastAttemptAt").GetDateTimeOffset());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — status must never generate LLM traffic
    // ---------------------------------------------------------------------

    // LlmStatusWebFactory.CreateHost mutates the Admin__Password / ConnectionStrings__Library /
    // Llm__Endpoint / Llm__Model process env vars for the boot window — shared with every other
    // env-var-mutating factory in this test project, so this class opts into the serializing
    // collection (see EnvVarMutatingWebFactoryCollection) rather than racing them under xUnit's
    // default parallelism.
    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioNoActiveHealthPolling
    {
        [Fact]
        public async Task RepeatedStatusPollsSendTheLlmZeroRequests()
        {
            // Mock request log stays empty across N polls with no renders (F34.8, AC3) — proven
            // here as zero IHttpClientFactory.CreateClient calls against the real production DI
            // graph, with Llm:Endpoint genuinely enabled.
            await using var factory = new LlmStatusWebFactory();
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = LlmStatusWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            for (var i = 0; i < 5; i++)
            {
                var response = await client.GetAsync("/api/status");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Assert.Equal(0, factory.HttpClientFactory.CreateClientCallCount);
        }
    }
}
