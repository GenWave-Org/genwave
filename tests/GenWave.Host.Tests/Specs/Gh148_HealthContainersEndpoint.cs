// gh-#148 — GET /api/health/containers: AdminOnly pin + never-500 degrade, end to end.
//
// BDD specification — xUnit, Gh008 factory/login idiom. DockerStats:BaseUrl points at a
// loopback port nothing listens on (immediate connection refused — no DNS, no timeout wait), so
// the logged-in scenario proves the full production pipeline serves a well-formed degraded
// envelope rather than an error when the sidecar is missing.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

file sealed class HealthContainersWebFactory() : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-gh148";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        // A loopback port with no listener: the typed client fails fast with connection refused.
        builder.UseSetting("DockerStats:BaseUrl", "http://127.0.0.1:1");
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

public static class FeatureHealthContainersEndpoint
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = HealthContainersWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public sealed class ScenarioPolicyPins
    {
        [Fact]
        public async Task TheEndpointCarriesAdminOnlyAndTheAdminSurfaceMarker()
        {
            // Given the production route table
            await using var factory = new HealthContainersWebFactory();
            _ = factory.CreateClient();

            var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Single(candidate => (candidate as RouteEndpoint)?.RoutePattern.RawText == "api/health/containers");

            // Then it is pinned to the AdminOnly policy and gated by the admin-plane kill switch
            var authorize = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Equal(AuthorizationPolicies.AdminOnly, authorize.Policy);
            Assert.NotNull(endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>());
        }

        [Fact]
        public async Task AnUnauthenticatedCallIsUnauthorized()
        {
            // Given no login cookie
            await using var factory = new HealthContainersWebFactory();
            var client = factory.CreateClient();

            // When the endpoint is called bare
            var response = await client.GetAsync("/api/health/containers");

            // Then the cookie scheme rejects it outright
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    public sealed class ScenarioMissingSidecarDegradesNever500s
    {
        [Fact]
        public async Task ALoggedInCallReturnsAWellFormedDegradedEnvelope()
        {
            // Given a logged-in admin and no sidecar listening at DockerStats:BaseUrl
            await using var factory = new HealthContainersWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When the endpoint is called
            var response = await client.GetAsync("/api/health/containers");

            // Then it is a 200 carrying degraded: true, a reason, and an empty container list
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("degraded").GetBoolean());
            Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("reason").GetString()));
            Assert.Equal(0, body.RootElement.GetProperty("containers").GetArrayLength());
        }
    }
}
