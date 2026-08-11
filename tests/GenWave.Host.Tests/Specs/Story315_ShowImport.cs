// STORY-315 — Hire a show from the shelf (F118.1, F118.2, F118.3, F118.5)
//
// BDD specification — xUnit. WIRED T254 — every Fact below drives the real production
// POST /api/shows/{slug}/import route (folded into ShowsController — see that class's own remarks on
// why Show needs no ThemesImportController-shaped second file) through WebApplicationFactory<Program>,
// against FakeShowStore/FakeScheduleStore/FakeShowImagingScope doubles — no live Postgres, this
// project has none for Host.Tests (mirrors Story305_ShowsApi.cs's own idiom, whose ShowsApiWebFactory
// is file-scoped to that file, so this one carries its own copy).
//
// ScenarioTheShelfListsShows additionally proves T254's OTHER half — kind "show" joining the
// index/meta projection through CatalogProxyService/CatalogController (SPEC F118.1) — mirroring
// Story269_CatalogKindSeam.cs's own "test the validator seam directly" idiom for the kind
// discriminator, plus a real GET /api/catalog/entries/{slug} round trip (mirrors
// Story273_ThemeShelfPreview.cs's own wire-projection proof) for the meta-sourced suggestedPersona
// field (F118.3).
//
// One assertion per Fact where the scenario allows it; the sad path (schema-major/size-cap/budget) is
// its own block, mirroring Story272_ThemeImport.cs's own shape.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Catalog;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

// ── Fixture file access ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Locates and reads <c>Fixtures/golden.show.json</c> from its SOURCE location (not a build output
/// copy) — mirrors <c>Story231_GoldenCardParity.cs</c>'s own <c>GoldenFixtureFile</c> idiom (itself
/// <c>file</c>-scoped, so this file needs its own copy).
/// </summary>
file static class GoldenShowFixtureFile
{
    public static byte[] ReadBytes() => File.ReadAllBytes(LocatePath());

    static string LocatePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("repo root (GenWave.sln) not found");

        return Path.Combine(dir.FullName, "tests", "GenWave.Host.Tests", "Fixtures", "golden.show.json");
    }
}

// ── Show manifest fixture builder ─────────────────────────────────────────────────────────────────

