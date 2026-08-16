// STORY-331 — The shelf gains the visual kinds (SPEC F128.1/.2, F130.6 · PLAN T292)
//
// BDD specification — xUnit. App-side kind admission only: AC4 (catalog CI rules +
// the likeness attestation) is genwave-catalog CI acceptance, not app xUnit (the
// STORY-314/316 precedent). T292 also RECORDS the ordering finding (does the SHIPPED
// validator tolerate persona assets[]?) — a finding, not a fact; it gates T311's
// catalog merges (see PLAN.md/the T292 dispatch report, not a spec here).
//
// The seam-level facts (AC5/AC6 below) drive CatalogIndexValidator.TryValidate directly —
// the same "test the seam directly" idiom Story269_CatalogKindSeam.cs/Story279_FontKindAssets.cs
// already use for kind admission. The wire-level facts (AC1-AC3) drive the real
// GET /api/catalog/index and GET /api/catalog/entries/{slug} routes through
// WebApplicationFactory<Program> against a fake origin — mirrors Story279's own
// ScenarioBothRealRoutesServeAValidFontEntry/ScenarioFontMetaProjectsThroughTheRealRoutes
// (the S1/F3 review-finding precedent: a new kind admitted by the validator alone still 500s
// both routes until CatalogController.ToWireKind/ToEntryResponse learn it too).

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Host.Api;
using GenWave.Host.Catalog;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheShelfGainsTheVisualKinds
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the two new kinds and the persona face asset
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnAvatarPackEntryIsAdmitted
    {
        [Fact]
        public async Task TheIndexEntrySurvivesValidationWithItsKind()
        {
            // Given a catalog index carrying a kind:"avatar" entry (manifest+meta+PNG assets[]),
            // served by a fake origin,
            await using var factory = new VisualKindShelfWebFactory();
            var client = await VisualKindShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/index is called through the real production route,
            var response = await client.GetAsync("/api/catalog/index");

            // Then it responds 200 (never a 500 — the S1/Story279 regression class: a kind the
            // validator admits but CatalogController.ToWireKind does not yet know still throws) and
            // the entry lists with kind == "avatar".
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var entry = body!.Entries!.Single(e => e.Slug == VisualKindCatalogFixtures.AvatarPackSlug);
            Assert.Equal("avatar", entry.Kind);
        }

        [Fact]
        public async Task TheDetailProjectionCarriesTheItemNames()
        {
            // Given the same fake origin's avatar pack, whose manifest declares three items,
            await using var factory = new VisualKindShelfWebFactory();
            var client = await VisualKindShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/entries/{slug} is called through the real production route,
            var response = await client.GetAsync($"/api/catalog/entries/{VisualKindCatalogFixtures.AvatarPackSlug}");

            // Then it responds 200 with the pack's item names (+ the one item's suggestedPersona
            // offer) parsed straight off the already-fetched, hash-verified manifest.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal(
                new[] { ("Warm Grin", "valid-dj"), ("Cool Stare", (string?)null), ("Ghost Face", (string?)null) },
                body!.AvatarItems!.Select(item => (item.Name, item.SuggestedPersona)));
        }

        [Fact]
        public async Task AManifestNamingAnUndeclaredFileDoesNotProjectIt()
        {
            // Given the same fake origin's avatar pack manifest, whose third item ("Ghost Face")
            // names a file the index's own assets[] never declared (review finding, PLAN T292 —
            // CatalogController.ResolveDeclaredAssetFile's own remarks),
            await using var factory = new VisualKindShelfWebFactory();
            var client = await VisualKindShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/entries/{slug} is called through the real production route,
            var response = await client.GetAsync($"/api/catalog/entries/{VisualKindCatalogFixtures.AvatarPackSlug}");

            // Then the undeclared item's File projects null (never the unverified manifest name),
            // while the two properly-declared items still project their real filenames.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            var byName = body!.AvatarItems!.ToDictionary(item => item.Name, item => item.File);
            Assert.Equal(
                new Dictionary<string, string?>
                {
                    ["Warm Grin"] = "warm-grin.png",
                    ["Cool Stare"] = "cool-stare.png",
                    ["Ghost Face"] = null,
                },
                byName);
        }
    }

    public sealed class ScenarioAnIconPackEntryIsAdmitted
    {
        [Fact]
        public async Task TheIndexEntrySurvivesValidationWithItsKind()
        {
            // Given a catalog index carrying a kind:"icon" entry (definition + meta, no binary
            // assets), served by a fake origin,
            await using var factory = new VisualKindShelfWebFactory();
            var client = await VisualKindShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/index is called through the real production route,
            var response = await client.GetAsync("/api/catalog/index");

            // Then it responds 200 and the entry lists with kind == "icon".
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var entry = body!.Entries!.Single(e => e.Slug == VisualKindCatalogFixtures.IconPackSlug);
            Assert.Equal("icon", entry.Kind);
        }
    }

    public sealed class ScenarioAPersonaEntryMayCarryExactlyOneFace
    {
        [Fact]
        public async Task OneAvatarAssetValidatesAndProjectsOnTheDetail()
        {
            // Given a persona entry whose assets[] holds exactly one <slug>.avatar.png, served by a
            // fake origin,
            await using var factory = new VisualKindShelfWebFactory();
            var client = await VisualKindShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/entries/{slug} is called through the real production route,
            var response = await client.GetAsync($"/api/catalog/entries/{VisualKindCatalogFixtures.PersonaSlug}");

            // Then it responds 200 — the entry validated — and its detail projection exposes the
            // avatar asset's bare filename, ready to pass straight to the existing asset route.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal("persona", body!.Kind);
            Assert.Equal($"{VisualKindCatalogFixtures.PersonaSlug}.avatar.png", body.PersonaAvatarFile);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — skip rules and the one-face rule
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownKindsStillSkipNotReject
    {
        static readonly Uri Directory = new("https://catalog.test/repo/");
        const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Fact]
        public void AFutureKindEntryIsSkippedAndKnownKindsStillList()
        {
            // Given an index carrying a kind this app does not recognise (F103.4's forward-compat
            // rule, held through the F128/F130 widening — a still-genuinely-unknown "hologram")
            // alongside one entry of every kind this app DOES recognise, including the two new ones,
            var index = $$"""
                { "generatedAt": "2026-08-15", "entries": [
                  { "slug": "future-hologram", "kind": "hologram", "audience": "everyone",
                    "manifest": { "path": "entries/future-hologram/future-hologram.hologram.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/future-hologram/future-hologram.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "sample-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/sample-pack/sample-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/sample-pack/sample-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [ { "path": "entries/sample-pack/sample-pack.woff2", "sha256": "{{Sha}}", "bytes": 100 } ] },
                  { "slug": "late-shift", "kind": "show", "audience": "everyone",
                    "manifest": { "path": "entries/late-shift/late-shift.show.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/late-shift/late-shift.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "face-pack", "kind": "avatar", "audience": "everyone",
                    "manifest": { "path": "entries/face-pack/face-pack.avatar.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/face-pack/face-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [ { "path": "entries/face-pack/warm-grin.png", "sha256": "{{Sha}}", "bytes": 100 } ] },
                  { "slug": "line-icons", "kind": "icon", "audience": "everyone",
                    "manifest": { "path": "entries/line-icons/line-icons.icon.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/line-icons/line-icons.meta.json", "sha256": "{{Sha}}" } } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(Encoding.UTF8.GetBytes(index), Directory, out var entries, out _);
            Assert.True(success);

            // Then the hologram entry is skipped and every known kind — including avatar and icon —
            // still lists.
            Assert.Equal(
                new[]
                {
                    ("valid-dj", CatalogEntryKind.Persona),
                    ("gilded-static", CatalogEntryKind.Theme),
                    ("sample-pack", CatalogEntryKind.Font),
                    ("late-shift", CatalogEntryKind.Show),
                    ("face-pack", CatalogEntryKind.Avatar),
                    ("line-icons", CatalogEntryKind.Icon),
                }.ToHashSet(),
                entries!.Select(e => (e.Slug, e.Kind)).ToHashSet());
        }
    }

    public sealed class ScenarioAPersonaEntryWithTwoAssetsIsWithheldNotTheWholeIndex
    {
        static readonly Uri Directory = new("https://catalog.test/repo/");
        const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Fact]
        public void TheEntryIsWithheldAndTheRestOfTheShelfStillLists()
        {
            // Given a persona entry whose assets[] carries TWO declared assets (SPEC F128.2's
            // one-face rule — STORY-331 AC6), sharing an index with an ordinary, unrelated theme
            // entry (round-1 review finding 1 — proving the REST of the shelf survives),
            var index = $$"""
                { "generatedAt": "2026-08-15", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/valid-dj/valid-dj.avatar.png", "sha256": "{{Sha}}", "bytes": 100 },
                      { "path": "entries/valid-dj/valid-dj-second.avatar.png", "sha256": "{{Sha}}", "bytes": 100 }
                    ] },
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" } } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), Directory, out var entries, out var notices, out _);

            // Then the WHOLE INDEX still loads (a single community typo on one persona's sidecar
            // must never brick every other station's shelf), the two-asset entry is excluded, and
            // the unrelated theme entry still lists.
            Assert.True(success);
            Assert.Equal(["gilded-static"], entries!.Select(e => e.Slug));

            // ...and the one-face reason reaches the caller — CatalogProxyService's own WARN log —
            // naming the withheld slug.
            var notice = Assert.Single(notices);
            Assert.Equal(("valid-dj", CatalogValidationNoticeKind.EntryWithheld), (notice.Slug, notice.Kind));
            Assert.Contains("one face", notice.Reason);
        }
    }

    public sealed class ScenarioAMalformedSidecarDegradesToNoFace
    {
        static readonly Uri Directory = new("https://catalog.test/repo/");

        [Fact]
        public void TheEntryStillListsWithNoFaceAndADegradeNotice()
        {
            // Given a persona entry whose ONE declared avatar asset is malformed (a bad sha256
            // shape) — round-1 review finding 3's resilience regression: a broken sidecar used to
            // skip the WHOLE entry, silently, even though pre-F128 junk was always ignored,
            var index = """
                { "generatedAt": "2026-08-15", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                    "assets": [
                      { "path": "entries/valid-dj/valid-dj.avatar.png", "sha256": "not-a-real-sha256", "bytes": 100 }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), Directory, out var entries, out var notices, out _);

            // Then the entry still validates and lists, with no face (its one asset degraded)...
            Assert.True(success);
            var entry = Assert.Single(entries!);
            Assert.Equal(("valid-dj", 0), (entry.Slug, entry.Assets.Count));

            // ...and the caller gets a degrade notice, not a withheld one.
            var notice = Assert.Single(notices);
            Assert.Equal(("valid-dj", CatalogValidationNoticeKind.FieldDegraded), (notice.Slug, notice.Kind));
        }
    }

    public sealed class ScenarioAPersonaSidecarNamingAnotherEntrysSlugDegrades
    {
        static readonly Uri Directory = new("https://catalog.test/repo/");
        const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Fact]
        public void ItDoesNotValidateAsThisEntrysFace()
        {
            // Given a persona entry ("valid-dj") whose one declared avatar asset's filename names a
            // DIFFERENT slug ("other-dj.avatar.png") sitting in valid-dj's own directory (review
            // finding, PLAN T292 — the pattern used to admit ANY slug-shaped filename here, not just
            // this entry's own),
            var index = $$"""
                { "generatedAt": "2026-08-15", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/valid-dj/other-dj.avatar.png", "sha256": "{{Sha}}", "bytes": 100 }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), Directory, out var entries, out var notices, out _);

            // Then the entry still lists, with no face (the mismatched filename never matches the
            // pattern's own backreference, so it degrades exactly like any other malformed sidecar).
            Assert.True(success);
            var entry = Assert.Single(entries!);
            Assert.Equal(("valid-dj", 0), (entry.Slug, entry.Assets.Count));
            Assert.Equal(CatalogValidationNoticeKind.FieldDegraded, Assert.Single(notices).Kind);
        }
    }

    public sealed class ScenarioA300KiBDeclaredAvatarAssetValidates
    {
        static readonly Uri Directory = new("https://catalog.test/repo/");
        const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const int DeclaredBytes = 300 * 1024;

        [Fact]
        public void ThePackEntryValidatesDespiteExceedingTheFontDerivedCeiling()
        {
            // Given an avatar pack entry whose one PNG item declares 300 KiB — CI-legal under SPEC
            // F128.1's ≤512 KiB per-item ceiling, but ABOVE the 256 KiB font-derived ceiling this
            // class used to apply to every kind alike (round-1 review finding 2),
            var index = $$"""
                { "generatedAt": "2026-08-15", "entries": [
                  { "slug": "face-pack", "kind": "avatar", "audience": "everyone",
                    "manifest": { "path": "entries/face-pack/face-pack.avatar.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/face-pack/face-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/face-pack/warm-grin.png", "sha256": "{{Sha}}", "bytes": {{DeclaredBytes}} }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(Encoding.UTF8.GetBytes(index), Directory, out var entries, out _);

            // Then the pack entry validates, carrying its one 300 KiB-declared asset.
            Assert.True(success);
            var entry = Assert.Single(entries!);
            Assert.Equal(DeclaredBytes, Assert.Single(entry.Assets).Bytes);
        }
    }
}

// ── Test harness ──────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own wire-level scenarios
/// (PLAN T292) — boots the real Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at
/// <see cref="VisualKindCatalogFixtures.IndexUrl"/>, served by <see cref="VisualKindCatalogFixtures.BuildRoutedHandler"/>.
/// Mirrors <c>Story279_FontKindAssets.cs</c>'s own <c>FontShelfWebFactory</c> (private to that file,
/// so this file needs its own copy) trimmed to only what these scenarios need.
/// </summary>
file sealed class VisualKindShelfWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story331-visualkindshelf";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", VisualKindCatalogFixtures.IndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(VisualKindCatalogFixtures.BuildRoutedHandler()));
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
/// Fixture documents + a routed fake HTTP double for <c>VisualKindShelfWebFactory</c> (PLAN T292) — a
/// persona entry carrying its own avatar sidecar face, an avatar-pack entry with two items, and an
/// icon-pack entry with no binary assets, every sha256 computed from the served content itself so
/// every real route fetches and hash-verifies successfully. <c>file</c>-scoped, mirrors
/// <c>Story279_FontKindAssets.cs</c>'s own <c>FontShelfFixtures</c>.
/// </summary>
file static class VisualKindCatalogFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string PersonaSlug = "valid-dj";
    public const string AvatarPackSlug = "face-pack";
    public const string IconPackSlug = "line-icons";

    public static string PersonaCardJson => """
        {
          "schemaVersion": 1,
          "name": "Green Test DJ",
          "tagline": "",
          "soul": "",
          "quirks": [],
          "voice": { "engine": "kokoro", "voiceId": "af_heart", "pace": 1.0, "language": "en" },
          "energyDisposition": 0,
          "lore": [],
          "corrections": []
        }
        """;

    public static string PersonaMetaJson => """
        {
          "author": "Test Fixture",
          "description": "A persona entry wearing its own catalog sidecar face (STORY-331).",
          "samplePatter": ["Line one."],
          "audience": "everyone",
          "added": "2026-08-15"
        }
        """;

    public const string PersonaAvatarPngContent = "not-a-real-png-but-hash-verified-all-the-same";

    public static string AvatarManifestJson => """
        {
          "packName": "Face Pack",
          "items": [
            { "name": "Warm Grin", "file": "warm-grin.png", "suggestedPersona": "valid-dj" },
            { "name": "Cool Stare", "file": "cool-stare.png" },
            { "name": "Ghost Face", "file": "ghost-face.png" }
          ]
        }
        """;

    public static string AvatarMetaJson => """
        {
          "author": "Test Fixture",
          "description": "A curated avatar pack sharing the shelf with a persona entry.",
          "audience": "everyone",
          "added": "2026-08-15"
        }
        """;

    public const string WarmGrinPngContent = "warm-grin-fake-png-bytes";
    public const string CoolStarePngContent = "cool-stare-fake-png-bytes";

    public static string IconManifestJson => """
        {
          "schemaVersion": 1,
          "style": { "strokeWidth": 1.5, "fill": "none" },
          "icons": {}
        }
        """;

    public static string IconMetaJson => """
        {
          "author": "Test Fixture",
          "description": "A curated icon pack sharing the shelf with a persona entry.",
          "audience": "everyone",
          "added": "2026-08-15"
        }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string IndexJson() => $$"""
        { "generatedAt": "2026-08-15", "entries": [
          { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
            "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha256Hex(PersonaCardJson)}}" },
            "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha256Hex(PersonaMetaJson)}}" },
            "assets": [
              { "path": "entries/valid-dj/valid-dj.avatar.png", "sha256": "{{Sha256Hex(PersonaAvatarPngContent)}}", "bytes": {{PersonaAvatarPngContent.Length}} }
            ] },
          { "slug": "face-pack", "kind": "avatar", "audience": "everyone",
            "manifest": { "path": "entries/face-pack/face-pack.avatar.json", "sha256": "{{Sha256Hex(AvatarManifestJson)}}" },
            "meta": { "path": "entries/face-pack/face-pack.meta.json", "sha256": "{{Sha256Hex(AvatarMetaJson)}}" },
            "assets": [
              { "path": "entries/face-pack/warm-grin.png", "sha256": "{{Sha256Hex(WarmGrinPngContent)}}", "bytes": {{WarmGrinPngContent.Length}} },
              { "path": "entries/face-pack/cool-stare.png", "sha256": "{{Sha256Hex(CoolStarePngContent)}}", "bytes": {{CoolStarePngContent.Length}} }
            ] },
          { "slug": "line-icons", "kind": "icon", "audience": "everyone",
            "manifest": { "path": "entries/line-icons/line-icons.icon.json", "sha256": "{{Sha256Hex(IconManifestJson)}}" },
            "meta": { "path": "entries/line-icons/line-icons.meta.json", "sha256": "{{Sha256Hex(IconMetaJson)}}" } } ] }
        """;

    /// <summary>Serves every fixture document at its OWN resolved URL, 404 for anything else — every
    /// request is still recorded on <see cref="FakeHttpMessageHandler.Requests"/> (mirrors
    /// <c>FontShelfFixtures.BuildRoutedHandler</c>).</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/valid-dj/valid-dj.persona.json"] = PersonaCardJson,
            [Directory + "entries/valid-dj/valid-dj.meta.json"] = PersonaMetaJson,
            [Directory + "entries/valid-dj/valid-dj.avatar.png"] = PersonaAvatarPngContent,
            [Directory + "entries/face-pack/face-pack.avatar.json"] = AvatarManifestJson,
            [Directory + "entries/face-pack/face-pack.meta.json"] = AvatarMetaJson,
            [Directory + "entries/face-pack/warm-grin.png"] = WarmGrinPngContent,
            [Directory + "entries/face-pack/cool-stare.png"] = CoolStarePngContent,
            [Directory + "entries/line-icons/line-icons.icon.json"] = IconManifestJson,
            [Directory + "entries/line-icons/line-icons.meta.json"] = IconMetaJson,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
