// STORY-278 — Isolation and the exit-demo (SPEC F103.12, F103.13)
//
// BDD specification — xUnit. The theme catalog adds NO public surface and no new disclosure vector:
// every catalog + /api/themes/* import route stays on the AdminSurface behind the Settings policy
// (the F79/F90 posture), a spectator payload is byte-identical with the catalog disabled/unreachable,
// and the catalog stays fail-closed on an empty/unreachable Community:CatalogIndexUrl.
//
// WIRED T190 — every Fact below drives the real production route table / HTTP pipeline through
// WebApplicationFactory<Program> (IsolationWebFactory below) — mirrors Story234_CatalogProxyGuardedDoor's
// own CatalogApiWebFactory (the catalog fetch, faked at the HTTP boundary) and Story272_ThemeImport's own
// ThemeImportWebFactory (IThemeStore replaced with a scriptable fake, no live Postgres — this project
// carries none, see Story271_OwnerThemeStorage's own remarks). The "unreachable" catalog state is a real
// loopback port nothing listens on (http://127.0.0.1:1, Gh148_HealthContainersEndpoint's own idiom) —
// immediate connection-refused, no DNS lookup, no timeout wait — rather than a faked HTTP failure,
// exactly as PLAN T190's own guidance asks.
//
// The exit-demo itself (the demo station visibly wears a catalog theme) is browser/operator-gated —
// verified against the running compose stack at T192, not here (Story173/operator-gated precedent).
//
// One assertion per Fact where the scenario allows it; the disclosure scenario is its own block
// (Given catalog on/off/unreachable, byte-identity is proven across BOTH DB states T182's own offline
// floor cares about — a live station.theme store and one that never answers); fail-closed is its own
// block, closing with the deliberate "import/preview trust the client body, never the catalog" fact
// PLAN T190's own review guidance calls out to pin rather than assume.

using System.Net;
using System.Net.Http.Json;
using System.Text;
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
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

// ── WebApplicationFactory + doubles ───────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for every Fact in this file: boots the real
/// Program.cs graph with <c>Community:CatalogIndexUrl</c> set to <paramref name="catalogIndexUrl"/>.
/// <paramref name="catalogHandler"/>, when supplied, replaces the whole-graph
/// <see cref="IHttpClientFactory"/> (mirrors Story234's own <c>CatalogApiWebFactory</c>) — left
/// <see langword="null"/> for the unreachable-catalog Facts so Program.cs's own REAL client hits the
/// dead-port URL exactly as it would in production. <paramref name="themeStore"/>, when supplied,
/// replaces <see cref="IThemeStore"/> with a scriptable fake (mirrors Story272's own
/// <c>ThemeImportWebFactory</c>) — left <see langword="null"/> to exercise the default
/// <c>ConnectionStrings:Library=Host=nowhere</c> "DB absent" state the F102.7 offline floor degrades to.
/// <paramref name="simulatedPublicPort"/>, when supplied, stamps the public listener's port onto every
/// request (mirrors Story172_PublicListenerIsolation's own <c>SimulatedPortStartupFilter</c>).
/// </summary>
file sealed class IsolationWebFactory(
    string catalogIndexUrl,
    HttpMessageHandler? catalogHandler = null,
    IThemeStore? themeStore = null,
    int? simulatedPublicPort = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story278";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);
        // SurfaceGateMiddleware's public-listener isolation check only engages once
        // Spectator:PublicPort is configured (its own "publicPort > 0" guard) — mirrors Story172/
        // Story248's own factories; harmless for every OTHER Fact here, since LocalPort only ever
        // matches it when simulatedPublicPort's own SimulatedPortStartupFilter stamps it.
        builder.UseSetting("Spectator:PublicPort", IsolationFixtures.PublicPort.ToString());

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            if (catalogHandler is not null)
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(catalogHandler));
            }

            if (themeStore is not null)
            {
                services.RemoveAll<IThemeStore>();
                services.AddSingleton(themeStore);
            }

            if (simulatedPublicPort is int port)
                services.AddSingleton<IStartupFilter>(new SimulatedPortStartupFilter(port));
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

