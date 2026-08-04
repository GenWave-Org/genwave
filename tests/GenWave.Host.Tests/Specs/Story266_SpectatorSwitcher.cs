// STORY-266 — Spectator switcher (SPEC F102.9, F102.10, F102.10a, F102.11)
//
// BDD specification — xUnit. The public page gains its FIRST interactive chrome. F63's
// original ruling was "no theme toggle here (no interactive chrome for one)"; this
// deliberately overturns it, so the constraints below are acceptance criteria rather than
// notes.
//
// Three hard boundaries the switcher must not cross:
//   * F63.2/F102.10 — the page calls only /spectator/api/*. Learning the theme list is a
//     dedicated READ, GET /spectator/api/themes (F102.10a) — the one thing the original
//     "adds no new network call" wording over-generalized (amended 2026-08-04). Persisting
//     the choice stays a client-side cookie write; there is still no WRITE surface.
//   * script-src 'self' — external same-origin script, no inline handlers.
//   * style-src 'self' — no inline <style>. gh-#180's asserted CSP header must come out
//     BYTE-IDENTICAL; Gh180_SpectatorSecurityHeaders already pins it and must stay green.
//
// Client-rendered behaviour (restyle without reload, persistence across a reload, the
// cookie-refused path) is browser-verified against the running compose stack at T169,
// following Story173's precedent — enumerated here so the contract lives in one place.
//
// T166 lands the switcher itself (index.html/switcher.js, plus the new GET
// /spectator/api/themes read) and unskips every spec below except the three browser-gated
// ones T169 owns. index.html stays a byte-for-byte static file — nothing here is templated.

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Theming;
using GenWave.Tts;
using Xunit;

namespace GenWave.Host.Tests.Specs;

/// <summary>Mirrors Story173's/Gh180's own <c>WebApplicationFactory</c> setup exactly — the same
/// spectator-mode-on, faked-dependencies rig every other spectator-page spec file uses.</summary>
file sealed class SpectatorSwitcherWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
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

public static class FeatureSpectatorSwitcher
{
    const string BrowserGated =
        "Client-rendered behavior — verified in a real browser against the compose stack (PLAN T169 acceptance).";

    /// <summary>The page plus every same-origin asset it references (src/href), as raw text —
    /// Story173's own <c>FetchPageBundleAsync</c> idiom, copied here rather than shared (each
    /// spec file owns its own private fixtures by this codebase's convention).</summary>
    static async Task<IReadOnlyList<(string Path, string Content)>> FetchPageBundleAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/spectator");
        var bundle = new List<(string, string)> { ("/spectator", html) };

        var documentUri = new Uri(client.BaseAddress!, "/spectator");

