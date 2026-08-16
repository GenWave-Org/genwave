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
// itself never distinguishes the two; it only ever sees ONE already-merged station value.
//
// T165 (this task) resolves the two PENDING facts differently, per their own shape:
//   - AC7 (appliance mode) needs no live database — Admin:Enabled and an env-seeded Station:Theme
//     are both config-layer concerns, exactly like Story166/Story170's own env-seeded facts — so it
//     is now a real, always-run WebApplicationFactory fact.
//   - AC5 (the live setting) genuinely needs the real Postgres-backed settings overlay AND, because
//     Ship 1 ships exactly one theme, a second theme to prove the served BODY (not just a header)
//     changed — both true-"live stack" concerns this suite cannot fake without either standing up
//     Postgres or smuggling a second theme into the shipped catalog (T171's job, and T158's AA gate
//     would then have to cover it). It stays Skip'd + Category=Integration, mirroring Story170's own
//     "Live PUT round trip requires the real Postgres settings overlay" split — see its own Skip
//     message for the exact operator procedure this task ran by hand against a live stack.
//
// A separate spec (Story265_ProviderOrderingGuard.cs) pins the provider registration order T164's
// review flagged: StationSettingsHostingExtensions must register the DB overlay AFTER
// AddEnvironmentVariables(), or a saved settings row would silently lose to an env default.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Theming;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>AC7's rig — an appliance-mode stack, compose.demo.yaml's own shape (<c>Admin__Enabled:
/// "false"</c> plus a <c>Station__*</c> env seed): mirrors Story166's <c>KillSwitchWebFactory</c> and
/// Story170's <c>SpectatorAboutWebFactory</c>, the SAME <c>UseSetting</c> idiom both already use for
/// this exact class of claim ("an env-seeded value reaches the served page"). The
/// <see cref="ThemeCatalog"/> singleton is swapped for <see cref="ThemeSelectionFixtures.TwoThemeCatalog"/>
/// — a TEST FIXTURE, not the shipped shelf (Ship 1 ships exactly one theme) — so the env-seeded slug
/// resolves to a theme whose composed CSS is provably distinct, not coincidentally identical to the
/// shipped default.</summary>
file sealed class ApplianceModeWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "false");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("Station:Theme", ThemeSelectionFixtures.AlternateSlug);
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<ThemeCatalog>();
            services.AddSingleton(ThemeSelectionFixtures.TwoThemeCatalog());
        });
    }
}

