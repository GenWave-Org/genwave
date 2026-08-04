// STORY-265 — Selection and persistence (SPEC F102.5, F102.14, F102.15)
//
// BDD specification — xUnit. Precedence, highest first:
//     visitor cookie → Station:Theme settings row → Station:Theme env default → shipped default
// and an unresolvable slug falls back at EVERY level rather than erroring.
//
// Station:Theme is an allowlisted LIVE setting presented as a closed CHOICE of shipped
// slugs. That needs a new SettingKind.Choice — today's kinds are Boolean/Number/NumberList/
// String only, and String would render a free-text box where a typo silently yields an
// unresolvable slug. A UI special-case was rejected: it breaks the allowlist's
// self-describing contract.
//
// ⚠️ Selection inherits the Station:SpectatorMode trap — a saved settings row silently
// outranks the env value forever. That is how a pinned demo box's theme "won't change".
//
// T163 implements ScenarioTheSettingPresentsAsAClosedChoice (AC6): SettingKind.Choice, the
// Station:Theme allowlist entry, and its Choices sourced from ThemeCatalog.
//
// T164 implements ThemeCatalog.Resolve (AC1-AC4, AC8-AC10) — the ONE precedence cascade both
// theme endpoints now call. "Settings row outranks env default" (AC2/AC3/AC8's precedence half)
// is proven by layering two IConfiguration providers the SAME order Program.cs's own
// StationSettingsHostingExtensions does (DB overlay registered AFTER env/appsettings) — Resolve
// itself never distinguishes the two; it only ever sees ONE already-merged station value. PENDING
// T165 (the live-wire acceptance against a running stack) stays skipped.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Theming;

namespace GenWave.Host.Tests.Specs;

public static class FeatureThemeSelectionAndPersistence
{
    const string PendingWire = "Pending T165 — see docs/PLAN.md";

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioTheVisitorCookieOutranksTheStationSetting
    {
        [Fact]
        public void TheCookiesThemeIsResolved()
        {
            // Arrange: a station setting naming one theme, a visitor cookie naming another.
            var catalog = ThemeSelectionFixtures.TwoThemeCatalog();

            // Act: resolve the theme for that visitor.
            var resolved = catalog.Resolve(
                cookieSlug: ThemeSelectionFixtures.AlternateSlug,
                stationSlug: ThemeCatalog.ShippedDefaultSlug);

            // Assert: the cookie's theme wins (AC1).
            Assert.Equal(ThemeSelectionFixtures.AlternateSlug, resolved.Slug);
        }
    }

