// STORY-196 — LLM call inspector (WIRE)
//
// BDD specification — xUnit (SPEC F73.1-F73.3). Implements PLAN T41's three pending facts.
//
// AC1 drives the REAL production pipeline exactly like Story186_CorrectionsObservability's own
// factory idiom (WebApplicationFactory<Program>, only the two external-service edges this
// non-Integration suite cannot reach faked out — here, none of PersonaController's Postgres-backed
// dependencies are even touched: a draft-fields preview never calls IPersonaStore/IAdminMediaLookup,
// and each is Lazy<NpgsqlDataSource>-backed so merely resolving them via DI opens no connection, see
// PersonaServiceCollectionExtensions' own remarks) — POST /api/personas/preview against a real
// Kestrel-backed completions stub (mirrors GenWave.Tts.Tests' MockCompletionsServer; redefined here
// rather than cross-referencing that test project, same as Story186's own file-scoped doubles), then
// GET /api/llm-calls and prove the ring shows exactly what the render produced.
//
// AC2 mirrors Story172_PublicListenerIsolation's own idiom for "both listeners": the internal
// listener (no session -> 401, the same deny-by-default every other admin route gets) and the public
// listener (SimulatedPortStartupFilter stamping Connection.LocalPort, since TestServer opens no real
// socket -> 404 from SurfaceGateMiddleware, before auth ever runs).
//
// AC3 proves "never persisted" two ways: LlmCallRing's only constructor dependency is
// IOptionsMonitor<LlmOptions> (no store/repository/connection type in sight) and it resolves to the
// SAME instance twice from one host (singleton); and a brand-new WebApplicationFactory — a fresh DI
// container standing in for a process restart, the strongest "restart clears" proof available at
// this level — reads an empty ring even though the first host's ring held an entry.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Host.Api;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process stub / fakes ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal Kestrel-backed stub for an OpenAI-compatible <c>POST /v1/chat/completions</c> endpoint —
/// mirrors <c>GenWave.Tts.Tests.MockCompletionsServer</c> (STORY-119), redefined here since
/// Host.Tests has no project reference to that test project (same "redefine, don't cross-reference"
/// convention Story186_CorrectionsObservability's own header note explains). Every request always
/// serves 200 with <see cref="ReplyContent"/> — this spec has no need for the fuller
/// Serve/Fail/Delay repertoire the Tts.Tests original carries.
/// </summary>
sealed class LlmCompletionsStub : IAsyncDisposable
{
    readonly WebApplication app;

    public string ReplyContent { get; set; } = "Great tune coming up, stay tuned.";
    public Uri BaseUri { get; }

    LlmCompletionsStub(WebApplication app, Uri baseUri)
    {
        this.app = app;
        BaseUri = baseUri;
    }

    public static async Task<LlmCompletionsStub> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        LlmCompletionsStub? stubRef = null;

        app.MapPost("/v1/chat/completions", async (HttpContext ctx) =>
        {
            var stub = stubRef;
            if (stub is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ctx.Response.WriteAsJsonAsync(
                new { choices = new[] { new { message = new { content = stub.ReplyContent } } } },
                ctx.RequestAborted);
        });

        await app.StartAsync();
        var stub = new LlmCompletionsStub(app, new Uri(app.Urls.First()));
        stubRef = stub;
        return stub;
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

/// <summary>Stamps every request's Connection.LocalPort so SurfaceGateMiddleware sees the public
/// listener — mirrors Story172_PublicListenerIsolation's own <c>SimulatedPortStartupFilter</c>
/// (file-scoped there too, redefined here rather than shared).</summary>
file sealed class SimulatedPortStartupFilter(int port) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.LocalPort = port;
            return nextMiddleware(context);
        });
        next(app);
    };
}