public static class FeatureThemeSelectionAndPersistence
{
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
        // A live PUT genuinely round-tripping needs the real Postgres-backed settings overlay —
        // Story170's own precedent for the identical "SettingApplyMode.Live, same shape as
        // Station:PublicStreamUrl" claim ("Live PUT round trip requires the real Postgres settings
        // overlay — proven in the operator acceptance gate ... not under WebApplicationFactory").
        // AC5 additionally needs a SECOND theme to prove the served BODY changed (not merely an
        // ETag) — and Ship 1 ships exactly one (PLAN's own "F102.1 knowingly unmet" note; T171
        // lands the rest). Manufacturing that second theme as a real embedded manifest would leak a
        // fixture into the shipped shelf AND T158's AA gate, which this task is explicitly told not
        // to do — so this stays a live-stack, by-hand verification rather than a faked in-process one.
        const string OperatorGated =
            "AC5 originally had TWO blockers of different kinds; PLAN T183 resolved one of them. " +
            "(a) RESOLVED (T183): StationSettingsAllowlist/SettingValidator now source Station:Theme's " +
            "choices/acceptance from the DI-registered ThemeCatalog at request time (not a frozen " +
            "shipped-only snapshot), so a WebApplicationFactory test swapping the DI ThemeCatalog " +
            "singleton for ThemeSelectionFixtures.TwoThemeCatalog() (as ApplianceModeWebFactory " +
            "already does) can PUT the fixture's alternate slug and have it validate — no real " +
            "second SHIPPED theme is needed to prove this half. (b) STILL PERMANENT, absent new " +
            "test infrastructure: the real Postgres-backed settings overlay, because AC5 asserts a " +
            "settings ROW write. There is no Testcontainers/Respawn/DB fixture anywhere in tests/, " +
            "and every workflow runs --filter \"Category!=Integration\" — this is why the Fact stays " +
            "Skip'd even though (a) no longer blocks it. (Mirrors Story170's identical " +
            "Station:PublicStreamUrl split — note Story170 uses Skip alone without the Integration " +
            "trait, so its gated spec still shows in the filtered skip count where this one is " +
            "excluded outright.) " +
            "Operator procedure (run against `BUILD=1 ./launch.sh`, PLAN T165): " +
            "(1) temporarily add a second embedded manifest under " +
            "src/GenWave.Host/Theming/themes/ (any valid slug/tokens, distinct --bg) — a TEST " +
            "FIXTURE only, never committed; (2) `BUILD=1 ./launch.sh` to bake it in; (3) note " +
            "GET /api/status's startedAt; (4) `PUT /api/settings` [{\"key\":\"Station:Theme\"," +
            "\"value\":\"<fixture-slug>\"}] (cookie-authed); (5) `GET /spectator/theme.css` twice — " +
            "before/after — and diff the BODY (not just the ETag) to confirm it actually changed; " +
            "(6) `GET /api/status` again and confirm startedAt is byte-identical (no restart); " +
            "(7) delete the fixture manifest, `git status` clean, `BUILD=1 ./launch.sh` again so the " +
            "delivered image ships only cats-whisker.";

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void TheNextRequestServesTheNewThemeWithNoRestart()
        {
            // Arrange: a running api.
            // Act:     change Station:Theme via PUT /api/settings.
            // Assert:  the very NEXT request serves the new theme, with no api restart
            //          (AC5) — the SettingApplyMode.Live contract, same shape as
            //          Station:PublicStreamUrl.
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
                config, new FakeSettingsStore(), new SettingValidator(config), NullLogger<SettingsController>.Instance,
                new FakeIconPackStore())
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
            // Then it is a choice OVER THE SHIPPED SLUGS, each paired with its display label
            // (AC6; T175) — sourced from ThemeCatalog, the same catalog the theme.css endpoints
            // resolve against, so a typo cannot produce a choice this setting will accept but no
            // theme will ever resolve, and the admin UI never has to invent display copy the
            // manifest doesn't already carry. Also proves IsDefault (T175 follow-up #1) is set for
            // EXACTLY the shipped-default slug's own entry, by record equality against a list built
            // the same explicit-slug-match way StationSettingsAllowlist itself builds it.
            var theme = await GetStationThemeSetting();