    public sealed class ScenarioTheSettingsRowOutranksTheEnvDefault
    {
        [Fact]
        public void TheSettingsRowsThemeIsResolved()
        {
            // Arrange: an env default naming one theme, a saved settings row naming another —
            //          two IConfiguration providers layered in the same order Program.cs uses
            //          (the DB overlay is registered AFTER env/appsettings, so its value wins).
            var catalog = ThemeSelectionFixtures.TwoThemeCatalog();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Theme"] = ThemeCatalog.ShippedDefaultSlug, // env default
                })
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Theme"] = ThemeSelectionFixtures.AlternateSlug, // settings row, registered after
                })
                .Build();

            // Act: resolve with no visitor cookie.
            var resolved = catalog.Resolve(cookieSlug: null, stationSlug: config["Station:Theme"]);

            // Assert: the settings row wins (AC2).
            Assert.Equal(ThemeSelectionFixtures.AlternateSlug, resolved.Slug);
        }
    }

    public sealed class ScenarioTheEnvDefaultOutranksTheShippedDefault
    {
        [Fact]
        public void TheEnvDefaultsThemeIsResolved()
        {
            // Arrange: an env default naming a theme, no saved settings row.
            var catalog = ThemeSelectionFixtures.TwoThemeCatalog();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Theme"] = ThemeSelectionFixtures.AlternateSlug,
                })
                .Build();

            // Act: resolve with no visitor cookie.
            var resolved = catalog.Resolve(cookieSlug: null, stationSlug: config["Station:Theme"]);

            // Assert: the env default wins (AC3). This is how a pinned demo box gets its look.
            Assert.Equal(ThemeSelectionFixtures.AlternateSlug, resolved.Slug);
        }
    }

    public sealed class ScenarioTheShippedDefaultIsTheFloor
    {
        [Fact]
        public void TheShippedDefaultThemeIsResolved()
        {
            // Arrange: no cookie, no settings row, no env default — the real shipped catalog,
            //          so ShippedDefaultSlug is genuinely present to fall back to.
            var catalog = ThemeCatalog.LoadShipped();

            // Act: resolve the theme. Empty string mirrors what IOptionsMonitor<StationOptions>
            //      actually hands Resolve when nothing configures Station:Theme anywhere
            //      (StationOptions.Theme defaults to string.Empty, never null).
            var resolved = catalog.Resolve(cookieSlug: null, stationSlug: string.Empty);

            // Assert: the shipped default is resolved (AC4).
            Assert.Equal(ThemeCatalog.ShippedDefaultSlug, resolved.Slug);
        }
    }

    public sealed class ScenarioTheSettingIsLive
    {
        [Fact(Skip = PendingWire)]
        public void TheNextRequestServesTheNewThemeWithNoRestart()
        {
            // Arrange: a running api.
            // Act:     change Station:Theme via PUT /api/settings.
            // Assert:  the very NEXT request serves the new theme, with no api restart
            //          (AC5) — the SettingApplyMode.Live contract, same shape as
            //          Station:PublicStreamUrl.
            Assert.Fail("pending T165 — live setting wire acceptance");
        }
    }

    public sealed class ScenarioTheSettingPresentsAsAClosedChoice
    {
        // Given the settings surface describing Station:Theme (AC6) — a real SettingsController,
        // no live stack or DB required (same in-process pattern as Story100/Story120/Story124).

        sealed class FakeSettingsStore : IStationSettingsStore
        {
            public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("this scenario only reads the settings surface");

            public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }

        static async Task<SettingDto> GetStationThemeSetting()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Theme"] = ThemeCatalog.ShippedDefaultSlug,
                })
                .Build();
            var controller = new SettingsController(
                config, new FakeSettingsStore(), new SettingValidator(config), NullLogger<SettingsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            var ok = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            return items.Single(i => i.Key.Equals("Station:Theme", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ItsKindIsAChoiceNotFreeText()
        {
            // When its kind is read, Then it is a choice, not free text (AC6). Requires
            // SettingKind.Choice — Boolean/Number/NumberList/String are today's other kinds.
            var theme = await GetStationThemeSetting();

            Assert.Equal("choice", theme.Kind);
        }

        [Fact]
        public async Task ItsChoicesAreExactlyTheShippedThemeSlugs()
        {
            // Then it is a choice OVER THE SHIPPED SLUGS (AC6) — sourced from ThemeCatalog, the
            // same catalog the theme.css endpoints resolve against, so a typo cannot produce a
            // choice this setting will accept but no theme will ever resolve.
            var theme = await GetStationThemeSetting();

            var shippedSlugs = ThemeCatalog.LoadShipped().All.Select(t => t.Slug).ToList();
            Assert.Equal(shippedSlugs, theme.Choices);
        }
    }

    public sealed class ScenarioSelectionWorksOnAnUnadministrableBox
    {
        [Fact(Skip = PendingWire)]
        public void TheEnvSeededThemeIsServedWithNoAdminSurfaceReachable()
        {
            // Arrange: a station running with Admin:Enabled=false and an env-seeded
            //          Station:Theme (the compose.demo.yaml Station__* pattern).
            // Act:     serve the spectator page.
            // Assert:  it renders the env-seeded theme, with no admin surface reachable
            //          (AC7). The demo box pins its look this way.
            Assert.Fail("pending T165 — appliance-mode wire acceptance");
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioASavedRowSilentlyOutranksTheEnvValue
    {
        sealed class FakeSettingsStoreWithThemeOverride : IStationSettingsStore
        {
            public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("this scenario only reads the settings surface");

            public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string> { ["Station:Theme"] = ThemeSelectionFixtures.AlternateSlug });
        }

        [Fact]
        public async Task TheSavedRowWinsAndItsSourceIsReportable()
        {
            // Arrange: an env-seeded Station:Theme plus a settings row saved earlier naming
            //          a different theme — same provider layering as AC2, plus a settings-store
            //          double reporting that same row as an override (SettingsController.Get's
            //          own source computation: overrideKeys.ContainsKey(key) ? "override" : "default").
            var catalog = ThemeSelectionFixtures.TwoThemeCatalog();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Theme"] = ThemeCatalog.ShippedDefaultSlug, // env-seeded
                })
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Station:Theme"] = ThemeSelectionFixtures.AlternateSlug, // saved row, registered after
                })
                .Build();

            // Act: resolve the theme.
            var resolved = catalog.Resolve(cookieSlug: null, stationSlug: config["Station:Theme"]);

            // Assert: the saved row wins (AC8's precedence half — same mechanism as AC2).
            Assert.Equal(ThemeSelectionFixtures.AlternateSlug, resolved.Slug);

            // Assert: AND the setting's reported source makes that diagnosable (AC8). Without
            //         this, "the env var didn't take" is indistinguishable from "a row is
            //         winning" — the Station:SpectatorMode gotcha class DEPLOYMENT.md documents.
            var controller = new SettingsController(
                config, new FakeSettingsStoreWithThemeOverride(), new SettingValidator(config), NullLogger<SettingsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            var ok = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            var themeSetting = items.Single(i => i.Key.Equals("Station:Theme", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("override", themeSetting.Source);
        }
    }

    public sealed class ScenarioRejectingUnresolvableSlugs
    {
        [Fact]
        public void AnUnknownSlugInTheSettingFallsBackToTheShippedDefault()
        {
            // Arrange: Station:Theme naming a slug no shipped theme matches.
            var catalog = ThemeCatalog.LoadShipped();

            // Act: resolve the theme.
            var resolved = catalog.Resolve(cookieSlug: null, stationSlug: "no-such-theme");

            // Assert: the shipped default is resolved rather than an error (AC9).
            Assert.Equal(ThemeCatalog.ShippedDefaultSlug, resolved.Slug);
        }

        [Fact]
        public void AnUnknownSlugInTheCookieFallsBackToTheStationTheme()
        {
            // Arrange: a visitor cookie naming a slug no shipped theme matches, and a station
            //          theme that is deliberately NOT the shipped default — so landing on it
            //          (rather than the shipped default) is provable.
            var catalog = ThemeSelectionFixtures.TwoThemeCatalog();

            // Act: resolve the theme for that visitor.
            var resolved = catalog.Resolve(cookieSlug: "removed-theme", stationSlug: ThemeSelectionFixtures.AlternateSlug);

            // Assert: the STATION's theme is resolved (AC10) — not the shipped default, and
            //         not an error. A stale cookie from a removed theme must not strand a
            //         visitor away from what the station chose.
            Assert.Equal(ThemeSelectionFixtures.AlternateSlug, resolved.Slug);
        }
    }
}

/// <summary>Raw theme manifest fixtures local to this spec file, mirroring
/// Story263_ThemesBecomeData.cs's ThemeFixtures / Story264_ComposedStylesheet.cs's
/// ComposerFixtures — this file is the only one T164 is scoped to touch, so its fixtures live
/// here rather than a shared Fakes/ helper.</summary>
static class ThemeSelectionFixtures
{
    /// <summary>An alternate theme's slug — distinct from <see cref="ThemeCatalog.ShippedDefaultSlug"/>
    /// so a precedence spec can prove resolution landed on ONE named theme and not the other, not
    /// merely that resolution didn't throw. Not a real shipped theme — Ship 1 ships exactly one
    /// (PLAN's own "F102.1 knowingly unmet" note; T171 lands the rest) — fabricated the same way
    /// ComposerFixtures/ThemeFixtures fabricate theirs.</summary>
    public const string AlternateSlug = "test-alt-theme";

    /// <summary>A two-theme catalog: the real <see cref="ThemeCatalog.ShippedDefaultSlug"/> plus
    /// <see cref="AlternateSlug"/> — everything this file's precedence specs need to tell "the
    /// station's theme" and "the cookie's theme" apart.</summary>
    public static ThemeCatalog TwoThemeCatalog()
    {
        var shipped = new ThemeManifestSource(
            $"{ThemeCatalog.ShippedDefaultSlug}.json", ValidManifestJson(ThemeCatalog.ShippedDefaultSlug));
        var alternate = new ThemeManifestSource($"{AlternateSlug}.json", ValidManifestJson(AlternateSlug));
        return ThemeCatalog.Load([shipped, alternate]);
    }

    static string ValidManifestJson(string slug) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Test Theme",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;
}