        foreach (Match match in Regex.Matches(html, @"(?:src|href)\s*=\s*""([^""]+)"""))
        {
            var reference = match.Groups[1].Value;
            if (reference.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            var path = new Uri(documentUri, reference).AbsolutePath;
            var asset = await client.GetAsync(path);
            if (asset.IsSuccessStatusCode)
                bundle.Add((path, await asset.Content.ReadAsStringAsync()));
        }
        return bundle;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioThePageOffersASwitcher
    {
        [Fact]
        public async Task ASwitcherIsPresentInTheServedMarkup()
        {
            // Arrange: the spectator page is served.
            await using var factory = new SpectatorSwitcherWebFactory();
            var client = factory.CreateClient();

            // Act: render it.
            var html = await client.GetStringAsync("/spectator");

            // Assert: a theme switcher is present (AC1) — the theme (palette) picker and the
            //          light/dark mode toggle, both addressable.
            Assert.Contains("id=\"theme-switcher\"", html, StringComparison.Ordinal);
            Assert.Contains("id=\"theme-select\"", html, StringComparison.Ordinal);
            Assert.Contains("id=\"mode-toggle\"", html, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioChoosingAppliesWithoutAReload
    {
        [Fact(Skip = BrowserGated)]
        public void ThePageRestylesWithoutANavigation()
        {
            // Arrange: the spectator page rendered in a real browser.
            // Act:     a visitor picks a different theme.
            // Assert:  the page restyles with no navigation or reload (AC2).
            Assert.Fail("browser-gated — T169");
        }
    }

    public sealed class ScenarioTheChoicePersistsForThatVisitor
    {
        [Fact(Skip = BrowserGated)]
        public void TheChosenThemeSurvivesAReload()
        {
            // Arrange: a visitor picked a theme.
            // Act:     load the page again.
            // Assert:  their chosen theme is applied (AC3).
            Assert.Fail("browser-gated — T169");
        }
    }

    public sealed class ScenarioNoNewNetworkCallIsAdded
    {
        [Fact]
        public async Task EverySameOriginReferenceStaysWithinTheSpectatorSurface()
        {
            // Arrange: the spectator page with the switcher present.
            await using var factory = new SpectatorSwitcherWebFactory();
            var client = factory.CreateClient();

            // Act: collect the page bundle's same-origin references and any fetch() targets.
            var bundle = await FetchPageBundleAsync(client);
            var fetchTargets = bundle
                .SelectMany(item => Regex.Matches(item.Content, @"fetch\(\s*""([^""]+)""").Cast<Match>())
                .Select(match => match.Groups[1].Value)
                .ToList();

            // Assert: the page still calls only /spectator/api/* routes (AC4, upholding
            //          F63.2/F102.10). Learning the theme list is a READ within that surface —
            //          GET /spectator/api/themes — never the admin API; persisting the choice
            //          is still a client-side cookie write, never a request.
            Assert.NotEmpty(fetchTargets); // sanity: app.js's own polling calls are still there
            Assert.All(fetchTargets, target =>
                Assert.StartsWith("/spectator/api/", target, StringComparison.Ordinal));
            Assert.Contains(fetchTargets, target => target == "/spectator/api/themes");

            // Assert: AND no asset references the admin API surface either — same check
            //          Story173's ScenarioSpectatorApiOnly pins for the pre-switcher bundle,
            //          restated here so a regression in the switcher's OWN script (switcher.js)
            //          is caught by this file too, not only by coincidence of Story173 rerunning.
            Assert.All(bundle, item =>
                Assert.DoesNotContain("\"/api/", item.Content, StringComparison.Ordinal));
        }
    }

    public sealed class ScenarioTheSwitcherScriptIsSameOriginAndExternal
    {
        [Fact]
        public async Task NoInlineEventHandlerAppearsInTheMarkup()
        {
            // Arrange: the spectator page markup.
            await using var factory = new SpectatorSwitcherWebFactory();
            var client = factory.CreateClient();

            // Act: inspect it.
            var html = await client.GetStringAsync("/spectator");

            // Assert: the switcher's behaviour comes from an external same-origin script
            //          with NO inline handler (AC5) — script-src 'self' grants no
            //          'unsafe-inline', so an onclick would simply not fire.
            Assert.DoesNotMatch(new Regex(@"\son\w+\s*=", RegexOptions.IgnoreCase), html);
        }
    }

    public sealed class ScenarioNoInlineStyleIsIntroduced
    {
        [Fact]
        public async Task NoInlineStyleBlockAppearsInTheMarkup()
        {
            // Arrange: the spectator page markup.
            await using var factory = new SpectatorSwitcherWebFactory();
            var client = factory.CreateClient();

            // Act: inspect it.
            var html = await client.GetStringAsync("/spectator");

            // Assert: it carries no inline <style> block, nor an inline style="" attribute
            //          (AC6) — style-src 'self' grants no 'unsafe-inline' for either shape. This
            //          is why theme tokens are SERVED as stylesheets (styles.css, theme.css)
            //          rather than inlined into <head> or onto an element.
            Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(new Regex(@"\sstyle\s*=", RegexOptions.IgnoreCase), html);
        }
    }

    public sealed class ScenarioTheThemesEndpointReturnsTheShippedCatalog
    {
        [Fact]
        public async Task TheResponseIsExactlyActiveAndOptionsOfSlugAndName()
        {
            // Arrange: no cookie, no Station:Theme override — the shipped default resolves.
            await using var factory = new SpectatorSwitcherWebFactory();
            var client = factory.CreateClient();

            // Act: GET /spectator/api/themes.
            var response = await client.GetAsync("/spectator/api/themes");
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // Assert: exactly {active, options:[{slug, name}]} — nothing else at either level
            //          (SPEC F102.10a; audited by Story183's disclosure contract too).
            Assert.Equal(["active", "options"],
                root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(ThemeCatalog.ShippedDefaultSlug, root.GetProperty("active").GetString());

            var options = root.GetProperty("options").EnumerateArray().ToList();
            Assert.NotEmpty(options);
            Assert.All(options, option =>
                Assert.Equal(["name", "slug"],
                    option.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
            Assert.Contains(options, option =>
                option.GetProperty("slug").GetString() == ThemeCatalog.ShippedDefaultSlug);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheSecurityHeaderIsUnchanged
    {
        // Duplicated from Gh180_SpectatorSecurityHeaders.FeatureSpectatorSecurityHeaders's own
        // (private-to-that-file) DefaultCsp rather than referenced — reuse would need widening
        // that constant's accessibility, which is outside this task's owned files. Gh180 stays
        // the single ENFORCEMENT point (it already fails first on any drift); restating the
        // value here only states the switcher epic's OWN intent explicitly, per this file's own
        // header remarks.
        const string ExpectedCsp =
            "default-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; " +
            "script-src 'self'; style-src 'self'; font-src 'self'; " +
            "img-src 'self'; media-src 'self'; connect-src 'self'";

        [Fact]
        public async Task TheContentSecurityPolicyIsByteIdenticalToTheAssertedPolicy()
        {
            // Arrange: the spectator security headers after the switcher lands.
            await using var factory = new SpectatorSwitcherWebFactory();
            var client = factory.CreateClient();

            // Act: read the Content-Security-Policy.
            var response = await client.GetAsync("/spectator");
            var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

            // Assert: byte-identical to gh-#180's asserted policy — style-src, script-src
            //          and font-src gain no 'unsafe-inline' and no new host (AC7).
            //          Gh180_SpectatorSecurityHeaders pins the header today and must remain
            //          green; this spec states the intent explicitly for the theme epic so a
            //          future reader sees WHY the policy is load-bearing here. connect-src
            //          'self' already permits the new GET /spectator/api/themes fetch.
            Assert.Equal(ExpectedCsp, csp);
        }
    }

    public sealed class ScenarioAVisitorWhoCannotStoreAChoiceStillGetsAPage
    {
        [Fact(Skip = BrowserGated)]
        public void ThePageRendersTheStationThemeAndTheSwitcherDoesNotBreakIt()
        {
            // Arrange: a visitor whose browser rejects the cookie.
            // Act:     render the page.
            // Assert:  it renders the station's theme and the switcher does not break the
            //          page (AC8). A refused cookie is a preference that cannot persist, not
            //          an error state.
            Assert.Fail("browser-gated — T169");
        }
    }
}
