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
// Kestrel-backed completions stub (Support/LlmCompletionsStub.cs — shared with
// Story353_LlmCauseTaxonomy.cs since T334 review round 1, advisory a), then GET /api/llm-calls and
// prove the ring shows exactly what the render produced.
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── WebApplicationFactories ──────────────────────────────────────────────────────────────────────
// AC1's completions stub + web factory (Support/LlmCompletionsStub.cs's own LlmCompletionsStub /
// LlmCompletionsWebFactory) are shared with Story353_LlmCauseTaxonomy.cs — see that file's own
// header comment for the extraction rationale (T334 review round 1, advisory a).

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
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Spectator:PublicPort", PublicPort.ToString());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            if (simulatedPublicPort is int port)
                services.AddSingleton<IStartupFilter>(new SimulatedPortStartupFilter(port));
        });
    }
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Wire shape of one row from <c>GET /api/llm-calls</c> — mirrors
/// <see cref="GenWave.Host.Api.LlmCallDto"/> without depending on it directly.</summary>
file sealed record LlmCallRow(
    long Seq, string? PersonaName, DateTimeOffset StartedAt, long ElapsedMs, string Status, string? StatusDetail,
    string Mode, string? PromptSystem, string? PromptUser, string? Response, int PromptChars, int ResponseChars);

/// <summary>Wire shape of <c>GET /api/llm-calls</c> itself (SPEC F139.2, PLAN T334) — mirrors
/// <see cref="GenWave.Host.Api.LlmCallsResponseDto"/> without depending on it directly, same as
/// <see cref="LlmCallRow"/> does for each entry. AC1/AC3 below only ever assert on
/// <see cref="Calls"/>; the F139.2 counter summary itself is covered by
/// Story353_LlmCauseTaxonomy.cs, not re-proven here.</summary>
file sealed record LlmCallsResponseWire(IReadOnlyList<LlmCallRow> Calls);

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
            await using var factory = new LlmCompletionsWebFactory(stub.BaseUri.ToString());
            var client = factory.CreateClient();
            await LoginAsync(client, LlmCompletionsWebFactory.Password);

            // When the preview endpoint is driven — the exact production hand-off
            // (IPersonaPreviewWriter -> the real LlmCopyWriter -> RequestCleanedCompletionAsync) every
            // operator preview shares (SPEC F35.6)...
            var preview = await client.PostAsJsonAsync("/api/personas/preview", DraftPreviewBody());
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

            // Then the inspector endpoint shows exactly one entry, carrying prompt/response/timing/
            // status/mode (SPEC F73.1) — read back as an admin, capped at ring size, newest first.
            var response = await client.GetFromJsonAsync<LlmCallsResponseWire>("/api/llm-calls");
            Assert.NotNull(response);
            var row = Assert.Single(response!.Calls);

            Assert.True(
                row.Status == "ok" &&
                row.Mode == "normal" &&
                row.Response == stub.ReplyContent &&
                row.ElapsedMs >= 0 &&
                row.PromptSystem != null && row.PromptSystem.Contains("moody, late-night") &&
                row.PromptChars == (row.PromptSystem!.Length + (row.PromptUser?.Length ?? 0)) &&
                row.ResponseChars == stub.ReplyContent.Length);

            // gh-#429: the DTO carries who authored the call — DraftPreviewBody's own "name" field,
            // exactly as PersonaController built the override Persona the writer resolved it from.
            Assert.Equal("Neon Nightowl", row.PersonaName);
        }
    }

    // ── SAD PATH — admin-only, never public, on both listeners (F73.2, AC2) ─────────────────────────

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
        public async Task The_route_carries_an_admin_plane_policy_and_no_spectator_marker()
        {
            // Structural proof, not just a runtime probe (mirrors Story195_BoothLog's own
            // SadPathPublicSurface): this endpoint is classified as admin, never spectator, by
            // construction — it cannot become reachable on the public/spectator surface by accident.
            await using var factory = new LlmCallInspectorSurfaceWebFactory(simulatedPublicPort: null);

            var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Single(e => (e as RouteEndpoint)?.RoutePattern.RawText
                    ?.Equals("api/llm-calls", StringComparison.OrdinalIgnoreCase) == true);

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            // gh-#8: the admin plane split AdminOnly into granular names — the pin is "admin-plane, never
            // spectator/anonymous", not one specific name.
            Assert.Contains(policies, p => AuthorizationPolicies.AdminPlanePolicies.Contains(p));
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

        public sealed class ScenarioRestartClears
        {
            [Fact]
            public async Task A_new_host_instance_never_sees_the_previous_ones_entries()
            {
                await using var stub = await LlmCompletionsStub.StartAsync();

                // Given a ring entry recorded on a first host instance...
                await using (var factory1 = new LlmCompletionsWebFactory(stub.BaseUri.ToString()))
                {
                    var client1 = factory1.CreateClient();
                    await LoginAsync(client1, LlmCompletionsWebFactory.Password);
                    var preview = await client1.PostAsJsonAsync("/api/personas/preview", DraftPreviewBody());
                    Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

                    var response1 = await client1.GetFromJsonAsync<LlmCallsResponseWire>("/api/llm-calls");
                    Assert.Single(response1!.Calls);
                }

                // When a brand-new host instance stands up — a fresh DI container, standing in for a
                // process restart (nothing about LlmCallRing could carry state across this boundary;
                // see the no-persistence-dependency fact above)...
                await using var factory2 = new LlmCompletionsWebFactory(stub.BaseUri.ToString());
                var client2 = factory2.CreateClient();
                await LoginAsync(client2, LlmCompletionsWebFactory.Password);

                // Then its ring is empty (SPEC F73.3) — restart clears it, by construction.
                var response2 = await client2.GetFromJsonAsync<LlmCallsResponseWire>("/api/llm-calls");
                Assert.Empty(response2!.Calls);
            }
        }
    }
}
