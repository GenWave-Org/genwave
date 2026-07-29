// gh-#180 — spectator: CSP + security headers on the public surface
//
// BDD specification — xUnit. The public page shipped with no Content-Security-Policy,
// X-Frame-Options, Referrer-Policy, or X-Content-Type-Options. SpectatorSecurityHeadersMiddleware
// stamps all four on every response whose endpoint carries SpectatorSurfaceAttribute — the page,
// its assets, and /spectator/api/* — and nothing else. The CSP's img-src/media-src pins follow
// Station:PublicBaseUrl/Station:PublicStreamUrl live (per-request IOptionsMonitor read): a valid
// absolute http(s) URL contributes its scheme+host+port origin; empty, unparseable, or
// non-http(s) config fails CLOSED to 'self' alone — never an exception, never a wider policy.

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

/// <summary>Mirrors <c>SpectatorPageWebFactory</c> (Story173), plus per-scenario settings so each
/// scenario arranges its own <c>Station:PublicBaseUrl</c>/<c>Station:PublicStreamUrl</c>/
/// <c>Station:SpectatorMode</c> once, at construction.</summary>
file sealed class SecurityHeadersWebFactory(params (string Key, string Value)[] settings)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
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

public static class FeatureSpectatorSecurityHeaders
{
    /// <summary>The full default-config CSP — both dynamic pins collapsed to 'self' (empty
    /// PublicBaseUrl/PublicStreamUrl is the shipped default). Asserted by string equality on the
    /// page so any directive drift is a loud, whole-policy diff.</summary>
    const string DefaultCsp =
        "default-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; " +
        "script-src 'self'; style-src 'self'; font-src 'self'; " +
        "img-src 'self'; media-src 'self'; connect-src 'self'";

    static string Header(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioHeadersOnThePage
    {
        [Fact]
        public async Task ContentSecurityPolicyLocksTheSurfaceDown()
        {
            await using var factory = new SecurityHeadersWebFactory();

            var response = await factory.CreateClient().GetAsync("/spectator");

            Assert.Equal(DefaultCsp, Header(response, "Content-Security-Policy"));
        }

        [Fact]
        public async Task FramingIsDenied()
        {
            await using var factory = new SecurityHeadersWebFactory();

            var response = await factory.CreateClient().GetAsync("/spectator");

            Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        }

        [Fact]
        public async Task ReferrerNeverLeaksTheFullUrlCrossOrigin()
        {
            await using var factory = new SecurityHeadersWebFactory();

            var response = await factory.CreateClient().GetAsync("/spectator");

            Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        }

        [Fact]
        public async Task ContentTypeSniffingIsDenied()
        {
            await using var factory = new SecurityHeadersWebFactory();

            var response = await factory.CreateClient().GetAsync("/spectator");

            Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        }
    }

    public sealed class ScenarioHeadersOnEverySpectatorRoute
    {
        /// <summary>The whole surface: page, assets, fonts, and the API — one uniform header
        /// set (headers on API JSON are harmless and keep the surface consistent).</summary>
        public static TheoryData<string> SpectatorPaths => new(
            "/spectator",
            "/spectator/app.js",
            "/spectator/styles.css",
            "/spectator/favicon.ico",
            "/spectator/fonts/Fraunces-Variable-latin.woff2",
            "/spectator/api/now-playing",
            "/spectator/api/play-history",
            "/spectator/api/stats",
            "/spectator/api/about");

        [Theory]
        [MemberData(nameof(SpectatorPaths), MemberType = typeof(ScenarioHeadersOnEverySpectatorRoute))]
        public async Task EveryRouteCarriesTheContentSecurityPolicy(string path)
        {
            await using var factory = new SecurityHeadersWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync(path);

            Assert.Equal(DefaultCsp, Header(response, "Content-Security-Policy"));
        }
    }

    public sealed class ScenarioImgSrcFollowsPublicBaseUrl
    {
        [Fact]
        public async Task AConfiguredBaseUrlPinsItsOriginIntoImgSrc()
        {
            // Path and trailing segments are parsed away — only scheme+host+port may enter the policy.
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:PublicBaseUrl", "https://radio.example.test/some/path"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Contains("img-src 'self' https://radio.example.test;",
                Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnExplicitPortSurvivesIntoTheOrigin()
        {
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:PublicBaseUrl", "https://radio.example.test:8443"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Contains("img-src 'self' https://radio.example.test:8443;",
                Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioMediaSrcFollowsPublicStreamUrl
    {
        [Fact]
        public async Task AnAbsoluteStreamUrlPinsItsOriginIntoMediaSrc()
        {
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:PublicStreamUrl", "https://ice.example.test:8000/stream"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Contains("media-src 'self' https://ice.example.test:8000;",
                Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ARootRelativeStreamUrlIsSameOriginSoSelfSuffices()
        {
            // "/stream" (the Caddy reference topology) parses as file:// on Unix — the http(s)
            // scheme guard drops it, and 'self' is exactly right for a same-origin stream.
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:PublicStreamUrl", "/stream"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Contains("media-src 'self';",
                Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioInvalidConfigFailsClosed
    {
        [Fact]
        public async Task AnUnparseableBaseUrlStillServesThePage()
        {
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:PublicBaseUrl", "not a url at all"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AnUnparseableBaseUrlPinsNothing()
        {
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:PublicBaseUrl", "not a url at all"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Contains("img-src 'self';",
                Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task ANonHttpSchemePinsNothing()
        {
            // Env-supplied config bypasses SettingValidator — a hostile or fat-fingered scheme
            // must narrow the policy to 'self', never enter it.
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:PublicBaseUrl", "javascript:alert(1)"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.Contains("img-src 'self';",
                Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioNonSpectatorRoutesAreUntouched
    {
        [Fact]
        public async Task TheHealthProbeCarriesNoContentSecurityPolicy()
        {
            await using var factory = new SecurityHeadersWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }

        [Fact]
        public async Task TheAdminApiCarriesNoContentSecurityPolicy()
        {
            await using var factory = new SecurityHeadersWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/status");

            Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }
    }

    public sealed class ScenarioDisabledSurfaceStaysBare
    {
        [Fact]
        public async Task AGated404CarriesNoSecurityHeaders()
        {
            // F61.2: a disabled surface's 404 must stay indistinguishable from an unmapped
            // route's — stamping security headers on it would fingerprint the surface's
            // existence. The middleware runs after the surface gate precisely for this.
            await using var factory = new SecurityHeadersWebFactory(
                ("Station:SpectatorMode", "false"));
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator");

            Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }
    }
}
