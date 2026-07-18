// STORY-166 — Admin kill switch: admin plane returns 404 when Admin:Enabled=false
//
// BDD specification — xUnit (SPEC F61.1–F61.3). The plane does not exist: 404, not 401 — a
// fronting-proxy misroute exposes nothing, not even a login form. The flag is env-only and
// invisible to every API. Red until PLAN T03.
// Compose `profiles: ["admin"]` (F61.4) is compose-observable only — covered by T17, not here.

using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class KillSwitchWebFactory(bool adminEnabled, bool spectatorMode) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Both flags are read at request time (IOptionsMonitor), so UseSetting is early enough.
        builder.UseSetting("Admin:Enabled", adminEnabled ? "true" : "false");
        builder.UseSetting("Station:SpectatorMode", spectatorMode ? "true" : "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevLib = Environment.GetEnvironmentVariable("ConnectionStrings__Library");
        var prevAdmin = Environment.GetEnvironmentVariable("Admin__Password");
        Environment.SetEnvironmentVariable("ConnectionStrings__Library", "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable("Admin__Password", Password);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Library", prevLib);
            Environment.SetEnvironmentVariable("Admin__Password", prevAdmin);
        }
    }
}

public static class FeatureAdminKillSwitch
{
    /// <summary>Every /api/* route with a concrete verb, `{params}` substituted with "1".</summary>
    static IReadOnlyList<(string Verb, string Path)> ApiRoutes(IServiceProvider services)
    {
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints;
        var routes = new List<(string, string)>();
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var raw = endpoint.RoutePattern.RawText;
            if (raw is null || !raw.TrimStart('/').StartsWith("api", StringComparison.OrdinalIgnoreCase))
                continue;
            var path = "/" + Regex.Replace(raw.TrimStart('/'), @"\{[^}]+\}", "1");
            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var verb in verbs)
                routes.Add((verb, path));
        }
        return routes;
    }

    static HttpRequestMessage Request(string verb, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST" or "PUT" or "PATCH")
            request.Content = JsonContent.Create(new { });
        return request;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioAdminPlaneVanishes
    {
        [Fact]
        public async Task EveryApiRouteReturns404IncludingLogin()
        {
            await using var factory = new KillSwitchWebFactory(adminEnabled: false, spectatorMode: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var routes = ApiRoutes(factory.Services);
            Assert.NotEmpty(routes);

            var results = new List<(string Verb, string Path, HttpStatusCode Status)>();
            foreach (var (verb, path) in routes)
            {
                var response = await client.SendAsync(Request(verb, path));
                results.Add((verb, path, response.StatusCode));
            }

            Assert.All(results, r =>
                Assert.True(r.Status == HttpStatusCode.NotFound,
                    $"{r.Verb} {r.Path} returned {(int)r.Status} with Admin:Enabled=false — the plane must not exist (F61.2)."));
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioNonAdminSurfacesUnaffected
    {
        // /media/{id} is excluded here: with an empty fake catalog it 404s for its own reasons,
        // which this scenario could not tell apart from a kill-switch 404.

        [Theory]
        [InlineData("/health")]
        [InlineData("/internal/engine-config")]
        [InlineData("/spectator/api/now-playing")]
        public async Task RouteStillExistsUnderTheKillSwitch(string route)
        {
            await using var factory = new KillSwitchWebFactory(adminEnabled: false, spectatorMode: true);
            var client = factory.CreateClient();

            var response = await client.GetAsync(route);

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioAllFourModesBoot
    {
        [Theory]
        [InlineData(true, false)]  // Operator
        [InlineData(true, true)]   // Standard
        [InlineData(false, true)]  // Appliance
        [InlineData(false, false)] // Headless
        public async Task StartupCompletesWithoutError(bool adminEnabled, bool spectatorMode)
        {
            await using var factory = new KillSwitchWebFactory(adminEnabled, spectatorMode);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.NotNull(response); // reaching here at all = the host built and served a request
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioFlagInvisibleToTheApi
    {
        static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
        {
            var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = KillSwitchWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            return client;
        }

        [Fact]
        public async Task SettingsListingNeverMentionsTheFlag()
        {
            await using var factory = new KillSwitchWebFactory(adminEnabled: true, spectatorMode: false);
            var client = await LoggedInClientAsync(factory);

            var body = await client.GetStringAsync("/api/settings");

            Assert.DoesNotContain("Admin:Enabled", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task WritingTheFlagIsRejected()
        {
            await using var factory = new KillSwitchWebFactory(adminEnabled: true, spectatorMode: false);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PutAsJsonAsync("/api/settings",
                new[] { new { key = "Admin:Enabled", value = "false" } });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
