// STORY-275 — Imported-theme provenance (SPEC F103.11)
//
// BDD specification — xUnit. GET /api/settings is the read path this task adds (StationSettingsAllowlist
// .ThemeChoices, PLAN T187): each Station:Theme SettingChoice now also carries importedFrom/importedAt,
// sourced from ThemeCatalog.Entries (review F3's minimal ThemeProvenance carrier, walked alongside the
// manifest it loaded from) — null for a shipped default, stamped for a catalog- or file-imported one.
// The admin UI's Settings page (SettingsForm) reads this to list every imported choice's own row,
// "<label> — Imported · <source> · <date>" (the station.persona/db-25 pattern, F90.7); a shipped default
// carries no owner row and therefore no stamp. No new endpoint — this widens the existing GET
// /api/settings projection the same way T183 widened its Choices list.
//
// WIRED — every Fact below drives the real production route through WebApplicationFactory<Program>
// (real routing/auth pipeline; IThemeStore replaced with a scriptable fake, no live Postgres), mirroring
// Story272_ThemeImport.cs's own ThemeImportWebFactory (a `file`-scoped type there, so this file carries
// its own minimal copy rather than reaching across files). One assertion focus per Fact.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> bringing up the real HTTP pipeline for
/// <c>GET/POST /api/themes/{slug}/import</c> and <c>GET /api/settings</c> — mirrors
/// <c>Story272_ThemeImport.cs</c>'s own <c>ThemeImportWebFactory</c> (that type is <c>file</c>-scoped,
/// so this is a deliberate, minimal copy, not a duplicate source of truth: both configure the exact
/// same production DI substitution, <see cref="IThemeStore"/> only). <c>ThemeCatalog</c> is left as
/// <c>Program.cs</c>'s own real <c>CreateForStation</c> registration for the same reason that file's
/// remarks give — it resolves <see cref="IThemeStore"/> lazily, so it picks up whichever fake this
/// factory installs with no further wiring.
/// </summary>
file sealed class ThemeProvenanceWebFactory(FakeThemeStore? themeStore = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    readonly FakeThemeStore store = themeStore ?? new FakeThemeStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IThemeStore>();
            services.AddSingleton<IThemeStore>(store);
        });
    }
}

// ── Fixture ────────────────────────────────────────────────────────────────────────────────────────

file static class ThemeProvenanceFixture
{
    public static string ValidManifestJson(string slug, string name = "Test Theme") => $$"""
        {
          "slug": "{{slug}}",
          "name": "{{name}}",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#2a5c9e", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureThemeProvenanceBadge
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ThemeProvenanceWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static Task<HttpResponseMessage> PostManifestAsync(
        HttpClient client, string slug, string json, string? catalogSlug = null)
    {
        var uri = catalogSlug is null ? $"/api/themes/{slug}/import" : $"/api/themes/{slug}/import?catalogSlug={catalogSlug}";
        return client.PostAsync(uri, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    static async Task<SettingChoice> StationThemeChoiceAsync(HttpClient client, string slug)
    {
        var settings = await client.GetFromJsonAsync<IReadOnlyList<SettingDto>>("/api/settings");
        var themeSetting = settings!.Single(s => s.Key.Equals("Station:Theme", StringComparison.OrdinalIgnoreCase));
        return themeSetting.Choices!.Single(c => c.Value == slug);
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioACatalogImportedThemeCarriesItsSource
    {
        [Fact]
        public async Task ItsSettingChoiceCarriesTheCatalogSlugAndAStamp()
        {
            // Given a theme imported from the catalog,
            await using var factory = new ThemeProvenanceWebFactory();
            var client = await LoggedInClientAsync(factory);
            var import = await PostManifestAsync(
                client, "midnight-drive", ThemeProvenanceFixture.ValidManifestJson("midnight-drive"),
                catalogSlug: "midnight-drive-catalog-entry");
            Assert.True(import.IsSuccessStatusCode, await import.Content.ReadAsStringAsync());

            // When Station:Theme's settings choices are read (GET /api/settings, the read path this
            // task adds provenance to),
            var choice = await StationThemeChoiceAsync(client, "midnight-drive");

            // Then its choice carries the catalog entry's own slug as ImportedFrom, plus a stamp (AC1).
            Assert.Equal(
                (ImportedFrom: "midnight-drive-catalog-entry", Stamped: true),
                (choice.ImportedFrom, Stamped: choice.ImportedAt is not null));
        }
    }

    public sealed class ScenarioAFileImportedThemeCarriesFileProvenance
    {
        [Fact]
        public async Task ItsSettingChoiceCarriesFileAndAStamp()
        {
            // Given a theme imported directly (no catalogSlug),
            await using var factory = new ThemeProvenanceWebFactory();
            var client = await LoggedInClientAsync(factory);
            var import = await PostManifestAsync(client, "aurora-glow", ThemeProvenanceFixture.ValidManifestJson("aurora-glow"));
            Assert.True(import.IsSuccessStatusCode, await import.Content.ReadAsStringAsync());

            // When Station:Theme's settings choices are read,
            var choice = await StationThemeChoiceAsync(client, "aurora-glow");

            // Then its choice carries "file" as ImportedFrom, plus a stamp (AC1's file-upload half).
            Assert.Equal(
                (ImportedFrom: "file", Stamped: true),
                (choice.ImportedFrom, Stamped: choice.ImportedAt is not null));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAShippedDefaultCarriesNoProvenance
    {
        [Fact]
        public async Task ItsSettingChoiceCarriesNeitherField()
        {
            // Given no import at all — every shipped default is present from boot,
            await using var factory = new ThemeProvenanceWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When Station:Theme's settings choices are read,
            var choice = await StationThemeChoiceAsync(client, GenWave.Host.Theming.ThemeCatalog.ShippedDefaultSlug);

            // Then the shipped default's choice carries neither ImportedFrom nor ImportedAt (AC2) —
            // no station.theme row exists for it to read one off.
            Assert.Equal((ImportedFrom: (string?)null, ImportedAt: (DateTime?)null), (choice.ImportedFrom, choice.ImportedAt));
        }
    }
}
