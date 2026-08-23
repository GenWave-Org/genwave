// PLAN T340 carry-forward, built PLAN T344 — the per-IP door limiter on the announcements family
// (SPEC F145.3/.4, RateLimiterPolicies.Announcements).
//
// BDD specification — xUnit. WIRED — drives the real production middleware pipeline
// (Program.cs's UseRateLimiter(), before UseAuthentication()) through WebApplicationFactory<Program>,
// against a FakeAnnounceTokenStore double (no live Postgres). Two source IPs are simulated via a
// test-only IStartupFilter (Connection.RemoteIpAddress is null under TestServer by default — see
// RateLimiterPolicies' own remarks on the NoRemoteIpPartitionKey fallback) so this suite can prove the
// per-IP partitioning genuinely isolates one flooded caller from another, not merely that a shared
// fallback partition throttles (Story165_AdminLoginRateLimiting.cs's own posture one policy over).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Auth;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementDoorRateLimit
{
    public sealed class ScenarioAFloodedIpNeverThrottlesAnother
    {
        [Fact]
        public async Task AJunkBearerFloodFromOneIpFourTwentyNinesAtTheMiddlewareWhileASecondIpStillWorks()
        {
            // Given the real host, with two callers simulated as DISTINCT source IPs — neither carries
            // a valid credential, so every request that gets PAST the per-IP door would otherwise 401
            // at authentication, never touching the announcements action itself
            await using var factory = new AnnouncementDoorRateLimitWebFactory();
            var floodedIpClient = factory.CreateClient();
            floodedIpClient.DefaultRequestHeaders.Add(RemoteIpStartupFilter.TestIpHeaderName, "10.10.10.1");
            floodedIpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "junk");

            var secondIpClient = factory.CreateClient();
            secondIpClient.DefaultRequestHeaders.Add(RemoteIpStartupFilter.TestIpHeaderName, "10.10.10.2");
            secondIpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "also-junk");

            // When the first IP sends 60 requests (the door's own permit ceiling) then a 61st...
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 61; i++)
                statuses.Add((await floodedIpClient.GetAsync("/api/announcements/now-playing")).StatusCode);

            // ...and the second IP sends its own first request afterward
            var secondIpResponse = await secondIpClient.GetAsync("/api/announcements/now-playing");

            // Then the first 60 from the flooded IP never hit the door limiter (each refused 401 by
            // real auth — no token configured), the 61st is 429 AT THE MIDDLEWARE, and the second IP —
            // an entirely separate partition — still reaches real auth (its own 401), never 429
            Assert.True(
                statuses.Take(60).All(s => s == HttpStatusCode.Unauthorized) && statuses[60] == HttpStatusCode.TooManyRequests,
                $"expected sixty 401s then a 429; got: {string.Join(",", statuses)}");
            Assert.Equal(HttpStatusCode.Unauthorized, secondIpResponse.StatusCode);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stamps <see cref="HttpContext.Connection"/>'s <see cref="System.Net.IPAddress"/> from a test-only
/// request header — <c>TestServer</c> opens no real sockets, so <c>Connection.RemoteIpAddress</c> is
/// null by default (<c>RateLimiterPolicies</c>'s own remarks), collapsing every simulated caller into
/// one shared fallback partition unless something sets it explicitly. Mirrors
/// <c>SimulatedPortStartupFilter</c>'s (tests/GenWave.Host.Tests/Fakes) own "run before the production
/// pipeline" shape, the SAME "T339 review named the IStartupFilter shape" lever this file's own header
/// remarks name — scoped here rather than promoted to <c>Fakes/</c> since no other spec file needs a
/// simulated remote IP yet.
/// </summary>
file sealed class RemoteIpStartupFilter : IStartupFilter
{
    public const string TestIpHeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(TestIpHeaderName, out var value)
                && IPAddress.TryParse(value.ToString(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Fact — mirrors
/// Story360_AnnounceToken.cs's own <c>AnnounceTokenApiWebFactory</c> idiom (a fresh
/// <see cref="FakeAnnounceTokenStore"/>, no configured hash — every Bearer value refuses), widened
/// with <see cref="RemoteIpStartupFilter"/> so two simulated source IPs partition independently.
/// </summary>
file sealed class AnnouncementDoorRateLimitWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story344-announcement-door";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IAnnouncementStore>();
            services.AddSingleton<IAnnouncementStore>(new FakeAnnouncementStore());

            services.RemoveAll<IAnnounceTokenStore>();
            services.AddSingleton<IAnnounceTokenStore>(new FakeAnnounceTokenStore());

            services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter());
        });
    }
}
