// STORY-263 — Canonical font route (SPEC F102, PLAN T173)
//
// BDD specification — xUnit. GET /fonts/{file} is the one canonical, api-served home for the
// vendored .woff2 faces both surfaces' @font-face declarations point at. It serves each known
// vendored face with the right content type, rejects everything else (including traversal
// attempts) with a bare 404, and — the reason T173 exists at all — must stay reachable to BOTH
// surfaces regardless of Admin:Enabled/Station:SpectatorMode, since the route carries neither
// surface's gating attribute. See FontEndpoints and SurfaceGateMiddleware's own remarks.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

file sealed class FontRouteWebFactory(bool spectatorMode = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", spectatorMode ? "true" : "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
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

public static class FeatureCanonicalFontRoute
{
    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioEachVendoredFaceServes
    {
        [Theory]
        [InlineData("fraunces-variable-latin.woff2")]
        [InlineData("fraunces-italic-variable-latin.woff2")]
        [InlineData("source-sans-3-variable-latin.woff2")]
        [InlineData("jetbrains-mono-variable-latin.woff2")]
        [InlineData("grenze-gotisch-variable-latin.woff2")]
        public async Task TheFaceIsServedWithTheWoff2ContentType(string file)
        {
            await using var factory = new FontRouteWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/fonts/{file}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("font/woff2", response.Content.Headers.ContentType?.MediaType);
        }

        [Theory]
        [InlineData("fraunces-variable-latin.woff2")]
        [InlineData("fraunces-italic-variable-latin.woff2")]
        [InlineData("source-sans-3-variable-latin.woff2")]
        [InlineData("jetbrains-mono-variable-latin.woff2")]
        [InlineData("grenze-gotisch-variable-latin.woff2")]
        public async Task TheFaceBodyIsNonEmpty(string file)
        {
            await using var factory = new FontRouteWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync($"/fonts/{file}");
            var bytes = await response.Content.ReadAsByteArrayAsync();

            Assert.True(bytes.Length > 0, $"{file} served an empty body.");
        }

        [Fact]
        public async Task TheResponseIsCacheableLongAndImmutable()
        {
            await using var factory = new FontRouteWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/fonts/fraunces-variable-latin.woff2");

            var cacheControl = response.Headers.CacheControl;
            Assert.NotNull(cacheControl);
            Assert.True(cacheControl.Public);
            Assert.Contains(cacheControl.Extensions, e => e.Name == "immutable");
        }

        /// <summary>The route carries no SpectatorSurfaceAttribute, so SpectatorSecurityHeadersMiddleware's
        /// full header set (CSP/X-Frame-Options/Referrer-Policy) never applies here — but nosniff is
        /// stamped directly by FontEndpoints itself (see its own remarks), so it must still show up
        /// on the wire.</summary>
        [Fact]
        public async Task TheResponseCarriesNosniff()
        {
            await using var factory = new FontRouteWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/fonts/fraunces-variable-latin.woff2");

            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var values));
            Assert.Equal("nosniff", Assert.Single(values));
        }
    }

    public sealed class ScenarioReachableRegardlessOfEitherSurfaceToggle
    {
        /// <summary>The whole reason T173 exists: Station:SpectatorMode defaults FALSE
        /// (appsettings.json), so a route gated by SpectatorSurfaceAttribute would 404 admin's own
        /// fonts out of the box. The font route carries neither surface's gating attribute — it
        /// must serve identically whether or not spectator mode is on.</summary>
        [Fact]
        public async Task ServesWithSpectatorModeOff()
        {
            await using var factory = new FontRouteWebFactory(spectatorMode: false);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/fonts/fraunces-variable-latin.woff2");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ServesWithSpectatorModeOn()
        {
            await using var factory = new FontRouteWebFactory(spectatorMode: true);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/fonts/fraunces-variable-latin.woff2");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ServesWithNoCredentials()
        {
            // Admin's login page itself needs this route before any session exists.
            await using var factory = new FontRouteWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/fonts/fraunces-variable-latin.woff2");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnythingUnknownIs404
    {
        [Theory]
        // Traversal attempts — the switch matches literals only, so these never reach the
        // filesystem at all, but the observable contract is still a bare 404.
        [InlineData("..%2f..%2f..%2fetc%2fpasswd")]
        [InlineData("..%5c..%5cappsettings.json")]
        [InlineData("%2e%2e%2fappsettings.json")]
        // A real file elsewhere in wwwroot — proves the switch does not fall through to disk.
        // (A bare ".." is NOT included: HttpClient/Uri normalizes it away client-side per RFC 3986
        // before any request is sent, so it never reaches the server as a literal segment at all —
        // it is not a meaningful case to assert on here.)
        [InlineData("appsettings.json")]
        // Wrong case — the whole point of T156's FontSrcPattern is lowercase-only.
        [InlineData("Fraunces-Variable-latin.woff2")]
        // An unvendored name.
        [InlineData("comic-sans.woff2")]
        [InlineData("fraunces-variable-latin.woff2.evil")]
        public async Task UnknownOrHostileSegmentsReturn404(string segment)
        {
            await using var factory = new FontRouteWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync($"/fonts/{segment}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
