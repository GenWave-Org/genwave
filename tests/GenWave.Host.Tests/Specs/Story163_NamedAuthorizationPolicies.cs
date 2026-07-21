// STORY-163 — Named authorization policies (AdminOnly, Spectator) with fail-closed default
//
// BDD specification — xUnit (SPEC F60.1–F60.3, F60.6). The seam: every endpoint carries explicit
// authorization metadata (a named policy or AllowAnonymous); the fallback policy denies even an
// authenticated principal, so an unannotated endpoint is dead on arrival. Red until PLAN T01.

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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

file sealed class PoliciesWebFactory() : WebApplicationFactory<Program>
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

public static class FeatureNamedAuthorizationPolicies
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = PoliciesWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioRouteTableFullyAnnotated
    {
        [Fact]
        public async Task EveryEndpointCarriesExplicitAuthorizationMetadata()
        {
            await using var factory = new PoliciesWebFactory();
            _ = factory.CreateClient(); // force host build

            var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

            Assert.All(endpoints, endpoint =>
            {
                var hasNamedPolicy = endpoint.Metadata
                    .GetOrderedMetadata<IAuthorizeData>()
                    .Any(a => !string.IsNullOrEmpty(a.Policy));
                var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
                Assert.True(hasNamedPolicy || allowsAnonymous,
                    $"Endpoint '{endpoint.DisplayName}' has neither a named policy nor explicit AllowAnonymous.");
            });
        }
    }

    public sealed class ScenarioAdminBehaviorUnchangedWithPasswordSet
    {
        [Fact]
        public async Task LoginRoundTripStillIssuesTheCookie()
        {
            await using var factory = new PoliciesWebFactory();
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = PoliciesWebFactory.Password });

            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }

        [Fact]
        public async Task AuthenticatedAdminRequestStillSucceeds()
        {
            await using var factory = new PoliciesWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    public sealed class ScenarioAnonymousSurfacesStayAnonymous
    {
        // /media/{id} and /internal/* are Docker-network-isolated hot paths; their AllowAnonymous
        // is explicit and must survive the policy rework. 404 (unknown id) is fine — 401/403 is not.

        [Theory]
        [InlineData("/health")]
        [InlineData("/media/1")]
        [InlineData("/internal/engine-config")]
        public async Task NoCredentialRequestIsNeverUnauthorized(string route)
        {
            await using var factory = new PoliciesWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync(route);

            Assert.True(
                response.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden,
                $"{route} returned {(int)response.StatusCode} — anonymous surface now requires auth.");
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioFallbackDeniesEvenAuthenticatedPrincipals
    {
        [Fact]
        public async Task FallbackPolicyFailsForAnAuthenticatedUser()
        {
            // F60.3: the fallback is deny-ALL, not merely require-authentication — an endpoint that
            // forgot its annotation must be dead even for a logged-in admin.
            await using var factory = new PoliciesWebFactory();
            _ = factory.CreateClient();

            var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
            var fallback = await provider.GetFallbackPolicyAsync();
            Assert.NotNull(fallback);

            var authService = factory.Services.GetRequiredService<IAuthorizationService>();
            var authenticatedUser = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Cookie"));
            var result = await authService.AuthorizeAsync(authenticatedUser, resource: null, fallback!);

            Assert.False(result.Succeeded);
        }
    }

    public sealed class ScenarioAdminEndpointWithoutCookie
    {
        [Fact]
        public async Task MissingCookieIsUnauthorized()
        {
            await using var factory = new PoliciesWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/status");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
