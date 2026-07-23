// gh-#8 (plugin-readiness P1.2) — the admin plane's single AdminOnly split into granular names:
// Operator / Curation / Settings / PlayoutRead, with AdminOnly retained for session management.
//
// BDD specification — xUnit. The split is a SEAM, not a behavior change: all five admin-plane
// policies carry the SAME AdminOnlyRequirement today (one shared admin password), but every
// controller already declares which plane it belongs to, so a future RBAC module differentiates
// by re-registering names in AuthorizationPolicies — never by touching controllers. These facts
// pin exactly that: the names resolve, they all gate identically, the route table uses only
// known names, and one logged-in session still reaches every plane (F60.6 regression posture).
//
// Factory/login idiom mirrors Story163_NamedAuthorizationPolicies.cs.

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
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class GranularPoliciesWebFactory() : WebApplicationFactory<Program>
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

public static class FeatureGranularAdminPolicies
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = GranularPoliciesWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public sealed class ScenarioTheSeamExists
    {
        [Fact]
        public async Task AllFiveAdminPlanePoliciesResolveAndGateIdentically()
        {
            // Same requirement type behind every name — the "no behavior change" half of gh-#8.
            await using var factory = new GranularPoliciesWebFactory();
            _ = factory.CreateClient();

            var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
            foreach (var name in new[] { "AdminOnly", "Operator", "Curation", "Settings", "PlayoutRead" })
            {
                var policy = await provider.GetPolicyAsync(name);
                Assert.NotNull(policy);
                var requirement = Assert.Single(policy.Requirements);
                Assert.Equal("AdminOnlyRequirement", requirement.GetType().Name);
            }
        }

        [Fact]
        public async Task TheRouteTableUsesOnlyKnownPolicyNames()
        {
            // An endpoint annotated with a policy name outside the registered set would 500 at
            // request time (unknown policy) — pin the whole table to the known vocabulary.
            await using var factory = new GranularPoliciesWebFactory();
            _ = factory.CreateClient();

            var known = new HashSet<string>(["AdminOnly", "Operator", "Curation", "Settings", "PlayoutRead", "Spectator"]);
            var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

            Assert.All(endpoints, endpoint =>
            {
                foreach (var data in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
                {
                    if (!string.IsNullOrEmpty(data.Policy))
                        Assert.True(known.Contains(data.Policy),
                            $"Endpoint '{endpoint.DisplayName}' names unregistered policy '{data.Policy}'.");
                }
            });
        }

        [Fact]
        public async Task EveryGranularPlaneIsActuallyInUse()
        {
            // The split only helps RBAC if the names are ON the routes — an empty plane means a
            // controller was re-pointed back to AdminOnly and the seam quietly collapsed.
            await using var factory = new GranularPoliciesWebFactory();
            _ = factory.CreateClient();

            var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
            var used = endpoints
                .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>())
                .Select(a => a.Policy)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet();

            foreach (var name in new[] { "Operator", "Curation", "Settings", "PlayoutRead" })
                Assert.Contains(name, used);
        }
    }

    public sealed class ScenarioOneSessionStillReachesEveryPlane
    {
        [Theory]
        [InlineData("/api/status")]          // PlayoutRead
        [InlineData("/api/ratings?ids=")]    // Curation (empty batch — no catalog dependency)
        [InlineData("/api/settings")]        // Settings
        [InlineData("/api/voices")]          // Operator (engine down in this factory → 200/502, never 401/403)
        public async Task ALoggedInAdminIsNeverForbiddenByThePlaneSplit(string route)
        {
            await using var factory = new GranularPoliciesWebFactory();
            var client = await LoggedInClientAsync(factory);

            var response = await client.GetAsync(route);

            Assert.True(
                response.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden,
                $"{route} returned {(int)response.StatusCode} — the gh-#8 split changed behavior for the shared admin session.");
        }

        [Fact]
        public async Task WithoutTheCookieEveryPlaneStillDeniesEntry()
        {
            await using var factory = new GranularPoliciesWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            foreach (var route in new[] { "/api/status", "/api/ratings?ids=", "/api/settings", "/api/voices" })
            {
                var response = await client.GetAsync(route);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
    }
}
