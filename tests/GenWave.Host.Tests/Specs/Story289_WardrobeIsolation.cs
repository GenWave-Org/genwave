// STORY-289 — Disclosure re-audit + the M2 exit-demo (SPEC F104.15 · PLAN T209/T210)
//
// BDD specification — xUnit. AC1's own two clauses become this file's two Facts: every guarded route
// under api/catalog, api/themes, and api/fonts carries AdminSurface+Settings, 404s on the public
// listener, AND still routes for real behind it (a positive control — review finding F2 — so a probe
// path that never routes at all can never masquerade as "the gate correctly blocked it"); and the
// spectator surface changes ONLY through what the worn theme legitimately references — installing a
// pack and saving a remix that is NEVER worn changes nothing about the DEFAULT theme's own composed
// sheet, with ONE deliberate, narrowly-shaped exception (review finding F1, SPEC F102.10a): the
// switcher's own theme LIST legitimately grows by one option, {slug, name} and nothing more. WEARING
// that remix then legitimately serves its own face through /fonts/{file} exactly as a vendored one
// does, carrying no pack metadata (slug, licence, provenance) into the composed sheet.
//
// AC2 (the demo wears a remix) is the 🖐️ T210 operator gate — no automated spec (unchanged from the
// Skip'd stub this file replaces).
//
// WIRED T209 — every Fact below drives the real production route table / HTTP pipeline through
// WebApplicationFactory<Program> (WardrobeIsolationWebFactory below), mirroring
// Story278_ThemeCatalogIsolation.cs's own IsolationWebFactory (SimulatedPortStartupFilter for the
// public-listener half) and Story283_InstalledFontServing.cs/Story286_EditorComposesTheRemix.cs's own
// "always swap in a FakeFontPackStore/FakeThemeStore" idiom — no live Postgres, this project carries
// none. The first Fact discovers routes off EndpointDataSource directly (never a hand-copied literal
// set), via the shared GenWave.Host.Tests.Fakes.GuardedRouteInspector (PLAN T209 review finding N3,
// extracted here on the THIRD near-verbatim copy of this discovery + AdminSurface/Settings shape check
// — Story278_ThemeCatalogIsolation.cs and Story283_InstalledFontServing.cs both migrated onto it in the
// same task) — so a genuinely NEW route under any guarded prefix is swept in automatically the moment
// it exists, closing the exact gap a hand-maintained HashSet could silently drift from. The second Fact
// drives a REAL font-pack install and a REAL save-as-own through the production routes (mirrors
// Story283's own "prove the rebuild hook actually reaches this request pipeline" idiom, extended to
// save-as-own too), never a fixture seeded straight into a fake store — so the whole install →
// widen-the-law → save → resolve → compose → serve chain is proven end-to-end, not just its two ends.
//
// One assertion per Fact where the scenario allows it; both of this file's own Facts are inherently
// multi-claim integration proofs (mirrors Story278's own AssertByteIdenticalAcrossCatalogStatesAsync
// and Story287's own multi-Assert Facts) — each claim is still named individually rather than folded
// into one opaque boolean.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed class FeatureWardrobeIsolation
{
    public sealed class ScenarioTheAuditRerunsOverTheWidenedSet
    {
        [Fact]
        public async Task EveryEditorLibraryAndFontRouteIsAdminSurfaceBehindSettings()
        {
            // Given the real route table on TWO listeners off the SAME app graph — the public one
            // (SimulatedPortStartupFilter, mirrors Story278_ThemeCatalogIsolation.cs's own
            // EveryRouteReturns404OnThePublicListener idiom) and the ordinary internal one — so this
            // Fact proves the static metadata (AdminSurface+Settings) AND both runtime behaviours that
            // metadata predicts: a 404 in front of the public gate, and a REAL 401 behind the internal
            // one (review finding F2's positive control — see below).
            await using var publicFactory = new WardrobeIsolationWebFactory(simulatedPublicPort: WardrobeIsolationFixtures.PublicPort);
            var publicClient = publicFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await using var internalFactory = new WardrobeIsolationWebFactory();
            var internalClient = internalFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When every route under api/catalog, api/themes, and api/fonts — every guarded prefix
            // this epic's own routes ever join (review finding N3: widened from "just editor/library"
            // to the whole guarded set, since CatalogController carries the identical
            // AdminSurface+Settings pairing and costs this sweep nothing extra to include) — is
            // discovered off the app's OWN table via the shared GuardedRouteInspector (never a
            // hand-maintained mirror of it, per this task's own "consolidated audit fact… so drift in
            // ANY of them shows up in one suite" charge). Story278_ThemeCatalogIsolation.cs's own
            // EveryRouteReturns404OnThePublicListener Theory keeps its existing literal InlineData list
            // rather than being migrated onto this sweep — the two are deliberately redundant, not
            // merged, so a regression this discovery-based sweep might ever miss for an unforeseen
            // reason still has that hand-picked list catching it.
            var endpoints = GuardedRouteInspector.DiscoverEndpoints(publicFactory.Services, "api/catalog", "api/themes", "api/fonts");
            Assert.NotEmpty(endpoints); // guards this sweep against a silent rename emptying it

            var violations = new List<string>();
            foreach (var endpoint in endpoints)
            {
                var route = endpoint.RoutePattern.RawText!.TrimStart('/');

                var (carriesAdminSurface, hasExactlySettings, policies) = GuardedRouteInspector.AdminSurfaceShape(endpoint);
                if (!carriesAdminSurface)
                    violations.Add($"{route}: missing {nameof(AdminSurfaceAttribute)}");
                if (!hasExactlySettings)
                {
                    violations.Add(
                        $"{route}: policy set [{string.Join(", ", policies)}], expected exactly " +
                        $"\"{AuthorizationPolicies.Settings}\"");
                }

                var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
                if (verbs.Count == 0)
                {
                    // Review finding F2: a route mapped with no HttpMethodMetadata at all would
                    // otherwise silently contribute ZERO probes below — passing not because it is
                    // safe, but because this sweep never asked it anything. Named here instead, so a
                    // future verb-agnostic mapping under any guarded prefix fails loudly rather than
                    // quietly vanishing from this audit.
                    violations.Add($"{route}: carries no HttpMethodMetadata — this sweep cannot probe it on any verb");
                    continue;
                }

                foreach (var verb in verbs)
                {
                    var path = GuardedRouteInspector.ConcreteRequestPath(endpoint.RoutePattern.RawText!);

                    var publicResponse = await publicClient.SendAsync(new HttpRequestMessage(new HttpMethod(verb), path));
                    if (publicResponse.StatusCode != HttpStatusCode.NotFound)
                        violations.Add($"{verb} {route}: public listener returned {publicResponse.StatusCode}, expected 404");

                    // Positive control (review finding F2): the SAME concretized path, sent
                    // ANONYMOUSLY on the INTERNAL listener, must resolve to a REAL, routed endpoint —
                    // 401 Unauthorized — never a SECOND 404. Without this, the sweep could pass for the
                    // wrong reason: a probe path that never routes at all (a ConcreteRequestPath
                    // defect, or a route pattern this helper mis-concretizes) is indistinguishable from
                    // "the public gate correctly blocked it", since both look like a bare 404 from
                    // here. This control is what tells the two apart. UseAuthorization always runs
                    // BEFORE any controller action body (so an anonymous request 401s regardless of
                    // Community:CatalogIndexUrl — verified empirically, not assumed), which is exactly
                    // why WardrobeIsolationFixtures.IndexUrl is deliberately kept non-empty anyway: it
                    // is what keeps this control testing "does the ROUTE exist", the on-topic question,
                    // rather than depending on that fact holding — a future change to internalClient
                    // (e.g. logging it in, the way publicClient never needs to be) would reach
                    // CatalogController's own kill-switch 404 for a disabled catalog, and THAT read
                    // would be the correct-but-confusing false negative this fixture choice heads off.
                    var internalResponse = await internalClient.SendAsync(new HttpRequestMessage(new HttpMethod(verb), path));
                    if (internalResponse.StatusCode != HttpStatusCode.Unauthorized)
                    {
                        violations.Add(
                            $"{verb} {route}: internal listener returned {internalResponse.StatusCode}, expected 401 " +
                            "(positive control — proves the probe path actually reached a real, routed endpoint)");
                    }
                }
            }

            // Then every discovered route carries AdminSurface+Settings, 404s on the public listener,
            // AND genuinely routes behind it (AC1's "all new routes are AdminSurface+Settings") — one
            // named violation list, never one assertion per route, so a defect on route #6 is never
            // masked by route #1 passing first.
            Assert.True(violations.Count == 0, string.Join("; ", violations));
        }

        [Fact]
        public async Task TheSpectatorSurfaceChangesOnlyThroughTheWornThemesLegitimateReferences()
        {
            // Given a running station wearing its shipped default theme, with NEITHER a font pack NOR
            // a remix installed yet — the pre-M2 baseline every spectator visitor already saw,
            await using var factory = new WardrobeIsolationWebFactory();
            var adminClient = await WardrobeIsolationWebFactory.LoggedInClientAsync(factory);
            var spectatorClient = factory.CreateClient();
            var baselineCss = await ReadStringAsync(spectatorClient, "/spectator/theme.css");
            var baselineThemesJson = await ReadStringAsync(spectatorClient, "/spectator/api/themes");

            // When a font pack is installed and a remix referencing its own face is saved-as-own —
            // both real production writes (mirrors Story283_InstalledFontServing.cs's own "prove the
            // rebuild hook actually reaches this request pipeline" idiom, extended to save-as-own) —
            // but the remix is NEVER worn: no cookie is ever set, Station:Theme is never touched,
            var install = await adminClient.PostAsync($"/api/fonts/{WardrobeIsolationFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            var save = await adminClient.PostAsync(
                $"/api/themes/{WardrobeIsolationFixtures.RemixSlug}/save-as-own",
                new StringContent(WardrobeIsolationFixtures.RemixManifestJson, Encoding.UTF8, "application/json"));
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());

            var unwornCss = await ReadStringAsync(spectatorClient, "/spectator/theme.css");
            var unwornThemesJson = await ReadStringAsync(spectatorClient, "/spectator/api/themes");

            // Then the SAME anonymous spectator sees the EXACT same composed sheet as before either
            // write — installing a pack and saving a remix that is never worn changes nothing about
            // the page a spectator wearing the default theme receives (AC1's "changes only through
            // what the WORN theme legitimately references").
            Assert.Equal(baselineCss, unwornCss);

            // Then /spectator/api/themes is the ONE deliberate exception (review finding F1): the
            // switcher legitimately lists every RESOLVABLE theme, worn or not (SPEC F102.10a,
            // STORY-266) — so a never-worn save-as-own DOES change this ONE payload, by exactly one
            // added option, shaped EXACTLY {slug, name} and nothing more (no pack slug/family/licence/
            // fonts member ever reaching it). This is the literal counterexample AC1's own wording
            // invites, pinned here rather than left for Story266_SpectatorSwitcher.cs's own factory
            // (which never seeds an owner theme at all) to ever catch.
            using var baselineThemesDoc = JsonDocument.Parse(baselineThemesJson);
            using var unwornThemesDoc = JsonDocument.Parse(unwornThemesJson);
            var baselineSlugs = OptionSlugs(baselineThemesDoc);
            var addedOptions = unwornThemesDoc.RootElement.GetProperty("options").EnumerateArray()
                .Where(option => !baselineSlugs.Contains(option.GetProperty("slug").GetString() ?? ""))
                .ToArray();
            Assert.True(
                addedOptions.Length == 1,
                $"expected exactly one newly-added spectator theme option, found {addedOptions.Length}");
            var addedOption = addedOptions[0];
            var addedOptionMembers = addedOption.EnumerateObject().Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(
                (Slug: WardrobeIsolationFixtures.RemixSlug, Name: "Wardrobe Audit Remix"),
                (Slug: addedOption.GetProperty("slug").GetString(), Name: addedOption.GetProperty("name").GetString()));
            Assert.True(
                addedOptionMembers.SequenceEqual(["name", "slug"]),
                $"added spectator theme option carries members [{string.Join(", ", addedOptionMembers)}], expected exactly [name, slug]");

            // When that SAME remix is then WORN — via the visitor cookie SpectatorThemeEndpoints
            // itself reads, the one legitimate way a visitor's own composed SHEET ever changes (SPEC
            // F102.5/F104.15),
            var wornClient = factory.CreateClient();
            wornClient.DefaultRequestHeaders.Add("Cookie", $"{ThemeCatalog.CookieName}={WardrobeIsolationFixtures.RemixSlug}");
            var wornCss = await ReadStringAsync(wornClient, "/spectator/theme.css");
            var faceResponse = await wornClient.GetAsync($"/fonts/{WardrobeIsolationFixtures.AssetFile}");

            // Then wearing it legitimately changes the sheet (guards this Fact against vacuously
            // comparing two accidentally-identical strings — mirrors Story278's own
            // TheLiveDbFixtureIsNotVacuous "don't fake it" precedent) —
            Assert.NotEqual(baselineCss, wornCss);

            // — the composed sheet carries the installed pack's OWN face src exactly the way a
            // vendored one would (SPEC F104.15's "reaching @font-face exactly as vendored faces do"),
            // that src round-trips through /fonts/{file} to real, servable bytes, and the sheet carries
            // no PACK SLUG (review finding N1: trimmed to what this Fact actually pins —
            // ThemeCssComposer structurally never composes licence/provenance into a stylesheet at
            // all, see that type's own remarks; there is nothing further here for THIS Fact to
            // assert).
            Assert.Equal(
                (CarriesTheInstalledSrc: true, FaceRoundTrips: HttpStatusCode.OK, PackSlugLeaks: false),
                (CarriesTheInstalledSrc: wornCss.Contains($"/fonts/{WardrobeIsolationFixtures.AssetFile}", StringComparison.Ordinal),
                 FaceRoundTrips: faceResponse.StatusCode,
                 PackSlugLeaks: wornCss.Contains(WardrobeIsolationFixtures.PackSlug, StringComparison.Ordinal)));
        }

        static async Task<string> ReadStringAsync(HttpClient client, string path) =>
            await (await client.GetAsync(path)).Content.ReadAsStringAsync();

        /// <summary>Every slug <c>/spectator/api/themes</c>' own <c>options</c> array already carried
        /// — the "before" half of review finding F1's "gains EXACTLY one option" claim.</summary>
        static HashSet<string> OptionSlugs(JsonDocument themesDoc) =>
            themesDoc.RootElement.GetProperty("options").EnumerateArray()
                .Select(option => option.GetProperty("slug").GetString() ?? "")
                .ToHashSet(StringComparer.Ordinal);
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// Story278_ThemeCatalogIsolation.cs's own <c>IsolationWebFactory</c> (spectator mode on,
/// <c>SimulatedPortStartupFilter</c> for the public-listener half) crossed with
/// Story286_EditorComposesTheRemix.cs's own "always swap in a FakeFontPackStore/FakeThemeStore, never
/// the real Postgres-backed repository against the deliberately-unreachable Host=nowhere connection
/// string" idiom — this file's own second Fact needs BOTH a working font-pack store (a real install)
/// AND a working theme store (a real save-as-own) in the SAME run, which no existing sibling factory
/// combines. Carries no swap-in-a-double constructor parameters (review finding N2 — YAGNI): no Fact
/// in this file has ever needed a caller-supplied store/handler, unlike Story278/283's own factories,
/// which genuinely do.
/// </summary>
file sealed class WardrobeIsolationWebFactory(int? simulatedPublicPort = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story289-wardrobe";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", WardrobeIsolationFixtures.IndexUrl);
        // SurfaceGateMiddleware's public-listener isolation check only engages once
        // Spectator:PublicPort is configured (mirrors Story278/Story248's own factories) — harmless
        // for every Fact here, since LocalPort only ever matches it when simulatedPublicPort's own
        // SimulatedPortStartupFilter stamps it.
        builder.UseSetting("Spectator:PublicPort", WardrobeIsolationFixtures.PublicPort.ToString());

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(WardrobeIsolationFixtures.BuildRoutedHandler()));

            services.RemoveAll<IFontPackStore>();
            services.AddSingleton<IFontPackStore>(new FakeFontPackStore());

            services.RemoveAll<IThemeStore>();
            services.AddSingleton<IThemeStore>(new FakeThemeStore());

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