// ── WebApplicationFactories ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real host with a real <c>Llm:Endpoint</c> (a genuine <see cref="LlmCompletionsStub"/>)
/// so <c>LlmCopyWriter</c>/<c>LlmCallRing</c>/<c>DegradationController</c> are the exact production
/// singletons <c>AddGenWaveTts</c> wires — nothing about the LLM pipeline is faked. Only hosted
/// services are removed (no Liquidsoap/Postgres background work during this test); every
/// Postgres-backed controller dependency <c>PersonaController</c> needs is left as its REAL,
/// Lazy-backed registration (see the file header) since a draft-fields preview never forces any of
/// them to actually connect.
/// </summary>
file sealed class LlmCallInspectorWebFactory(string llmEndpoint) : WebApplicationFactory<Program>
{
    const string LibraryConnVar = "ConnectionStrings__Library";
    const string AdminPasswordVar = "Admin__Password";
    const string LlmEndpointVar = "Llm__Endpoint";
    const string LlmModelVar = "Llm__Model";
    internal const string Password = "test-password-x9k3";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevLibrary = Environment.GetEnvironmentVariable(LibraryConnVar);
        var prevAdmin = Environment.GetEnvironmentVariable(AdminPasswordVar);
        var prevEndpoint = Environment.GetEnvironmentVariable(LlmEndpointVar);
        var prevModel = Environment.GetEnvironmentVariable(LlmModelVar);
        Environment.SetEnvironmentVariable(LibraryConnVar, "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable(AdminPasswordVar, Password);
        Environment.SetEnvironmentVariable(LlmEndpointVar, llmEndpoint);
        Environment.SetEnvironmentVariable(LlmModelVar, "test-model");
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

/// <summary>
/// Boots the real host with no LLM configured at all (irrelevant to AC2 — nothing here ever calls
/// it) and, optionally, a simulated public-listener port — mirrors Story172's
/// <c>PublicListenerWebFactory</c>.
/// </summary>
file sealed class LlmCallInspectorSurfaceWebFactory(int? simulatedPublicPort) : WebApplicationFactory<Program>
{
    internal const int PublicPort = 8081;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Spectator:PublicPort", PublicPort.ToString());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            if (simulatedPublicPort is int port)
                services.AddSingleton<IStartupFilter>(new SimulatedPortStartupFilter(port));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevLibrary = Environment.GetEnvironmentVariable("ConnectionStrings__Library");
        var prevAdmin = Environment.GetEnvironmentVariable("Admin__Password");
        Environment.SetEnvironmentVariable("ConnectionStrings__Library", "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable("Admin__Password", "test-password-x7z");
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Library", prevLibrary);
            Environment.SetEnvironmentVariable("Admin__Password", prevAdmin);
        }
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Wire shape of one row from <c>GET /api/llm-calls</c> — mirrors
/// <see cref="GenWave.Host.Api.LlmCallDto"/> without depending on it directly.</summary>
file sealed record LlmCallRow(
    long Seq, DateTimeOffset StartedAt, long ElapsedMs, string Status, string? StatusDetail, string Mode,
    string? PromptSystem, string? PromptUser, string? Response, int PromptChars, int ResponseChars);

public static class FeatureLlmCallInspector
{
    static async Task LoginAsync(HttpClient client, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    static object DraftPreviewBody() => new
    {
        kind = "LeadIn",
        name = "Neon Nightowl",
        backstory = "Spins vinyl til dawn.",
        style = "moody, late-night",
    };

    // ── HAPPY PATH — ring contents through the production pipeline (F73.1, AC1) ────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioRingContentsThroughTheProductionPipeline : IAsyncLifetime
    {
        LlmCompletionsStub stub = null!;

        public async Task InitializeAsync() => stub = await LlmCompletionsStub.StartAsync();

        public async Task DisposeAsync() => await stub.DisposeAsync();

        [Fact]
        public async Task A_real_preview_render_is_readable_back_via_the_inspector_endpoint()
        {
            // Given a real persona preview render against a real (stub) completions endpoint...
            stub.ReplyContent = "Spinning up something great, stick around.";
            await using var factory = new LlmCallInspectorWebFactory(stub.BaseUri.ToString());
            var client = factory.CreateClient();
            await LoginAsync(client, LlmCallInspectorWebFactory.Password);

            // When the preview endpoint is driven — the exact production hand-off
            // (IPersonaPreviewWriter -> the real LlmCopyWriter -> RequestCompletionAsync) every
            // operator preview shares (SPEC F35.6)...
            var preview = await client.PostAsJsonAsync("/api/personas/preview", DraftPreviewBody());
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

            // Then the inspector endpoint shows exactly one entry, carrying prompt/response/timing/
            // status/mode (SPEC F73.1) — read back as an admin, capped at ring size, newest first.
            var rows = await client.GetFromJsonAsync<List<LlmCallRow>>("/api/llm-calls");
            Assert.NotNull(rows);
            var row = Assert.Single(rows!);

            Assert.True(
                row.Status == "ok" &&
                row.Mode == "normal" &&
                row.Response == stub.ReplyContent &&
                row.ElapsedMs >= 0 &&
                row.PromptSystem != null && row.PromptSystem.Contains("moody, late-night") &&
                row.PromptChars == (row.PromptSystem!.Length + (row.PromptUser?.Length ?? 0)) &&
                row.ResponseChars == stub.ReplyContent.Length);
        }
    }

    // ── SAD PATH — admin-only, never public, on both listeners (F73.2, AC2) ─────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioAdminOnlyOnBothListeners
    {
        [Fact]
        public async Task No_credentials_on_the_internal_listener_is_rejected()
        {
            await using var factory = new LlmCallInspectorSurfaceWebFactory(simulatedPublicPort: null);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/llm-calls");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task The_public_listener_never_reaches_it_either()
        {
            await using var factory = new LlmCallInspectorSurfaceWebFactory(
                simulatedPublicPort: LlmCallInspectorSurfaceWebFactory.PublicPort);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // SurfaceGateMiddleware runs before authentication (SPEC F64.1/F64.2) — the public
            // listener 404s this route with no session at all, same as every other non-spectator route.
            var response = await client.GetAsync("/api/llm-calls");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task The_route_carries_AdminOnly_and_no_spectator_marker()
        {
            // Structural proof, not just a runtime probe (mirrors Story195_BoothLog's own
            // SadPathPublicSurface): this endpoint is classified as admin, never spectator, by
            // construction — it cannot become reachable on the public/spectator surface by accident.
            await using var factory = new LlmCallInspectorSurfaceWebFactory(simulatedPublicPort: null);

            var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Single(e => (e as RouteEndpoint)?.RoutePattern.RawText
                    ?.Equals("api/llm-calls", StringComparison.OrdinalIgnoreCase) == true);

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            Assert.Contains(AuthorizationPolicies.AdminOnly, policies);
            Assert.DoesNotContain(AuthorizationPolicies.Spectator, policies);
            Assert.Null(endpoint.Metadata.GetMetadata<SpectatorSurfaceAttribute>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>());
        }
    }

    // ── Never persisted: singleton, no persistence dependency, restart clears (F73.3, AC3) ─────────

    public sealed class ScenarioNeverPersisted
    {
        [Fact]
        public void The_ring_is_registered_as_a_singleton_with_no_persistence_dependency()
        {
            // The type's own shape proves it (not just a runtime probe): its ONLY constructor
            // dependency is IOptionsMonitor<LlmOptions> — no store/repository/connection type in
            // sight, so it structurally cannot persist anything (SPEC F73.3).
            var parameters = typeof(LlmCallRing).GetConstructors().Single().GetParameters();
            var soleParam = Assert.Single(parameters);
            Assert.Equal(typeof(IOptionsMonitor<LlmOptions>), soleParam.ParameterType);
        }

        [Fact]
        public async Task Resolving_it_twice_from_one_host_returns_the_same_instance()
        {
            await using var factory = new LlmCallInspectorSurfaceWebFactory(simulatedPublicPort: null);

            var first = factory.Services.GetRequiredService<LlmCallRing>();
            var second = factory.Services.GetRequiredService<LlmCallRing>();

            Assert.Same(first, second);
        }

        [Collection(EnvVarMutatingWebFactoryCollection.Name)]
        public sealed class ScenarioRestartClears
        {
            [Fact]
            public async Task A_new_host_instance_never_sees_the_previous_ones_entries()
            {
                await using var stub = await LlmCompletionsStub.StartAsync();

                // Given a ring entry recorded on a first host instance...
                await using (var factory1 = new LlmCallInspectorWebFactory(stub.BaseUri.ToString()))
                {
                    var client1 = factory1.CreateClient();
                    await LoginAsync(client1, LlmCallInspectorWebFactory.Password);
                    var preview = await client1.PostAsJsonAsync("/api/personas/preview", DraftPreviewBody());
                    Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

                    var rows1 = await client1.GetFromJsonAsync<List<LlmCallRow>>("/api/llm-calls");
                    Assert.Single(rows1!);
                }

                // When a brand-new host instance stands up — a fresh DI container, standing in for a
                // process restart (nothing about LlmCallRing could carry state across this boundary;
                // see the no-persistence-dependency fact above)...
                await using var factory2 = new LlmCallInspectorWebFactory(stub.BaseUri.ToString());
                var client2 = factory2.CreateClient();
                await LoginAsync(client2, LlmCallInspectorWebFactory.Password);

                // Then its ring is empty (SPEC F73.3) — restart clears it, by construction.
                var rows2 = await client2.GetFromJsonAsync<List<LlmCallRow>>("/api/llm-calls");
                Assert.Empty(rows2!);
            }
        }
    }
}
