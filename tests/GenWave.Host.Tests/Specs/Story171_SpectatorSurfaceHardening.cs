// STORY-171 — Cacheable, rate-limited, provably read-only spectator surface
//
// BDD specification — xUnit (SPEC F62.3, F62.9–F62.12). Public Cache-Control + OutputCache so
// spikes hit cache not the playout host; per-IP rate limit; the Spectator policy is GET-only by
// enumeration; the disclosure contract is asserted as ABSENCE; admin is untouched by the flag.
// Red until PLAN T13/T14.

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Playout;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class SpectatorHardeningWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Station:PublicStreamUrl", "https://demo.example/stream");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null,
                statusCounts: new CatalogStatusCounts(5, 3, 2, 1, 4)));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureSpectatorSurfaceHardening
{
    static void PublishOnAirTrack(IServiceProvider services)
    {
        services.GetRequiredService<NowPlayingService>().Update("1",
            new NowPlayingSnapshot("42", "Night Drive", "The Waveforms", -2.5,
                new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero), 214000, IsDrain: false));
        services.GetRequiredService<PlayHistoryService>().Push(
            new PlayHistoryEntry("1", "42", "Night Drive", "The Waveforms", -2.5,
                new DateTimeOffset(2026, 7, 18, 11, 56, 0, TimeSpan.Zero), null, 214000));
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioPublicCacheHeaders
    {
        [Theory]
        [InlineData("/spectator/api/now-playing", 5)]
        [InlineData("/spectator/api/play-history", 30)]
        [InlineData("/spectator/api/stats", 30)]
        [InlineData("/spectator/api/about", 300)]
        public async Task ResponseIsPubliclyCacheableWithTheSpecTtl(string route, int maxAgeSeconds)
        {
            await using var factory = new SpectatorHardeningWebFactory();
            PublishOnAirTrack(factory.Services);
            var client = factory.CreateClient();

            var response = await client.GetAsync(route);

            var cache = response.Headers.CacheControl;
            Assert.True(
                cache is { Public: true, MaxAge: not null } && (int)cache.MaxAge.Value.TotalSeconds == maxAgeSeconds,
                $"{route} Cache-Control was '{cache}' — expected public, max-age={maxAgeSeconds}.");
        }
    }

    public sealed class ScenarioOutputCacheAbsorbsRepeats
    {
        [Fact]
        public async Task SecondRequestWithinTtlServesTheCachedProjection()
        {
            await using var factory = new SpectatorHardeningWebFactory();
            PublishOnAirTrack(factory.Services);
            var client = factory.CreateClient();

            var first = await client.GetStringAsync("/spectator/api/now-playing");

            // Mutate the underlying store inside the 5s TTL — a cached surface must not see it.
            factory.Services.GetRequiredService<NowPlayingService>().Update("1",
                new NowPlayingSnapshot("43", "Different Track", "Other Artist", 0,
                    new DateTimeOffset(2026, 7, 18, 12, 1, 0, TimeSpan.Zero), 90000, IsDrain: false));

            var second = await client.GetStringAsync("/spectator/api/now-playing");

            Assert.Equal(first, second);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioRateLimit
    {
        [Fact]
        public async Task ExceedingTheBudgetReturns429()
        {
            await using var factory = new SpectatorHardeningWebFactory();
            PublishOnAirTrack(factory.Services);
            var client = factory.CreateClient();

            HttpStatusCode last = default;
            for (var i = 0; i < 121; i++)
                last = (await client.GetAsync("/spectator/api/now-playing")).StatusCode;

            Assert.Equal(HttpStatusCode.TooManyRequests, last);
        }
    }

    public sealed class ScenarioSpectatorPolicyIsGetOnly
    {
        [Fact]
        public async Task SpectatorEndpointsExist()
        {
            await using var factory = new SpectatorHardeningWebFactory();
            _ = factory.CreateClient();

            var spectatorEndpoints = SpectatorEndpoints(factory.Services);

            Assert.NotEmpty(spectatorEndpoints);
        }

        [Fact]
        public async Task EverySpectatorEndpointAcceptsOnlyGet()
        {
            await using var factory = new SpectatorHardeningWebFactory();
            _ = factory.CreateClient();

            var spectatorEndpoints = SpectatorEndpoints(factory.Services);

            Assert.All(spectatorEndpoints, endpoint =>
            {
                // POST /spectator/api/requests (SPEC F87, STORY-224, PLAN T87) is the one
                // deliberate exception to F62.3's GET-only invariant, written into the plan as
                // "this codebase's FIRST public anonymous WRITE endpoint" — it still carries the
                // Spectator policy (F60.2 demands no cookie) but is gated by its own kill switch
                // (RequestsSurfaceAttribute) and dedicated cooldown+daily-cap limiter instead of by
                // being read-only. Its own contract lives in Story224_RequestIntake.cs.
                var isRequestsIntake = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()
                    is { ControllerName: "SpectatorRequests" };
                if (isRequestsIntake)
                    return;

                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
                Assert.True(methods.All(m => m is "GET" or "HEAD"),
                    $"'{endpoint.DisplayName}' accepts [{string.Join(",", methods)}] under the Spectator policy (F62.3).");
            });
        }

        static IReadOnlyList<Endpoint> SpectatorEndpoints(IServiceProvider services) =>
            services.GetRequiredService<EndpointDataSource>().Endpoints
                .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Any(a => string.Equals(a.Policy, "Spectator", StringComparison.Ordinal)))
                .ToList();
    }

    public sealed class ScenarioDisclosureContract
    {
        // Asserted as ABSENCE over the serialized payloads: no media ids, file paths/locators,
        // gain values, settings, persona internals, or scope-revealing counts (F62.9).
        static readonly string[] ForbiddenFragments =
            ["mediaId", "locator", "path", "gainDb", "lufs", "persona", "settings", "playable", "unavailable"];

        [Theory]
        [InlineData("/spectator/api/now-playing")]
        [InlineData("/spectator/api/play-history")]
        [InlineData("/spectator/api/stats")]
        [InlineData("/spectator/api/about")]
        public async Task PayloadContainsNoExcludedField(string route)
        {
            await using var factory = new SpectatorHardeningWebFactory();
            PublishOnAirTrack(factory.Services);
            var client = factory.CreateClient();

            var body = await client.GetStringAsync(route);

            Assert.All(ForbiddenFragments, fragment =>
                Assert.DoesNotContain($"\"{fragment}\"", body, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class ScenarioAdminUnaffectedBySpectatorMode
    {
        [Fact]
        public async Task AdminEndpointStillRequiresAuthWithSpectatorOn()
        {
            await using var factory = new SpectatorHardeningWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