            var expectedChoices = ThemeCatalog.LoadShipped().All
                .Select(t => new SettingChoice(t.Slug, t.Name, t.Slug == ThemeCatalog.ShippedDefaultSlug))
                .ToList();
            Assert.Equal(expectedChoices, theme.Choices);
        }

        [Fact]
        public async Task ExactlyOneChoiceIsFlaggedAsTheDefaultAndItIsTheShippedOne()
        {
            // Then exactly one choice carries IsDefault (T175 follow-up #1) — the admin UI's
            // ChoiceSettingControl relies on this to label an empty Station:Theme value as "Station
            // default (<name>)" instead of silently matching whatever choice sorts first.
            var theme = await GetStationThemeSetting();

            var defaultChoices = theme.Choices!.Where(c => c.IsDefault).ToList();
            var defaultChoice = Assert.Single(defaultChoices);
            Assert.Equal(ThemeCatalog.ShippedDefaultSlug, defaultChoice.Value);
        }
    }

    public sealed class ScenarioSelectionWorksOnAnUnadministrableBox
    {
        [Fact]
        public async Task TheEnvSeededThemeIsServedWithNoAdminSurfaceReachable()
        {
            // Arrange: a station running with Admin:Enabled=false and an env-seeded Station:Theme —
            //          ApplianceModeWebFactory's own UseSetting calls mirror the compose.demo.yaml
            //          Station__*/Admin__Enabled env pattern exactly (Story166/Story170's own
            //          idiom for this class of claim, no live database needed).
            await using var factory = new ApplianceModeWebFactory();
            var client = factory.CreateClient();

            // Act: serve the spectator surface's composed stylesheet.
            var themeResponse = await client.GetAsync("/spectator/theme.css");
            var css = await themeResponse.Content.ReadAsStringAsync();

            // Assert: it renders the env-seeded theme (AC7) — the ALTERNATE fixture theme's own
            //         --bg token, not merely "some theme" or coincidentally the shipped default
            //         (PLAN T165's own "don't fake it" warning).
            Assert.Equal(HttpStatusCode.OK, themeResponse.StatusCode);
            Assert.Contains($"--bg: {ThemeSelectionFixtures.AlternateLightBg};", css, StringComparison.Ordinal);

            // Assert: AND no admin surface is reachable (AC7/F61.2) — the plane does not exist, 404
            //         not 401. Story166 already proves this exhaustively across every /api/* route;
            //         these two are representative enough to prove theme selection specifically
            //         survives the kill switch, not a re-run of that full sweep.
            foreach (var adminRoute in new[] { "/api/theme.css", "/api/settings" })
            {
                var response = await client.GetAsync(adminRoute);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
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
                config, new FakeSettingsStoreWithThemeOverride(), new SettingValidator(config), NullLogger<SettingsController>.Instance,
                new FakeIconPackStore())
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

    /// <summary>The alternate theme's light-mode <c>--bg</c> value — deliberately a colour that
    /// appears nowhere in any shipped manifest, so a spec asserting on it proves the composed sheet
    /// carries THIS theme's own tokens, not merely a byte-identical sheet under a different slug
    /// (PLAN T165's own warning: "a test that passes because both themes are the same CSS proves
    /// nothing"). Paired with <see cref="ShippedSlugLightBg"/> in <see cref="TwoThemeCatalog"/> so
    /// the two fixture themes' composed CSS genuinely differ.</summary>
    public const string AlternateLightBg = "#4a1c8c";

    /// <summary>The shipped-slug fixture's own light-mode <c>--bg</c> value — see
    /// <see cref="AlternateLightBg"/>'s own remarks. Unrelated to the REAL shipped
    /// <c>cats-whisker</c> manifest's actual token values: this fixture is loaded via
    /// <see cref="ThemeCatalog.Load"/>, never <see cref="ThemeCatalog.LoadShipped"/>, so it never
    /// reads (or could drift against) the real embedded resource.</summary>
    public const string ShippedSlugLightBg = "#f6efe3";

    /// <summary>A two-theme catalog: the real <see cref="ThemeCatalog.ShippedDefaultSlug"/> plus
    /// <see cref="AlternateSlug"/> — everything this file's precedence specs need to tell "the
    /// station's theme" and "the cookie's theme" apart. The two themes carry genuinely different
    /// light <c>--bg</c> tokens (<see cref="ShippedSlugLightBg"/>/<see cref="AlternateLightBg"/>) so
    /// a scenario resolving one over the other can prove the composed CSS BODY differs, not just the
    /// resolved <see cref="ThemeManifest.Slug"/>.</summary>
    public static ThemeCatalog TwoThemeCatalog()
    {
        var shipped = new ThemeManifestSource(
            $"{ThemeCatalog.ShippedDefaultSlug}.json",
            ValidManifestJson(ThemeCatalog.ShippedDefaultSlug, ShippedSlugLightBg));
        var alternate = new ThemeManifestSource(
            $"{AlternateSlug}.json", ValidManifestJson(AlternateSlug, AlternateLightBg));
        return ThemeCatalog.Load([shipped, alternate]);
    }

    static string ValidManifestJson(string slug, string lightBg) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Test Theme",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "{{lightBg}}", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;
}
