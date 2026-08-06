// STORY-283 — Installed faces serve at /fonts (SPEC F104.6, F104.8 · PLAN T200)
//
// BDD specification — xUnit. GET /fonts/{file}'s closed literal switch (STORY-263/T173) widens to a
// closed SET: vendored literals ∪ station.font_pack_face rows, via InstalledFontCatalog's in-memory
// snapshot (never a per-request store read). F104.6's non-enumerability is an ANONYMOUS-surface rule:
// no ANONYMOUS surface lists installed face filenames — 401 on GET /api/fonts, 404 on the public
// listener, no bare listing route anywhere. It does NOT forbid the Settings-gated ADMIN listing GET
// api/fonts (PLAN T203, SPEC F104.7) — that route deliberately carries every installed face's
// filename via `faces[].file`, for an authenticated operator inspecting installed PACKS
// (family/faces/licence/provenance), a different audience than the anonymous rule guards against —
// see ScenarioTheSetStaysClosedAndNonEnumerable's own remarks for the distinction this file now draws
// between the two.
//
// WIRED T200 — every Fact below drives the real production route table through
// WebApplicationFactory<Program> (InstalledFontServingWebFactory below), mirroring
// Story282_FontPackInstall.cs's own FontPackInstallWebFactory idiom (a fake catalog origin +
// FakeFontPackStore, no live Postgres — this project carries none). ScenarioTheClosedSetWidens drives
// a REAL POST /api/fonts/{slug}/install (proving FontPackController's own post-write
// InstalledFontCatalog.ReloadAsync rebuild hook actually reaches the request pipeline) before its own
// GET; ScenarioInstalledFacesSurviveOutages seeds FakeFontPackStore directly
// (FakeFontPackStore.WithInstalledFace — mirrors Story278_ThemeCatalogIsolation.cs's own
// BuildLiveThemeStoreAsync precedent for an isolation-focused spec that has no need to re-derive a
// whole install fixture) and drives InstalledFontCatalog.ReloadAsync explicitly, the same "boot
// warm-up, simulated" idiom Story278 uses for ThemeCatalog.ReloadOwnerThemesAsync.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad/edge
// path (unknown file, the route-set pin) is its own block.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureInstalledFontServing
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheClosedSetWidens
    {
        [Fact]
        public async Task AnInstalledFaceServesWithWoff2ContentType()
        {
            // Given a pack installed through the real production install route (proving the T200
            // rebuild hook actually reaches this request pipeline, not just InstalledFontCatalog in
            // isolation),
            var store = new FakeFontPackStore();
            await using var factory = new InstalledFontServingWebFactory(store);
            var client = await InstalledFontServingWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/fonts/{InstalledFontServingFixtures.Slug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When its face is requested at the widened /fonts/{file} route,
            var response = await client.GetAsync($"/fonts/{InstalledFontServingFixtures.AssetFile}");

            // Then it serves with the same font/woff2 content type a vendored face carries (SPEC
            // F104.6), and the exact bytes that were installed (byte[] carries no value equality, so
            // both sides are compared as base64 text rather than via a tuple's default
            // reference-equality-on-arrays behaviour).
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(
                (HttpStatusCode.OK, "font/woff2", Convert.ToBase64String(InstalledFontServingFixtures.AssetBytes)),
                (response.StatusCode, response.Content.Headers.ContentType?.MediaType, Convert.ToBase64String(bytes)));
        }

        [Fact]
        public async Task AnInstalledFaceCarriesTheVendoredCachingPosture()
        {
            // Given the same installed pack,
            var store = new FakeFontPackStore();
            await using var factory = new InstalledFontServingWebFactory(store);
            var client = await InstalledFontServingWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/fonts/{InstalledFontServingFixtures.Slug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When a vendored face and the installed face are both requested,
            var vendored = await client.GetAsync("/fonts/fraunces-variable-latin.woff2");
            var installed = await client.GetAsync($"/fonts/{InstalledFontServingFixtures.AssetFile}");

            // Then the installed response's own Cache-Control and X-Content-Type-Options headers are
            // byte-identical to the vendored one's — proven by direct comparison, not by restating the
            // expected literal values a second time (SPEC F104.6 "identical to vendored").
            Assert.Equal(
                (vendored.Headers.CacheControl?.ToString(), HeaderValue(vendored, "X-Content-Type-Options")),
                (installed.Headers.CacheControl?.ToString(), HeaderValue(installed, "X-Content-Type-Options")));
        }

        static string? HeaderValue(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }

    public sealed class ScenarioInstalledFacesSurviveOutages
    {
        [Fact]
        public async Task ALoadedFaceStillServesWithTheCatalogUnreachable()
        {
            // Given a face already folded into InstalledFontCatalog's snapshot (the "boot warm-up
            // already completed" idiom — mirrors Story278_ThemeCatalogIsolation's own
            // ReloadOwnerThemesAsync-before-the-request precedent) — and THEN both halves of SPEC
            // F104.8's outage clause: the Community Catalog origin unreachable, and the font-pack
            // store itself gone (FakeFontPackStore.Broken),
            var store = FakeFontPackStore.WithInstalledFace(
                "already-installed", "Already Installed", InstalledFontServingFixtures.AssetFile,
                InstalledFontServingFixtures.AssetBytes, InstalledFontServingFixtures.AssetSha256);
            await using var factory = new InstalledFontServingWebFactory(
                store, catalogIndexUrl: InstalledFontServingFixtures.UnreachableCatalogUrl);
            var client = factory.CreateClient();
            await factory.Services.GetRequiredService<InstalledFontCatalog>().ReloadAsync(CancellationToken.None);
            store.Broken = true;

            // When that already-loaded face is requested,
            var response = await client.GetAsync($"/fonts/{InstalledFontServingFixtures.AssetFile}");

            // Then it still serves — InstalledFontCatalog.TryGetFace never touches the store per
            // request, so neither outage can reach this path.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task TheEmbeddedThemeFloorIsUntouchedByPackMachinery()
        {
            // Given the exact same double outage as above,
            var store = FakeFontPackStore.WithInstalledFace(
                "already-installed", "Already Installed", InstalledFontServingFixtures.AssetFile,
                InstalledFontServingFixtures.AssetBytes, InstalledFontServingFixtures.AssetSha256);
            await using var factory = new InstalledFontServingWebFactory(
                store, catalogIndexUrl: InstalledFontServingFixtures.UnreachableCatalogUrl);
            var client = factory.CreateClient();
            await factory.Services.GetRequiredService<InstalledFontCatalog>().ReloadAsync(CancellationToken.None);
            store.Broken = true;

            // When the shipped default theme's own composed stylesheet is requested,
            var response = await client.GetAsync("/api/theme.css");

            // Then it still serves — the embedded themes' own SPEC F102.7 floor is a wholly separate
            // seam (ThemeCatalog) that InstalledFontCatalog's own failure mode can never reach.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── SAD / EDGE PATH ─────────────────────────────────────────────────────

    public sealed class ScenarioTheSetStaysClosedAndNonEnumerable
    {
        [Fact]
        public async Task AnUnknownFileStill404s()
        {
            // Given a pack genuinely installed (so a miss below is proven against a NON-empty
            // installed set, not a vacuously empty one),
            var store = FakeFontPackStore.WithInstalledFace(
                "already-installed", "Already Installed", InstalledFontServingFixtures.AssetFile,
                InstalledFontServingFixtures.AssetBytes, InstalledFontServingFixtures.AssetSha256);
            await using var factory = new InstalledFontServingWebFactory(store);
            var client = factory.CreateClient();
            await factory.Services.GetRequiredService<InstalledFontCatalog>().ReloadAsync(CancellationToken.None);

            // When a file name that is neither vendored nor installed is requested,
            var response = await client.GetAsync("/fonts/definitely-not-a-real-face.woff2");

            // Then it still 404s exactly as before T200 (SPEC F104.6).
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public void TheRouteSetMatchesTheKnownDeliberateSet()
        {
            // Given the real route table (Community:CatalogIndexUrl's value is irrelevant to which
            //       ROUTES exist),
            using var factory = new InstalledFontServingWebFactory(new FakeFontPackStore());
            _ = factory.CreateClient(); // force host build so the route table is populated

            // When every route under either "fonts" or "api/fonts" is discovered off the app's OWN
            //      table (never a hand-maintained mirror of it — mirrors
            //      Story264_AnonymousApiSurface/Story278_ThemeCatalogIsolation's own route-table-sweep
            //      idiom),
            var discovered = DiscoverFontEndpoints(factory.Services).Select(entry => entry.Route).ToHashSet();

            // Then it is EXACTLY the known serving/install/library set (PLAN T203, SPEC F104.7 —
            //      widened from the pre-T203 serving-and-install-only set) — not a subset check, which
            //      would let an EIGHTH route join either prefix silently. Segment-bounded matching
            //      (mirrors Story278's own MatchesGuardedPrefix) means a hypothetical bare
            //      filename-enumeration route under "fonts" — the one thing SPEC F104.6 still
            //      forbids — would show up as an UNEXPECTED member of `discovered` and fail this
            //      assertion by name, same as before T203.
            Assert.True(ExpectedFontRoutes.SetEquals(discovered), FailureMessage(discovered));
        }

        /// <summary>
        /// T203 review-obligation rider (T200 review finding N7, "assert surface attributes, not just
        /// route names") — every route under <c>api/fonts</c> (both the pre-existing install route and
        /// the new <c>GET api/fonts</c> library listing, SPEC F104.7) carries the SAME
        /// <see cref="AdminSurfaceAttribute"/>+<see cref="AuthorizationPolicies.Settings"/> pairing
        /// <c>Story278_ThemeCatalogIsolation.cs</c>'s own <c>EveryDiscoveredRouteCarriesAdminSurfaceAndTheSettingsPolicy</c>
        /// asserts for its own three prefixes — repeated here rather than relied on solely over there,
        /// because THIS file's <c>fonts/{file}</c> serving route is the one route this sweep discovers
        /// that must NOT carry it (F104.6's public, non-admin serving route) — proving the split by
        /// name, not merely a subset the other file's broader sweep happens to also cover.
        /// </summary>
        [Fact]
        public void OnlyTheAdminFontsRoutesCarryAdminSurfaceAndSettingsThePublicServingRouteDoesNot()
        {
            using var factory = new InstalledFontServingWebFactory(new FakeFontPackStore());
            _ = factory.CreateClient();

            var discovered = DiscoverFontEndpoints(factory.Services).ToArray();
            Assert.NotEmpty(discovered); // guards this sweep against a silent rename emptying it

            Assert.All(discovered, entry =>
            {
                var isAdminRoute = entry.Route.Route.StartsWith("api/fonts", StringComparison.Ordinal);
                var carriesAdminSurface = entry.Endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>() is not null;
                var policies = entry.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(authorizeData => authorizeData.Policy)
                    .Where(candidate => !string.IsNullOrEmpty(candidate))
                    .Distinct()
                    .ToArray();

                if (isAdminRoute)
                {
                    Assert.True(carriesAdminSurface, $"{entry.Route} is under api/fonts but carries no AdminSurfaceAttribute.");
                    Assert.True(
                        policies is [var onlyPolicy] && onlyPolicy == AuthorizationPolicies.Settings,
                        $"{entry.Route} carries policy set [{string.Join(", ", policies)}], expected exactly one: " +
                        $"\"{AuthorizationPolicies.Settings}\".");
                }
                else
                {
                    Assert.False(carriesAdminSurface, $"{entry.Route} is the public fonts/{{file}} route but carries AdminSurfaceAttribute.");
                    Assert.Empty(policies);
                }
            });
        }

        /// <summary>Every discovered fonts/api-fonts endpoint paired with its own (verb, route) —
        /// one shared discovery walk both Facts above read from, so the route SET and the per-route
        /// SURFACE ATTRIBUTE checks can never silently discover a different set from one another.</summary>
        static IEnumerable<(RouteEndpoint Endpoint, (string Verb, string Route) Route)> DiscoverFontEndpoints(IServiceProvider services) =>
            services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText is { } raw && MatchesEitherFontsPrefix(raw.TrimStart('/')))
                .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                    .Select(verb => (Endpoint: endpoint, Route: (Verb: verb, Route: endpoint.RoutePattern.RawText!.TrimStart('/')))));

        // "fonts" bare, or "fonts/…", or "api/fonts" bare, or "api/fonts/…" — segment-bounded so a
        // route at exactly "fontsomething" (a real but unrelated prefix) never falsely matches.
        static bool MatchesEitherFontsPrefix(string route) =>
            MatchesPrefix(route, "fonts") || MatchesPrefix(route, "api/fonts");

        static bool MatchesPrefix(string route, string prefix) =>
            route == prefix || route.StartsWith(prefix + "/", StringComparison.Ordinal);

        // The known, deliberate set (SPEC F104.5/F104.6/F104.7, widened at PLAN T203): the widened
        // serving route (GET+HEAD, one parameterized segment — F104.6's non-enumerability is an
        // ANONYMOUS-surface rule: no anonymous route lists these filenames), the install route (POST,
        // write), and the library listing route (GET api/fonts, T203 — the Settings-gated ADMIN
        // listing F104.7 requires, which DOES carry every installed face's filename via `faces[].file`
        // by design; that is not the anonymous enumeration F104.6 forbids) — nothing else has ever
        // joined either prefix, and this pins it stays that way.
        static readonly IReadOnlySet<(string Verb, string Route)> ExpectedFontRoutes =
            new HashSet<(string Verb, string Route)>
            {
                ("GET", "fonts/{file}"),
                ("HEAD", "fonts/{file}"),
                ("POST", "api/fonts/{slug}/install"),
                ("GET", "api/fonts"),
            };

        static string FailureMessage(IReadOnlySet<(string Verb, string Route)> discovered)
        {
            var added = discovered.Except(ExpectedFontRoutes).ToArray();
            var removed = ExpectedFontRoutes.Except(discovered).ToArray();
            return "The fonts/* + api/fonts/* route set no longer matches the known, deliberate set " +
                $"[{string.Join(", ", ExpectedFontRoutes)}]. " +
                (added.Length > 0 ? $"Newly present: [{string.Join(", ", added)}]. " : "") +
                (removed.Length > 0 ? $"No longer present: [{string.Join(", ", removed)}]. " : "") +
                "The /fonts/{file} SERVING set stays deliberately non-enumerable (SPEC F104.6) — a bare " +
                "filename-listing route under either prefix is a disclosure decision, not a routing " +
                "accident; the library's own PACK listing (SPEC F104.7, GET api/fonts) is a distinct, " +
                "deliberate exception this set now names.";
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// <c>Story282_FontPackInstall.cs</c>'s own <c>FontPackInstallWebFactory</c> exactly (a fake catalog
/// origin behind <see cref="IHttpClientFactory"/>, <see cref="IFontPackStore"/> replaced by a
/// <see cref="FakeFontPackStore"/>, no live Postgres).
/// </summary>
file sealed class InstalledFontServingWebFactory(
    FakeFontPackStore store, HttpMessageHandler? handler = null,
    string catalogIndexUrl = InstalledFontServingFixtures.IndexUrl) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story283-fontserving";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new SingleHandlerHttpClientFactory(handler ?? InstalledFontServingFixtures.BuildRoutedHandler()));

            services.RemoveAll<IFontPackStore>();
            services.AddSingleton<IFontPackStore>(store);
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>
/// Fixture documents + a routed fake HTTP double for <see cref="ScenarioTheClosedSetWidens"/>'s own
/// real-install Facts — a single valid <c>kind:"font"</c> entry, dedicated to serving specs (not
/// <c>Story282_FontPackInstall.cs</c>'s own golden Space Grotesk fixture, which pins hash-verification
/// mechanics this file has no need to re-derive). <c>AssetBytes</c> is deliberately NOT a real woff2
/// binary — <c>GET /fonts/{file}</c> never parses a face's payload, only serves it, so any non-empty
/// byte content proves this file's own claims just as well.
/// </summary>
file static class InstalledFontServingFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/serving-index.json";
    const string Directory = "https://catalog.test/repo/";

    // A loopback port nothing listens on — immediate connection-refused, no DNS lookup, no timeout
    // wait (mirrors Story278_ThemeCatalogIsolation's own UnreachableCatalogUrl/Gh148's own idiom).
    public const string UnreachableCatalogUrl = "http://127.0.0.1:1/repo/index.json";

    public const string Slug = "serving-test-pack";
    const string Family = "Serving Test";
    public const string AssetFile = "serving-test-variable-latin.woff2";

    public static readonly byte[] AssetBytes = "installed face bytes for /fonts serving specs (T200)"u8.ToArray();

    public static string AssetSha256 => Sha256Hex(AssetBytes);

    static string ManifestJson => $$"""
        {"family":"{{Family}}","files":[{"role":"upright","file":"{{AssetFile}}","weight":"400","style":"normal","bytes":{{AssetBytes.Length}}}],"license":"OFL-1.1","sourceUrl":"https://example.test/serving","version":"1.0","subset":"text"}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A pack for /fonts serving specs.","audience":"everyone","added":"2026-08-05"}
        """;

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-05", "entries": [
          { "slug": "{{Slug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{AssetFile}}", "sha256": "{{AssetSha256}}", "bytes": {{AssetBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + Slug + "/" + Slug + ".font.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + AssetFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(AssetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