/// <summary>Fixture documents for this file's own Facts — <c>file</c>-scoped, mirroring every other
/// Story2xx spec's "each file needs its own committed copy" idiom. One small font pack (mirrors
/// Story283_InstalledFontServing.cs's own InstalledFontServingFixtures — <c>AssetBytes</c> is
/// deliberately NOT a real woff2 binary, since GET /fonts/{file} never parses a face's payload, only
/// serves it) plus one remix manifest that references it alongside a real vendored face (real vendored
/// srcs — PLAN T188, SPEC F103.10 — since this file's own Facts POST through the production save-as-own
/// route, which enforces the widened font law).</summary>
file static class WardrobeIsolationFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/wardrobe-audit-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const int PublicPort = 8085;

    public const string PackSlug = "wardrobe-audit-pack";
    const string Family = "Wardrobe Audit Sans";
    public const string AssetFile = "wardrobe-audit-variable-latin.woff2";

    public static readonly byte[] AssetBytes = "installed face bytes for the STORY-289 re-audit (T209)"u8.ToArray();

    static string AssetSha256 => Sha256Hex(AssetBytes);

    static string ManifestJson => $$"""
        {"family":"{{Family}}","files":[{"role":"upright","file":"{{AssetFile}}","weight":"400","style":"normal","bytes":{{AssetBytes.Length}}}],"license":"OFL-1.1","sourceUrl":"https://example.test/wardrobe-audit","version":"1.0","subset":"text"}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A pack for the STORY-289 disclosure re-audit.","audience":"everyone","added":"2026-08-07"}
        """;

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-07", "entries": [
          { "slug": "{{PackSlug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{PackSlug}}/{{PackSlug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{PackSlug}}/{{PackSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{PackSlug}}/{{AssetFile}}", "sha256": "{{AssetSha256}}", "bytes": {{AssetBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + PackSlug + "/" + PackSlug + ".font.json"] = ManifestJson,
            [Directory + "entries/" + PackSlug + "/" + PackSlug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + PackSlug + "/" + AssetFile;

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

    public const string RemixSlug = "wardrobe-audit-remix";

    /// <summary>A valid remix: the display role stays vendored (Fraunces), the sans role points at
    /// <see cref="PackSlug"/>'s own installed face — the exact "base theme's palette plus a
    /// role-assigned installed face" shape the v2 editor's own Save-as-own composes (SPEC F104.11,
    /// F104.13).</summary>
    public static string RemixManifestJson => $$"""
        {
          "slug": "{{RemixSlug}}",
          "name": "Wardrobe Audit Remix",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "{{Family}}", "assets": [ { "src": "/fonts/{{AssetFile}}", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#5a3c8f", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;
}