/// <summary>Fixed inputs shared across this file's Facts — catalog states, a minimal valid index, a
/// pre-seeded owner theme, and a font-provenance-clean import manifest (T188's gate applies to every
/// real POST /api/themes/{slug}/import, so a fixture using an unvendored src would fail every import
/// Fact here for a reason unrelated to what this file pins).</summary>
file static class IsolationFixtures
{
    public const string ReachableCatalogUrl = "https://catalog.test/repo/index.json";
    public const string DisabledCatalogUrl = "";

    // A loopback port nothing listens on: the typed client fails fast with connection refused — no
    // DNS lookup, no timeout wait — same idiom as Gh148_HealthContainersEndpoint's own DockerStats:BaseUrl.
    public const string UnreachableCatalogUrl = "http://127.0.0.1:1/repo/index.json";

    public const int PublicPort = 8081;

    // The "reachable, enabled" state's index — deliberately NON-empty (review finding F1): an empty
    // index gives a spectator disclosure leak nothing to leak, so the byte-identity pin below could
    // never fire regardless of whether a leak existed. Carries one theme-kind entry with a preview
    // swatch payload — the exact F103 shape (SPEC F103.2-F103.4) — mirroring
    // Fixtures/mixed-catalog-index.json's own "golden-frequency" entry (STORY-273's own fixture)
    // rather than inventing a second copy of that shape; sha256 values are placeholder hex (this
    // Scenario never fetches the manifest/meta files themselves, only the index).
    const string ReachableIndexJson = """
        {
          "generatedAt": "2026-08-05",
          "entries": [
            {
              "slug": "golden-frequency",
              "kind": "theme",
              "audience": "everyone",
              "manifest": { "path": "entries/golden-frequency/golden-frequency.theme.json", "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
              "meta": { "path": "entries/golden-frequency/golden-frequency.meta.json", "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
              "preview": {
                "light": { "bg": "#f7ecd2", "surface": "#fff8e6", "ink": "#2c2410", "accent": "#b8860b", "accent-2": "#4f6b52" },
                "dark": { "bg": "#171205", "surface": "#241c09", "ink": "#f4ecce", "accent": "#e0a52c", "accent-2": "#7fa382" }
              }
            }
          ]
        }
        """;

    public static FakeHttpMessageHandler BuildReachableCatalogHandler() => new((request, _) =>
        Task.FromResult(request.RequestUri!.AbsoluteUri == ReachableCatalogUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReachableIndexJson, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound)));

    public const string OwnerThemeSlug = "isolation-owner-theme";

    /// <summary>A working <see cref="IThemeStore"/> double carrying one owner theme, written directly
    /// (never through the import route — so <see cref="ThemeFixtures.ValidManifestJson"/>'s
    /// non-vendored font src is fine here, unlike the import Facts below) — the "live DB" half of
    /// T182's own offline-floor axis.</summary>
    public static async Task<IThemeStore> BuildLiveThemeStoreAsync()
    {
        var store = new FakeThemeStore();
        await store.UpsertAsync(
            OwnerThemeSlug, ThemeFixtures.ValidManifestJson(OwnerThemeSlug), "file", CancellationToken.None);
        return store;
    }

    public const string ImportSlug = "isolation-import-theme";

    // Real vendored font srcs (PLAN T188, SPEC F103.10) — mirrors Story272's own ThemeImportFixture.
    public static readonly string ImportManifestJson = $$"""
        {
          "slug": "{{ImportSlug}}",
          "name": "Isolation Pin Theme",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureThemeCatalogIsolation
{
    const string DemoGated =
        "exit-demo — the demo station visibly wears a catalog theme, verified in a browser against " +
        "the running compose stack (PLAN T192, operator-gated).";

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioNoNewPublicRoute
    {
        // The known, deliberate set today (SPEC F103.6, F90.2, F104.4, F104.5, PLAN T184/T185/T194/
        // T199) — a seventh route joining any of the three prefixes is a disclosure decision
        // (SPEC F103.12), not a routing accident. The assets/{file} route (T194) delivers a font
        // pack's hash-verified binary asset (the F104.4 specimen face) — same CatalogController
        // class-level AdminSurface+Settings attributes as its siblings, never a new surface of its
        // own. api/fonts/{slug}/install (T199, SPEC F104.5) is FontPackController's own install
        // route — a third guarded prefix joining api/catalog and api/themes, pinned here (review
        // finding N4) the moment it exists rather than left to drift unnoticed.
        static readonly IReadOnlySet<(string Verb, string Route)> KnownCatalogAndThemeRoutes =
            new HashSet<(string Verb, string Route)>
            {
                ("GET", "api/catalog/index"),
                ("GET", "api/catalog/entries/{slug}"),
                ("GET", "api/catalog/entries/{slug}/assets/{file}"),
                ("POST", "api/themes/{slug}/import"),
                ("POST", "api/themes/preview"),
                ("POST", "api/fonts/{slug}/install"),
            };

        static List<RouteEndpoint> DiscoverCatalogAndThemeEndpoints(IServiceProvider services) =>
            services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText is { } raw
                    && (MatchesGuardedPrefix(raw.TrimStart('/'), "api/catalog")
                        || MatchesGuardedPrefix(raw.TrimStart('/'), "api/themes")
                        || MatchesGuardedPrefix(raw.TrimStart('/'), "api/fonts")))
                .ToList();

        // All three controllers are ROOTED at their own bare prefix ([Route("api/catalog")],
        // [Route("api/themes")], [Route("api/fonts")] — review finding F2, extended to the third
        // prefix at N4): a `StartsWith(prefix + "/")`-only check misses a route at EXACTLY
        // "api/catalog"/"api/themes"/"api/fonts" (a future parameterless [HttpGet] list action), so
        // the match is segment-bounded — the prefix itself, or the prefix followed by a '/' — never a
        // bare substring match.
        static bool MatchesGuardedPrefix(string route, string prefix) =>
            route == prefix || route.StartsWith(prefix + "/", StringComparison.Ordinal);

        [Fact]
        public void TheDiscoveredRouteSetMatchesTheKnownDeliberateSet()
        {
            // Given the real route table — Community:CatalogIndexUrl's value is irrelevant to which
            //       ROUTES exist, only to how they answer (this file's other Scenarios cover that),
            using var factory = new IsolationWebFactory(IsolationFixtures.DisabledCatalogUrl);
            _ = factory.CreateClient(); // force host build so the route table is populated

            // When every api/catalog/*, api/themes/*, and api/fonts/* route is discovered off the
            //      app's OWN table (never a hand-maintained mirror of it),
            var discovered = DiscoverCatalogAndThemeEndpoints(factory.Services)
                .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                    .Select(verb => (Verb: verb, Route: endpoint.RoutePattern.RawText!.TrimStart('/'))))
                .ToHashSet();

            // Then it is EXACTLY the known set (AC1) — not a subset check, which would let a seventh
            //      route join silently (mirrors Story264_AnonymousApiSurface's own "named, deliberate
            //      set" idiom, applied to the admin-not-public axis instead of the anonymous one).
            Assert.True(KnownCatalogAndThemeRoutes.SetEquals(discovered), FailureMessage(discovered));
        }

        static string FailureMessage(IReadOnlySet<(string Verb, string Route)> discovered)
        {
            var added = discovered.Except(KnownCatalogAndThemeRoutes).ToArray();
            var removed = KnownCatalogAndThemeRoutes.Except(discovered).ToArray();
            return "The api/catalog/* + api/themes/* + api/fonts/* route set no longer matches the " +
                "known, deliberate set. " +
                (added.Length > 0 ? $"Newly present: [{string.Join(", ", added)}]. " : "") +
                (removed.Length > 0 ? $"No longer present: [{string.Join(", ", removed)}]. " : "") +
                "A new route under any of these prefixes is a disclosure decision (SPEC F103.12) — " +
                "add it to KnownCatalogAndThemeRoutes above only once it carries AdminSurface + Settings.";
        }

        [Fact]
        public void EveryDiscoveredRouteCarriesAdminSurfaceAndTheSettingsPolicy()
        {
            using var factory = new IsolationWebFactory(IsolationFixtures.DisabledCatalogUrl);
            _ = factory.CreateClient();

            var endpoints = DiscoverCatalogAndThemeEndpoints(factory.Services);
            Assert.NotEmpty(endpoints); // guards this sweep against a silent rename emptying it

            // Then EVERY one of them — not a sample — carries AdminSurface and the Settings policy
            //      (AC1), the exact pairing CatalogController/ThemesImportController/
            //      ThemePreviewController's own class-level attributes declare. This fact reads
            //      IAuthorizeData only — a route that additionally picked up [AllowAnonymous] is
            //      Story264_AnonymousApiSurface's own route-table sweep to catch, not this one's.
            Assert.All(endpoints, endpoint =>
            {
                Assert.NotNull(endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>());

                var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(authorizeData => authorizeData.Policy)
                    .Where(candidate => !string.IsNullOrEmpty(candidate))
                    .Distinct()
                    .ToArray();

                // An explicit assertion, not LINQ's SingleOrDefault (review finding, F2's neighbour):
                // SingleOrDefault throws InvalidOperationException — an unhandled-exception test
                // failure carrying no route/policy context — the moment an endpoint carries two
                // distinct non-empty policies; this fails the normal xUnit way instead, naming the
                // route and the policy set actually found.
                Assert.True(
                    policies is [var onlyPolicy] && onlyPolicy == AuthorizationPolicies.Settings,
                    $"{endpoint.RoutePattern.RawText} carries policy set [{string.Join(", ", policies)}], " +
                    $"expected exactly one: \"{AuthorizationPolicies.Settings}\".");
            });
        }

        [Theory]
        [InlineData("GET", "/api/catalog/index")]
        [InlineData("GET", "/api/catalog/entries/anything")]
        [InlineData("GET", "/api/catalog/entries/anything/assets/anything.woff2")]
        [InlineData("POST", "/api/themes/anything/import")]
        [InlineData("POST", "/api/themes/preview")]
        [InlineData("POST", "/api/fonts/anything/install")]
        public async Task EveryRouteReturns404OnThePublicListener(string verb, string path)
        {
            // AdminSurface alone is a proxy for "not public" — SurfaceGateMiddleware's public-listener
            // check is what actually enforces it (only SpectatorSurface-tagged endpoints, plus
            // /health and /fonts/*, exist there), so this proves the BEHAVIOR the attribute predicts.
            // /api/fonts/anything/install (review finding N4) is the ADMIN install route this file's
            // own KnownCatalogAndThemeRoutes now pins — a distinct prefix from the public /fonts/*
            // this comment's own parenthetical names, which serves already-installed face bytes and
            // carries no AdminSurface at all.
            await using var factory = new IsolationWebFactory(
                IsolationFixtures.DisabledCatalogUrl, simulatedPublicPort: IsolationFixtures.PublicPort);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(verb), path));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioTheDemoWearsACatalogTheme
    {
        [Fact(Skip = DemoGated)]
        public void TheStationVisiblyRendersTheInstalledCatalogTheme()
        {
            // Given the demo station,
            // When a catalog theme is installed and activated,
            // Then the station visibly renders it (before/after), and the shelf shows themes beside
            //      personas (AC2) — the epic's observable "shipped", verified live at T192.
            Assert.Fail(DemoGated);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioSpectatorIsUnchangedRegardlessOfCatalogState
    {
        // /spectator/theme.css (the composed active-theme sheet) and /spectator/api/themes (the
        // switcher's list, which — unlike theme.css — DOES read ThemeCatalog.All, so it is the one
        // spectator route most likely to drift if the community catalog ever leaked into it) are the
        // two spectator routes SpectatorThemeEndpoints/SpectatorThemesEndpoint's own remarks name as
        // reading ThemeCatalog at all; every other spectator route (now-playing, play-history, stats,
        // about, artwork) never touches theming and so cannot plausibly be affected by either axis
        // this Scenario varies.

        static async Task<string> CapturedPayloadAsync(
            string route, string catalogIndexUrl, HttpMessageHandler? catalogHandler,
            IThemeStore? themeStore, bool reloadOwnerThemes)
        {
            await using var factory = new IsolationWebFactory(catalogIndexUrl, catalogHandler, themeStore);
            var client = factory.CreateClient();

            // IHostedService is removed from every factory here (no engine/DB warm-up needed), so the
            // boot-time ThemeCatalogOwnerLoadHostedService never runs — "live DB" is instead driven
            // explicitly, against the SAME singleton the request pipeline itself reads (mirrors
            // Story272's own "ReloadOwnerThemesAsync reaches the SAME singleton" proof).
            if (reloadOwnerThemes)
                await factory.Services.GetRequiredService<ThemeCatalog>().ReloadOwnerThemesAsync(CancellationToken.None);

            var response = await client.GetAsync(route);
            return await response.Content.ReadAsStringAsync();
        }

        static async Task AssertByteIdenticalAcrossCatalogStatesAsync(
            string route, IThemeStore? themeStore, bool reloadOwnerThemes)
        {
            var reachableHandler = IsolationFixtures.BuildReachableCatalogHandler();
            var reachable = await CapturedPayloadAsync(
                route, IsolationFixtures.ReachableCatalogUrl, reachableHandler, themeStore, reloadOwnerThemes);

            // "No catalog fetch occurs on the spectator path" as a RECORDED fact, not an inference
            // from the byte-identity comparison below (review finding F1): the reachable-state
            // fixture now carries a real theme-kind entry (ReachableIndexJson above), so this is a
            // meaningful proof that the spectator route never even ASKS the catalog for it.
            Assert.Empty(reachableHandler.Requests);

            var disabled = await CapturedPayloadAsync(
                route, IsolationFixtures.DisabledCatalogUrl, catalogHandler: null, themeStore, reloadOwnerThemes);
            var unreachable = await CapturedPayloadAsync(
                route, IsolationFixtures.UnreachableCatalogUrl, catalogHandler: null, themeStore, reloadOwnerThemes);

            // One assertion bundling the whole 3-way claim (this codebase's tuple-equality idiom,
            // e.g. Story271's own ScenarioAnOwnerThemePersists): enabled+reachable, disabled, and
            // unreachable must all be byte-identical — no disclosure drift on any axis.
            Assert.Equal((reachable, reachable), (disabled, unreachable));
        }

        [Fact]
        public Task ThemeCssIsByteIdenticalAcrossCatalogStatesWithTheDbAbsent() =>
            AssertByteIdenticalAcrossCatalogStatesAsync("/spectator/theme.css", themeStore: null, reloadOwnerThemes: false);

        [Fact]
        public async Task ThemeCssIsByteIdenticalAcrossCatalogStatesWithALiveDb() =>
            await AssertByteIdenticalAcrossCatalogStatesAsync(
                "/spectator/theme.css", await IsolationFixtures.BuildLiveThemeStoreAsync(), reloadOwnerThemes: true);

        [Fact]
        public Task SpectatorThemesJsonIsByteIdenticalAcrossCatalogStatesWithTheDbAbsent() =>
            AssertByteIdenticalAcrossCatalogStatesAsync("/spectator/api/themes", themeStore: null, reloadOwnerThemes: false);

        [Fact]
        public async Task SpectatorThemesJsonIsByteIdenticalAcrossCatalogStatesWithALiveDb() =>
            await AssertByteIdenticalAcrossCatalogStatesAsync(
                "/spectator/api/themes", await IsolationFixtures.BuildLiveThemeStoreAsync(), reloadOwnerThemes: true);

        [Fact]
        public async Task TheLiveDbFixtureIsNotVacuous()
        {
            // Guards the two ALiveDb Facts above against passing for the wrong reason: proves the
            // owner theme this Scenario seeds really does reach /spectator/api/themes (so its
            // byte-identity claim is comparing a payload that KNOWABLY differs from the DB-absent
            // case, not one that happens to look the same either way) — mirrors Story265's own
            // "don't fake it" warning.
            var withOwnerTheme = await CapturedPayloadAsync(
                "/spectator/api/themes", IsolationFixtures.ReachableCatalogUrl,
                IsolationFixtures.BuildReachableCatalogHandler(),
                await IsolationFixtures.BuildLiveThemeStoreAsync(), reloadOwnerThemes: true);

            Assert.Contains(IsolationFixtures.OwnerThemeSlug, withOwnerTheme, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheCatalogStaysFailClosed
    {
        // Collapsed from four near-identical Fact pairs, each varying only a URL, into four Theories
        // (review finding, note 4) — the same [Theory]/[InlineData] idiom
        // EveryRouteReturns404OnThePublicListener above already uses.

        [Theory]
        [InlineData("/api/catalog/index")]
        [InlineData("/api/catalog/entries/anything")]
        public async Task RouteReturns404WhenDisabled(string path)
        {
            await using var factory = new IsolationWebFactory(IsolationFixtures.DisabledCatalogUrl);
            var client = await IsolationWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData("/api/catalog/index")]
        [InlineData("/api/catalog/entries/anything")]
        public async Task RouteDegradesGracefullyWhenUnreachable(string path)
        {
            await using var factory = new IsolationWebFactory(IsolationFixtures.UnreachableCatalogUrl);
            var client = await IsolationWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // CatalogIndexResponse/CatalogEntryResponse are two distinct record shapes (persona/theme
            // detail vs shelf listing) — both carry the SAME "unreachable" wire field, read here
            // generically so one Theory covers both routes rather than one typed Fact per route.
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("unreachable").GetBoolean());
        }

        // Import/preview trust the client's OWN posted body — ?catalogSlug is provenance text only,
        // never a key the route re-fetches the catalog with (see ThemesImportController's own
        // remarks: it depends on IThemeStore/ThemeCatalog, never CommunityCatalogAccessor/
        // CatalogProxyService). Pinned here rather than assumed, per PLAN T190's own review guidance.

        [Theory]
        [InlineData(IsolationFixtures.DisabledCatalogUrl)]
        [InlineData(IsolationFixtures.UnreachableCatalogUrl)]
        public async Task ImportSucceedsRegardlessOfCatalogState(string catalogIndexUrl)
        {
            await using var factory = new IsolationWebFactory(catalogIndexUrl, themeStore: new FakeThemeStore());
            var client = await IsolationWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync(
                $"/api/themes/{IsolationFixtures.ImportSlug}/import",
                new StringContent(IsolationFixtures.ImportManifestJson, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData(IsolationFixtures.DisabledCatalogUrl)]
        [InlineData(IsolationFixtures.UnreachableCatalogUrl)]
        public async Task PreviewSucceedsRegardlessOfCatalogState(string catalogIndexUrl)
        {
            await using var factory = new IsolationWebFactory(catalogIndexUrl);
            var client = await IsolationWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync(
                "/api/themes/preview",
                new StringContent(IsolationFixtures.ImportManifestJson, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
