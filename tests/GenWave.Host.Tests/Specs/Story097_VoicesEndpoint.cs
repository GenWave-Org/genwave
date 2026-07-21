// STORY-097 — Voices listing endpoint (WIRE) (Epic R / SPEC F29.4, gitea-#183)
//
// BDD specification — xUnit. GET /api/voices (cookie-auth) proxies Kokoro's voices listing over
// the core network with a ~5 min in-memory cache; 502 ProblemDetails when Kokoro is unreachable
// and the cache is cold/expired; 401 without a cookie when Admin:Password is set. R2 confirmed the
// actual kokoro-fastapi voices path (GET /v1/audio/voices, response {"voices": [...]}) against the
// running container before coding KokoroVoiceLister — these specs pin behavior, not the upstream
// path. The live listing proof is R13's gate job.
//
// ScenarioVoicesProxied.ResponseIsOkWithTheUpstreamVoiceIds and ScenarioShortCache /
// ScenarioKokoroUnreachable construct VoicesController directly against a real
// KokoroVoiceLister/CachedVoiceLister pointed at KokoroVoicesStubServer — a tiny Kestrel-backed
// fake standing in for Kokoro (mirrors Story004/KokoroStubServer's idiom), so the JSON-shape
// translation is exercised for real without a live Kokoro container.
// ScenarioVoicesProxied.RealRequestThroughTheProductionPipelineReturnsVoices and
// ScenarioDenyByDefault drive the real HTTP pipeline via WebApplicationFactory<Program> (mirrors
// Story084's ScenarioDeployedEndpoint / StatusApiWebFactory) so routing, cookie auth, and the
// deny-by-default fallback policy are all exercised for real.

using System.Net;
using System.Net.Http.Json;
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
using GenWave.Host.Api;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> that returns <see cref="CurrentValue"/> on every read
/// (mirrors Story120's/Story123's file-scoped precedent). <c>KokoroVoiceLister</c>/
/// <c>CachedVoiceLister</c> read <c>TtsOptions.Endpoint</c> through this per call
/// (SPEC F36.1–F36.4) instead of a boot-frozen <c>HttpClient.BaseAddress</c>.
/// </summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

// ── WebApplicationFactory for the deployed-pipeline scenarios ──────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for the deployed-pipeline scenarios. Sets
/// <c>Admin:Password</c> so the deny-by-default fallback policy is active, points
/// <c>Tts:Endpoint</c> at a <see cref="KokoroVoicesStubServer"/> instead of a real Kokoro, and
/// removes hosted services that would attempt real Liquidsoap/DB connections — mirrors Story084's
/// <c>StatusApiWebFactory</c> exactly. All three settings are injected per-instance via
/// <c>ConfigureWebHost</c>'s <c>UseSetting</c> (colon-form keys), which reaches Program.cs's
/// composition-time reads (verified empirically) — no process environment variable is mutated, so
/// no other test class can race with it.
/// </summary>
file sealed class VoicesApiWebFactory(Uri kokoroBaseUri) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-voices-r2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope so ValidateOnStart()
        // is satisfied without injecting them manually.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Tts:Endpoint", kokoroBaseUri.ToString());

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();
        });
    }
}

// ── Shared helpers ──────────────────────────────────────────────────────────

public static class FeatureVoicesEndpoint
{
    static VoicesController BuildController(ITtsVoiceLister voiceLister) =>
        new(voiceLister, NullLogger<VoicesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    /// <summary>
    /// Wires a real <see cref="KokoroVoiceLister"/>/<see cref="CachedVoiceLister"/> pointed at
    /// <paramref name="endpoint"/> via <see cref="FakeOptionsMonitor{T}"/> — no
    /// <see cref="HttpClient.BaseAddress"/> (SPEC F36.1–F36.4), mirroring Program.cs's own wiring.
    /// Live-repoint behavior (a PUT mid-test changing the endpoint) is Story124's job, in
    /// GenWave.Tts.Tests — every use here fixes the endpoint for the scenario's lifetime.
    /// </summary>
    static CachedVoiceLister BuildLister(Uri endpoint)
    {
        var optionsMonitor = new FakeOptionsMonitor<TtsOptions>(new TtsOptions { Endpoint = endpoint.ToString() });
        var http = new HttpClient();
        return new CachedVoiceLister(new KokoroVoiceLister(http, optionsMonitor), optionsMonitor, TimeSpan.FromMinutes(5));
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioVoicesProxied
    {
        [Fact]
        public async Task ResponseIsOkWithTheUpstreamVoiceIds()
        {
            await using var stub = await KokoroVoicesStubServer.StartAsync(["af_heart", "af_bella"]);
            var lister = BuildLister(stub.BaseUri);
            var controller = BuildController(lister);

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var voices = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);
            Assert.Equal(new[] { "af_heart", "af_bella" }, voices);
        }

        [Fact]
        public async Task RealRequestThroughTheProductionPipelineReturnsVoices()
        {
            // Drives GET /api/voices through the production HTTP pipeline (routing, cookie auth,
            // the deny-by-default fallback policy) with a valid cookie obtained via a real
            // POST /api/auth/login round trip, against a fake Kokoro over the wire.
            await using var stub = await KokoroVoicesStubServer.StartAsync(["af_heart", "af_bella", "am_adam"]);
            await using var factory = new VoicesApiWebFactory(stub.BaseUri);
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync(
                "/api/auth/login", new { password = VoicesApiWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var response = await client.GetAsync("/api/voices");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var voices = await response.Content.ReadFromJsonAsync<string[]>();
            Assert.Equal(new[] { "af_heart", "af_bella", "am_adam" }, voices);
        }
    }

    public sealed class ScenarioShortCache
    {
        [Fact]
        public async Task SecondRequestInsideTtlDoesNotHitKokoro()
        {
            await using var stub = await KokoroVoicesStubServer.StartAsync(["af_heart"]);
            var lister = BuildLister(stub.BaseUri);
            var controller = BuildController(lister);

            await controller.Get(CancellationToken.None);
            await controller.Get(CancellationToken.None);

            Assert.Equal(1, stub.CallCount);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioKokoroUnreachable
    {
        [Fact]
        public async Task ReturnsBadGatewayProblemDetails()
        {
            // Start a stub, capture its (still-valid) BaseUri, then dispose it — the address now
            // refuses connections, exactly like Kokoro stopped, with cache empty/expired (AC3).
            var stub = await KokoroVoicesStubServer.StartAsync(["af_heart"]);
            var baseUri = stub.BaseUri;
            await stub.DisposeAsync();

            var lister = BuildLister(baseUri);
            var controller = BuildController(lister);

            var result = await controller.Get(CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
            Assert.IsType<ProblemDetails>(problem.Value);
        }
    }

    public sealed class ScenarioDenyByDefault
    {
        [Fact]
        public async Task MissingCookieIsUnauthorized()
        {
            await using var stub = await KokoroVoicesStubServer.StartAsync(["af_heart"]);
            await using var factory = new VoicesApiWebFactory(stub.BaseUri);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/voices");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