file static class ShowImportFixture
{
    /// <summary>A schema-valid show manifest body. <paramref name="schemaVersionRaw"/> takes
    /// precedence over <paramref name="schemaVersion"/> — mirrors Story272_ThemeImport.cs's own
    /// <c>ValidManifestJson</c> idiom — inserting a raw JSON literal verbatim so an "unreadable
    /// schemaVersion" spec can exercise a shape <c>int?</c> can't express, while every other field
    /// stays a fully valid manifest (<c>AnUnreadableSchemaVersionIsRefused400</c>, below). Every text
    /// field is JSON-ENCODED via <see cref="JsonSerializer"/> (F1 review finding), not naively
    /// interpolated between bare quotes — the sanitizer/control-character Facts in
    /// <c>ScenarioFlavorNameHygiene</c> post a raw newline and a literal <c>&lt;&lt;&lt;</c> run
    /// through <paramref name="name"/>/<paramref name="flavor"/>/<paramref name="tagline"/>, and a bare
    /// <c>"{{value}}"</c> splice would itself emit invalid JSON for either shape.</summary>
    public static string ManifestJson(
        string name, string flavor, string tagline = "Late-night deep cuts",
        int? schemaVersion = null, string? schemaVersionRaw = null)
    {
        var schemaVersionField = schemaVersionRaw is { } raw
            ? $"\"schemaVersion\": {raw},"
            : schemaVersion is { } version ? $"\"schemaVersion\": {version}," : "";
        return $$"""
            {
              {{schemaVersionField}}
              "name": {{JsonString(name)}},
              "tagline": {{JsonString(tagline)}},
              "flavor": {{JsonString(flavor)}}
            }
            """;
    }

    static string JsonString(string value) => JsonSerializer.Serialize(value);
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for every Fact in this file — brings up the real
/// HTTP pipeline (routing, auth, the production <c>POST /api/shows/{slug}/import</c> route) over ONE
/// shared <see cref="FakeShowStore"/>, mirrors <c>Story305_ShowsApi.cs</c>'s own
/// <c>ShowsApiWebFactory</c> (file-scoped there, so this file keeps its own copy).
/// <paramref name="catalogIndexUrl"/>/<paramref name="catalogHandler"/> are ONLY consumed by
/// <see cref="FeatureShowImport.ScenarioTheShelfListsShows"/>/
/// <see cref="FeatureShowImport.ScenarioRejectingBadImports.SpectatorIsByteIdenticalWithCatalogUnreachable"/>
/// — every other Fact leaves both null and gets Program.cs's own untouched default (an empty,
/// disabled <c>Community:CatalogIndexUrl</c>), which is itself the point for those Facts (an import
/// wired to nothing catalog-shaped at all still succeeds).
/// </summary>
file sealed class ShowImportWebFactory(
    FakeShowStore store, string? catalogIndexUrl = null, HttpMessageHandler? catalogHandler = null)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story315";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        if (catalogIndexUrl is not null)
            builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IShowStore>();
            services.AddSingleton<IShowStore>(store);

            services.RemoveAll<IScheduleStore>();
            services.AddSingleton<IScheduleStore>(new FakeScheduleStore());

            services.RemoveAll<IShowImagingScope>();
            services.AddSingleton<IShowImagingScope>(new FakeShowImagingScope());

            if (catalogHandler is not null)
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(catalogHandler));
            }
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

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureShowImport
{
    static Task<HttpResponseMessage> PostManifestAsync(
        HttpClient client, string slug, string json, string? catalogSlug = null)
    {
        var uri = catalogSlug is null ? $"/api/shows/{slug}/import" : $"/api/shows/{slug}/import?catalogSlug={catalogSlug}";
        return client.PostAsync(uri, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    /// <summary>Reads a <c>ProblemDetails</c> response's own <c>detail</c> field — mirrors
    /// Story272_ThemeImport.cs's own identically-named helper (each spec file keeps its own committed
    /// copy, the house idiom that file's own remarks already state).</summary>
    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }

    public sealed class ScenarioImportThroughTheShell
    {
        [Fact]
        public async Task ImportUpsertsTransactionallyWithProvenance()
        {
            // Given a valid show card (as if fetched by catalogSlug — the import route never
            // re-fetches it itself, proven separately by ScenarioTheShelfListsShows below and by
            // SpectatorIsByteIdenticalWithCatalogUnreachable), imported once,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            var first = await PostManifestAsync(
                client, "night-moves", ShowImportFixture.ManifestJson("Night Moves", "moody, sparse"),
                catalogSlug: "night-moves-catalog-entry");
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When POST /api/shows/{slug}/import runs AGAIN on the SAME slug with different content,
            var second = await PostManifestAsync(
                client, "night-moves", ShowImportFixture.ManifestJson("Night Moves Redux", "moodier, sparser"),
                catalogSlug: "night-moves-catalog-entry");

            // Then the show lands WHOLE with imported_from = catalogSlug and imported_at set, and
            // exactly ONE row exists under this slug — the upsert replaced rather than duplicated
            // (the "transactionally" claim: one statement, no partial state possible in between).
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            var all = await store.GetAllAsync(CancellationToken.None);
            var stored = await store.GetBySlugAsync("night-moves", CancellationToken.None);
            Assert.Equal(
                (RowCount: 1, Name: "Night Moves Redux", ImportedFrom: "night-moves-catalog-entry", ImportedAtSet: true),
                (RowCount: all.Count, stored?.Name, stored?.ImportedFrom, ImportedAtSet: stored?.ImportedAt is not null));
        }

        [Fact]
        public async Task FileUploadStampsFile()
        {
            // Given a direct file upload of a show manifest (no catalogSlug),
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When the import runs,
            var response = await PostManifestAsync(client, "aurora-drive", ShowImportFixture.ManifestJson("Aurora Drive", "warm, unhurried"));
            var body = await response.Content.ReadFromJsonAsync<ShowDto>();

            // Then imported_from = "file" (the F103.6/F118.2 provenance triple) — checked on both the
            // response and the stored row.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal("file", body?.ImportedFrom);
            var stored = await store.GetBySlugAsync("aurora-drive", CancellationToken.None);
            Assert.Equal("file", stored?.ImportedFrom);
        }

        [Fact]
        public async Task GoldenParityPinsTheCrossRepoContract()
        {
            // Given fixtures/golden.show.json's raw bytes, byte-for-byte from the catalog repo (see
            // Fixtures/README.md for the pin),
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            var content = new ByteArrayContent(GoldenShowFixtureFile.ReadBytes());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // When they are POSTed, unmodified, to the real F79 shell — there is no
            // ShowManifestSerializer to reserialize through (unlike golden.theme.json's own proof;
            // Show is a flat station.show row, not a JSON blob), so the shell's own
            // parse-then-persist path IS the round trip (T107/T193 extended to shows).
            var response = await client.PostAsync("/api/shows/night-moves/import", content);

            // Then it imports whole — the fixture's own known field values, hand-transcribed from
            // Fixtures/golden.show.json's own bytes (never re-derived from the parser), so a content
            // drift in either the fixture or the import path turns this fact red.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await store.GetBySlugAsync("night-moves", CancellationToken.None);
            Assert.Equal(
                (Name: "Night Moves",
                 Tagline: "Late-night drive tracks for the insomniac hours.",
                 Flavor: "A slow-burn night-shift show built for empty highways and glowing dashboards, " +
                     "moody, unhurried, heavy on atmosphere and light on chatter. Leans into extended " +
                     "instrumental passages and lets tracks breathe; keeps banter sparse, warm, and " +
                     "half-whispered, like talking to someone already drifting off."),
                (stored?.Name, stored?.Tagline, stored?.Flavor));
        }
    }

    public sealed class ScenarioSoftSuggestion
    {
        [Fact]
        public async Task ImportSucceedsWithSuggestionAbsentUnknownOrHired()
        {
            // Given a show manifest — the import endpoint reads ONLY the posted <slug>.show.json
            // body; suggestedPersona lives in the entry's meta.json (SPEC F118.3), which this route
            // never fetches or inspects (see ShowManifest's own remarks: the manifest carries name/
            // tagline/flavor and nothing else). "Absent, unknown, or already hired" are therefore not
            // three different inputs from THIS endpoint's own point of view — they are the identical
            // input (a manifest with no suggestedPersona-shaped concern at all); the soft "also hire"
            // offer is entirely PLAN T255's UI-side concern, sourced from the separate
            // GET /api/catalog/entries/{slug} read this task's ScenarioTheShelfListsShows proves below.
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When the import runs,
            var response = await PostManifestAsync(client, "solo-drive", ShowImportFixture.ManifestJson("Solo Drive", "steady, easy"));

            // Then it succeeds — soft means soft: nothing about a suggestion can ever block or alter
            // an import, because nothing about it is even read here.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
    }

    // F1 review finding — the ShowManifestParser.Parse decision this file's header names ("VERBATIM
    // but F108-style hygiene") had ZERO coverage before this class: a Fact-free suite is deletable
    // with a green build. One fact per hazard shape, per field, mirrors Story272_ThemeImport.cs's own
    // per-gate granularity.
    public sealed class ScenarioFlavorNameHygiene
    {
        [Fact]
        public async Task FlavorContainingANewlineIsSanitizedToASpaceOnImport()
        {
            // Given a flavor carrying a raw newline — the exact shape LlmPromptBuilder.BuildShowFlavorPatterLine's
            // own unfenced interpolation could turn into a fake extra prompt line,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When it is imported,
            var response = await PostManifestAsync(
                client, "newline-flavor", ShowImportFixture.ManifestJson("Newline Flavor", "line one\nline two"));

            // Then it persists flattened to a single space — no raw control character survives.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await store.GetBySlugAsync("newline-flavor", CancellationToken.None);
            Assert.Equal("line one line two", stored?.Flavor);
        }

        [Fact]
        public async Task FlavorContainingAFenceRunCollapsesToOneCharacterOnImport()
        {
            // Given a flavor carrying a literal run of the SAME fence delimiter
            // BuildPatterFactLine/BuildContextFactsLine use elsewhere in the same prompt,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When it is imported,
            var response = await PostManifestAsync(
                client, "fenced-flavor", ShowImportFixture.ManifestJson("Fenced Flavor", "start <<<data>>> end"));

            // Then every run of 3+ identical angle brackets collapses to exactly one — it can never
            // again forge either fence delimiter.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await store.GetBySlugAsync("fenced-flavor", CancellationToken.None);
            Assert.Equal("start <data> end", stored?.Flavor);
        }

        [Fact]
        public async Task NameContainingANewlineIsSanitizedToASpaceOnImport()
        {
            // Given a name carrying a raw newline — BuildShowFlavorPatterLine/BuildShowLine both
            // interpolate Show.Name unfenced too (ShowManifestParser's own remarks), not flavor alone,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When it is imported,
            var response = await PostManifestAsync(
                client, "newline-name", ShowImportFixture.ManifestJson("Line One\nLine Two", "steady"));

            // Then it persists flattened, exactly like flavor.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await store.GetBySlugAsync("newline-name", CancellationToken.None);
            Assert.Equal("Line One Line Two", stored?.Name);
        }

        [Fact]
        public async Task NameContainingAFenceRunCollapsesToOneCharacterOnImport()
        {
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            var response = await PostManifestAsync(
                client, "fenced-name", ShowImportFixture.ManifestJson("Show <<<X>>> Nine", "steady"));

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await store.GetBySlugAsync("fenced-name", CancellationToken.None);
            Assert.Equal("Show <X> Nine", stored?.Name);
        }

        [Fact]
        public async Task TaglineIsNeverSanitizedTheDeliberateAsymmetry()
        {
            // Given a tagline carrying the SAME two hazard shapes flavor/name are sanitized against —
            // proves the asymmetry is a DECISION (Tagline never reaches an LLM prompt position,
            // ShowManifest's own remarks), not an oversight that merely forgot this field too.
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            const string rawTagline = "line one\nline two <<<raw>>>";

            // When it is imported,
            var response = await PostManifestAsync(
                client, "raw-tagline", ShowImportFixture.ManifestJson("Raw Tagline Show", "steady", tagline: rawTagline));

            // Then it survives byte-for-byte, raw newline and fence run both — never touched.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await store.GetBySlugAsync("raw-tagline", CancellationToken.None);
            Assert.Equal(rawTagline, stored?.Tagline);
        }
    }

    // F1 review finding — the F115.5 collision gate itself (both directions) had zero coverage through
    // the real route before this class.
    public sealed class ScenarioAuthoredCollision
    {
        [Fact]
        public async Task ImportOntoAnAuthoredSlugRefuses409ThroughTheRouteAndLeavesItUntouched()
        {
            // Given an authored show already sitting at this slug (ImportedFrom null),
            var authored = new Show(1, "Authored Show", "authored-show", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
            var store = new FakeShowStore([authored]);
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When an import targets the SAME slug,
            var response = await PostManifestAsync(client, "authored-show", ShowImportFixture.ManifestJson("Hijack Attempt", "x"));

            // Then it refuses 409 — the atomic upsert's own WHERE guard declined — and the authored
            // row is left byte-for-byte untouched (F2's atomicity claim, proven through the route).
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var stillThere = await store.GetBySlugAsync("authored-show", CancellationToken.None);
            Assert.Equal(authored, stillThere);
        }

        [Fact]
        public async Task ImportOverAPriorImportOverwritesAndRestampsProvenanceThroughTheRoute()
        {
            // Given a show already imported once,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            var first = await PostManifestAsync(
                client, "reimport-show", ShowImportFixture.ManifestJson("First Cut", "moody"), catalogSlug: "entry-a");
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When it is imported again under a DIFFERENT provenance,
            var second = await PostManifestAsync(
                client, "reimport-show", ShowImportFixture.ManifestJson("Second Cut", "moodier"), catalogSlug: "entry-b");

            // Then it succeeds (never 409 — a prior import can always be overwritten by a later one)
            // and provenance is re-stamped to the new source.
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            var stored = await store.GetBySlugAsync("reimport-show", CancellationToken.None);
            Assert.Equal(("Second Cut", "entry-b"), (stored?.Name, stored?.ImportedFrom));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioRejectingBadImports
    {
        [Fact]
        public async Task SchemaMajorAndSizeCapRejectAsTheShellDoes()
        {
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // Given a manifest whose schema major exceeds the app's,
            var newerSchema = await PostManifestAsync(
                client, "future-show", ShowImportFixture.ManifestJson("Future Show", "x", schemaVersion: 2));
            var newerSchemaDetail = await DetailAsync(newerSchema);

            // Given a body over BoundedImportBodyReader's own size cap,
            var oversized = await PostManifestAsync(client, "too-big-show", new string('a', 300 * 1024));

            // Given a flavor over the SPEC F115.1 2× import ceiling (strictly greater — see
            // ShowManifestParser's own remarks on the 2× boundary),
            var overCap = await PostManifestAsync(
                client, "flavor-blowout",
                ShowImportFixture.ManifestJson("Flavor Blowout", new string('f', (ShowBudgets.FlavorMaxChars * 2) + 1)));

            // Then each fails closed — the schema case naming BOTH versions (SPEC F118.2), 413 for
            // the oversized body, 400 for the over-cap flavor — and nothing was stored for any of the
            // three rejected attempts (no partial write).
            Assert.Equal(
                (SchemaStatus: HttpStatusCode.BadRequest,
                 SchemaNamesBothVersions: true,
                 OversizedStatus: HttpStatusCode.RequestEntityTooLarge,
                 OverCapStatus: HttpStatusCode.BadRequest,
                 StoredCount: 0),
                (SchemaStatus: newerSchema.StatusCode,
                 SchemaNamesBothVersions: newerSchemaDetail.Contains(
                     "schema version 2 is newer than this station's supported version 1", StringComparison.Ordinal),
                 OversizedStatus: oversized.StatusCode,
                 OverCapStatus: overCap.StatusCode,
                 StoredCount: (await store.GetAllAsync(CancellationToken.None)).Count));
        }

        [Theory]
        [InlineData("")]                                    // disabled
        [InlineData("http://127.0.0.1:1/repo/index.json")]   // unreachable — dead loopback port
        public async Task SpectatorIsByteIdenticalWithCatalogUnreachable(string catalogIndexUrl)
        {
            // F103.12's fail-closed/isolation posture, inherited verbatim (F118.5): the show import
            // route depends on IShowStore alone — never CommunityCatalogAccessor/CatalogProxyService
            // (catalogSlug is provenance text only, mirrors ThemesImportController's identical
            // posture and Story278_ThemeCatalogIsolation.cs's own ImportSucceedsRegardlessOfCatalogState).
            // No spectator payload reads station.show's catalog-adjacent state at all — SpectatorShow
            // (PLAN T251) reads straight off the schedule/show tables, and
            // Story278_ThemeCatalogIsolation.cs's own ScenarioSpectatorIsUnchangedRegardlessOfCatalogState
            // already proves the SAME catalog subsystem this task extends (CatalogController/
            // CatalogProxyService) stays fully isolated from every spectator route on both the
            // disabled and unreachable axes — nothing spectator-side branches on CatalogEntryKind at
            // all, so that existing proof already covers the "show" kind too. The concrete,
            // exercisable form of this claim for THIS task's own added surface is therefore: the
            // import route itself never depends on catalog reachability.
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store, catalogIndexUrl);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            var response = await PostManifestAsync(
                client, "catalog-agnostic-show", ShowImportFixture.ManifestJson("Catalog Agnostic Show", "steady"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // F1 review finding — nine refusal gates this route's own remarks name in its GATE ORDER paragraph
    // carried zero coverage: each was deletable from ShowsController.Import with the suite staying
    // green. One Fact per gate, mirrors Story272_ThemeImport.cs's own ScenarioRejectingBadImports/
    // ScenarioCatalogSlugPrecedence granularity.
    public sealed class ScenarioTheOtherRefusalGates
    {
        [Fact]
        public async Task RouteSlugFormatIsRefused400()
        {
            // Given a route slug outside the lowercase/digit/single-hyphen shape,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(client, "Bad_Slug", ShowImportFixture.ManifestJson("Bad Slug Show", "x"));

            // Then it refuses 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task TheReservedPersonaSlugIsRefused400()
        {
            // Given the route slug literal "persona" — T239's reserved fallback, mirrored (ReservedSlug's
            // own remarks),
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(client, "persona", ShowImportFixture.ManifestJson("Reserved Slug Show", "x"));

            // Then it refuses 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AnOverlongCatalogSlugIsRefused400()
        {
            // Given a catalogSlug longer than a real catalog entry slug could ever be,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            var overlong = new string('a', 65);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "overlong-catalog-slug", ShowImportFixture.ManifestJson("Overlong Catalog Slug Show", "x"),
                catalogSlug: overlong);

            // Then it refuses 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ABadlyShapedCatalogSlugIsRefused400()
        {
            // Given a catalogSlug outside the same slug shape,
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "bad-catalog-slug", ShowImportFixture.ManifestJson("Bad Catalog Slug Show", "x"), catalogSlug: "Not_Valid");

            // Then it refuses 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Theory]
        [InlineData("\"2\"")]           // string-typed
        [InlineData("2.5")]             // non-integer
        [InlineData("99999999999")]     // overflows Int32
        public async Task AnUnreadableSchemaVersionIsRefused400(string schemaVersionRaw)
        {
            // Given an otherwise-valid manifest whose schemaVersion is present but not a readable
            // whole number (mirrors Story272_ThemeImport.cs's own identically-shaped Theory — the
            // schemaVersionRaw knob ShowImportFixture.ManifestJson carries for exactly this),
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "unreadable-schema-version",
                ShowImportFixture.ManifestJson("Unreadable Schema Version Show", "x", schemaVersionRaw: schemaVersionRaw));

            // Then it refuses 400 (never silently treated as version 1) and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ANewerMajorManifestIsRefusedNamingBothVersionsEvenWhenAlsoStructurallyInvalid()
        {
            // Given a manifest whose schema major exceeds the app's AND whose shape is also missing
            // every field ShowManifestParser requires (name/tagline/flavor) — a newer major is free to
            // look nothing like today's v1 shape (mirrors Story272_ThemeImport.cs's own identically
            // named fact — proves the two-parse order: ExtractSchemaVersion runs BEFORE
            // ShowManifestParser.Parse ever sees the body),
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            const string json = """{ "schemaVersion": 2 }""";

            // When it is posted,
            var response = await PostManifestAsync(client, "both-broken", json);
            var detail = await DetailAsync(response);

            // Then it refuses 400 naming the version mismatch — never a misleading structural-parse
            // complaint (e.g. "is missing a name") — and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "schema version 2 is newer than this station's supported version 1", detail, StringComparison.Ordinal);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ABadCatalogSlugWinsOverAnOversizedBody()
        {
            // Mirrors Story272_ThemeImport.cs's own ScenarioCatalogSlugPrecedence (PLAN T207 review
            // finding B2, the SAME precedence this route's own remarks name): a bad catalogSlug —
            // cheap, pure-string, no I/O — must refuse BEFORE a body is ever read, even an oversized one.
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            var oversizedBody = new string('a', 300 * 1024);

            // When it is posted,
            var response = await PostManifestAsync(client, "precedence-show", oversizedBody, catalogSlug: "Not_Valid");
            var detail = await DetailAsync(response);

            // Then it refuses 400 with the catalogSlug problem's own copy — NEVER 413 — proving the
            // format check ran before the body was ever read.
            Assert.Equal(
                (Status: HttpStatusCode.BadRequest,
                 Detail: "\"Not_Valid\" is not a valid catalog slug (lowercase letters, digits, and single hyphens only)."),
                (Status: response.StatusCode, Detail: detail));
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ANameThatSanitizesToBlankIsRefused400()
        {
            // Given a name that is non-blank RAW (a single control character — passes the pre-sanitize
            // IsNullOrWhiteSpace guard, since char.IsWhiteSpace and char.IsControl are different
            // predicates) but sanitizes down to nothing (ShowManifestParser's own remarks),
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            var json = ShowImportFixture.ManifestJson("\u0001", "x");

            // When it is posted,
            var response = await PostManifestAsync(client, "blank-after-sanitize", json);

            // Then it refuses 400 and nothing is stored — the post-sanitize re-check, not the raw
            // pre-sanitize one, is what actually caught it.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task TaglineOverThe2xImportCeilingIsRefused400()
        {
            // Given a tagline over the SPEC F115.1 2× import ceiling (strictly greater),
            var store = new FakeShowStore();
            await using var factory = new ShowImportWebFactory(store);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);
            var overCap = new string('t', (ShowBudgets.TaglineMaxChars * 2) + 1);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "tagline-blowout", ShowImportFixture.ManifestJson("Tagline Blowout", "x", tagline: overCap));

            // Then it refuses 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    // ── F118.1 — the kind "show" index/meta projection through CatalogProxyService ────────────────

    public sealed class ScenarioTheShelfListsShows
    {
        const string Sha256Placeholder = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Fact]
        public void TheValidatorAdmitsAShowEntryUnderTheShowManifestPattern()
        {
            // Given an index entry declaring kind:"show" with a .show.json manifest (mirrors
            // Story269_CatalogKindSeam.cs's own "test the validator seam directly" idiom),
            var index = $$"""
                { "generatedAt": "2026-08-11", "entries": [
                  { "slug": "night-moves", "kind": "show", "audience": "everyone",
                    "manifest": { "path": "entries/night-moves/night-moves.show.json", "sha256": "{{Sha256Placeholder}}" },
                    "meta": { "path": "entries/night-moves/night-moves.meta.json", "sha256": "{{Sha256Placeholder}}" } } ] }
                """;

            // When it is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out var entries, out _);

            // Then it is admitted as kind Show, with no assets/family/preview (F118.1 — the same
            // minimal {manifest, meta} shape a persona entry has).
            Assert.True(success);
            var entry = Assert.Single(entries!);
            Assert.Equal(
                (Kind: CatalogEntryKind.Show, AssetCount: 0, Family: (string?)null, Preview: (CatalogThemePreview?)null),
                (Kind: entry.Kind, AssetCount: entry.Assets.Count, entry.Family, entry.Preview));
        }

        [Fact]
        public void AShowManifestPathUnderTheThemePatternIsRejected()
        {
            // Given a show entry whose manifest path wrongly uses the .theme.json extension —
            // proves the show manifest pattern is genuinely its OWN, not merely reusing theme's.
            var index = $$"""
                { "generatedAt": "2026-08-11", "entries": [
                  { "slug": "night-moves", "kind": "show", "audience": "everyone",
                    "manifest": { "path": "entries/night-moves/night-moves.theme.json", "sha256": "{{Sha256Placeholder}}" },
                    "meta": { "path": "entries/night-moves/night-moves.meta.json", "sha256": "{{Sha256Placeholder}}" } } ] }
                """;

            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out _, out var reason);

            Assert.False(success);
            Assert.Contains("manifest", reason);
        }

        [Fact]
        public async Task TheDetailRouteProjectsKindShowAndTheMetaSourcedSuggestedPersona()
        {
            // Given a catalog origin serving a kind:"show" entry whose meta.json carries an optional
            // suggestedPersona (SPEC F118.3) — the shape genwave-catalog's own tools/testdata/green/
            // valid-show-index-entry fixture uses,
            const string Slug = "night-moves";
            const string ManifestJson = """{"schemaVersion":1,"name":"Night Moves","tagline":"Late-night deep cuts","flavor":"moody, sparse"}""";
            const string MetaJson = """{"author":"Test","description":"desc","audience":"everyone","added":"2026-08-11","suggestedPersona":"example-dj"}""";
            const string IndexUrl = "https://catalog.test/repo/index.json";
            var manifestUrl = $"https://catalog.test/repo/entries/{Slug}/{Slug}.show.json";
            var metaUrl = $"https://catalog.test/repo/entries/{Slug}/{Slug}.meta.json";
            var indexJson = $$"""
                { "generatedAt": "2026-08-11", "entries": [
                  { "slug": "{{Slug}}", "kind": "show", "audience": "everyone",
                    "manifest": { "path": "entries/{{Slug}}/{{Slug}}.show.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
                    "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" } } ] }
                """;
            var handler = new FakeHttpMessageHandler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri switch
            {
                IndexUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(indexJson, Encoding.UTF8, "application/json") },
                _ when request.RequestUri!.AbsoluteUri == manifestUrl =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ManifestJson, Encoding.UTF8, "application/json") },
                _ when request.RequestUri!.AbsoluteUri == metaUrl =>
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MetaJson, Encoding.UTF8, "application/json") },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            }));
            await using var factory = new ShowImportWebFactory(new FakeShowStore(), IndexUrl, handler);
            var client = await ShowImportWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/entries/{slug} is called through the real production route,
            var response = await client.GetAsync($"/api/catalog/entries/{Slug}");
            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();

            // Then the entry's kind is "show" and its meta-sourced suggestedPersona reaches the wire
            // (F118.3) — the shelf/detail projection this task's own kind seam adds.
            Assert.Equal(
                (Status: HttpStatusCode.OK, Kind: "show", SuggestedPersona: "example-dj"),
                (Status: response.StatusCode, body!.Kind, body.SuggestedPersona));
        }

        static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
