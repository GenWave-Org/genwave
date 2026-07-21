// STORY-172 — Public listener serves only the spectator surface
//
// BDD specification — xUnit (SPEC F64.1, F64.2). The api binds a dedicated public port; on it,
// only SpectatorSurface endpoints and /health exist — admin, /media/*, /internal/* return 404
// regardless of flags, so a fronting-proxy misroute is structurally harmless. TestServer opens
// no real sockets, so these specs simulate arrival-on-the-public-port by stamping
// Connection.LocalPort through an IStartupFilter that runs before the production pipeline; the
// real dual-socket binding is exercised by PLAN T15/T18 against the compose stack.
// Red until PLAN T15.

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Playout;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>Stamps every request's Connection.LocalPort so the SurfaceGate sees the public listener.</summary>
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

file sealed class PublicListenerWebFactory(int? simulatedPort) : WebApplicationFactory<Program>
{
    internal const int PublicPort = 8081;
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Spectator:PublicPort", PublicPort.ToString());
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            if (simulatedPort is int port)
                services.AddSingleton<IStartupFilter>(new SimulatedPortStartupFilter(port));
        });
    }
}

public static class FeaturePublicListenerIsolation
{
    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioSpectatorSurfaceRespondsOnThePublicPort
    {
        [Theory]
        [InlineData("/spectator/api/now-playing")]
        [InlineData("/spectator/api/play-history")]
        [InlineData("/spectator/api/stats")]
        [InlineData("/spectator/api/about")]
        [InlineData("/health")]
        public async Task RouteExistsOnThePublicListener(string route)
        {
            await using var factory = new PublicListenerWebFactory(PublicListenerWebFactory.PublicPort);
            factory.Services.GetRequiredService<NowPlayingService>().Update("1",
                new NowPlayingSnapshot("42", "T", "A", 0,
                    new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero), 1000, IsDrain: false));
            var client = factory.CreateClient();

            var response = await client.GetAsync(route);

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioRootLandsOnThePage
    {
        [Fact]
        public async Task GetRootDeliversTheSpectatorPage()
        {
            await using var factory = new PublicListenerWebFactory(PublicListenerWebFactory.PublicPort);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/");

            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        }
    }

    public sealed class ScenarioInternalListenerUnaffected
    {
        [Fact]
        public async Task InternalRoutesStillExistOnTheInternalListener()
        {
            await using var factory = new PublicListenerWebFactory(simulatedPort: null);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/internal/engine-config");

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioEverythingElseDoesNotExistPublicly
    {
        [Theory]
        [InlineData("/api/status")]
        [InlineData("/api/auth/login")]
        [InlineData("/api/settings")]
        [InlineData("/media/1")]
        [InlineData("/media/random")]
        [InlineData("/internal/engine-config")]
        [InlineData("/internal/safe-track")]
        public async Task PrivateRouteReturns404OnThePublicListener(string route)
        {
            await using var factory = new PublicListenerWebFactory(PublicListenerWebFactory.PublicPort);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync(route);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
