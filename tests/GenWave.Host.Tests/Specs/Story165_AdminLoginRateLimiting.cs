// STORY-165 — Admin login rate limiting (5/min per source IP)
//
// BDD specification — xUnit (SPEC F61.5). The api's first brute-force guard: POST /api/auth/login
// throttles per source IP. Red until PLAN T04.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class LoginLimitWebFactory() : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureAdminLoginRateLimiting
{
    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioNormalLoginUnaffected
    {
        [Fact]
        public async Task FirstAttemptIsNotThrottled()
        {
            await using var factory = new LoginLimitWebFactory();
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = LoginLimitWebFactory.Password });

            Assert.NotEqual(HttpStatusCode.TooManyRequests, login.StatusCode);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioBurstIsThrottled
    {
        [Fact]
        public async Task SixthAttemptWithinTheMinuteIs429()
        {
            await using var factory = new LoginLimitWebFactory();
            var client = factory.CreateClient();

            for (var i = 0; i < 5; i++)
                await client.PostAsJsonAsync("/api/auth/login", new { password = "wrong" });

            var sixth = await client.PostAsJsonAsync("/api/auth/login", new { password = "wrong" });

            Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        }
    }
}
