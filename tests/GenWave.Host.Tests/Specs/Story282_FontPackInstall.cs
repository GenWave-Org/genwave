// STORY-282 — Install a pack into the library (SPEC F104.5 · PLAN T198/T199)
//
// BDD specification — xUnit. POST /api/fonts/{slug}/install fetches a Dean-curated font pack's
// assets server-side through the guarded door (CatalogProxyService), hash-verifies every one against
// the catalog index, and upserts station.font_pack(+_face) — no request body, no file-upload path
// (SPEC F104.5).
//
// WIRED T199 — every Fact below drives the real production route through WebApplicationFactory<Program>
// (real routing/auth/content-negotiation pipeline) against a fake catalog origin (mirrors
// Story279_FontKindAssets.cs's own FontShelfWebFactory idiom) and FakeFontPackStore (mirrors
// Story272_ThemeImport.cs's own FakeThemeStore idiom — this project has no Postgres fixture; the
// REAL station.font_pack_face SQL, including the true no-partial-installs rollback, is proven against
// real Postgres in GenWave.MediaLibrary.Tests/Specs/Story282_FontPackRepository.cs instead).
//
// ScenarioCrossPackFileCollision and ScenarioTheDdlStaysInSync are the two T198 review-obligation
// riders this task adds beyond STORY-282's own committed AC4 sad path: the former pins
// FontPackController's 23505-to-409 mapping (scripted via FakeFontPackStore.NextThrow, mirrors
// FakeScheduleStore's own precedent for ScheduleController's PostgresException handling — this
// project has no Postgres fixture to raise a real 23505 against either); the latter is a plain
// script-content comparison (no DB needed) pinning that db/32's standalone in-place-upgrade DDL and
// db/06's inline fresh-install DDL never silently diverge on this table's shape. Two more are T199
// REVIEW riders: ScenarioPackByteCeilingCutsOffEarly (finding N1) pins that the app-side pack-bytes
// ceiling refuses the INSTANT the running total crosses it, mid-fetch — never only after every
// declared asset is already buffered — by proving an asset past the tipping point was never even
// requested; ScenarioDuplicateManifestFileEntry (finding N2) pins that a manifest listing the same
// file twice in files[] is refused with a precise 400 naming the duplicate BEFORE it ever reaches
// the store, rather than surfacing as a misleading generic 409 off the real unique constraint.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// (AC4 plus the four riders) is its own block.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;
using Npgsql;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureFontPackInstall
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioInstallIsTransactionalAndHashPinned
    {
        [Fact]
        public async Task ARealInstallPostVerifiesEverySha256AgainstTheIndex()
        {
            // Given a fake origin serving the golden pack with its own REAL sha256s throughout
            // (index, manifest, meta, asset — FontPackInstallFixtures computes every hash from the
            // actual served content),
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(store);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When POST /api/fonts/{slug}/install is called (the real production route),
            var response = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);

            // Then it responds success, and the stored face's own bytes/sha256/size exactly match the
            // golden woff2's real hash-verified content — proving the sha256 was verified against the
            // index rather than merely copied through.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var pack = Assert.Single(await store.GetAllAsync(CancellationToken.None));
            var face = Assert.Single(pack.Faces);
            var goldenBytes = FontFixtureFiles.ReadWoff2Bytes();
            Assert.Equal(
                (File: FontPackInstallFixtures.AssetFile, Sha256: FontPackInstallFixtures.AssetSha256, ByteSize: goldenBytes.Length),
                (face.File, face.Sha256, face.ByteSize));
        }

        [Fact]
        public async Task TheUpsertOfPackAndFacesIsOneTransactionWithNoPartialState()
        {
            // Given a fresh install,
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(store);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When it completes,
            var response = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the pack row and its face(s) arrive through exactly ONE store write —
            // IFontPackStore.UpsertAsync's own contract (SPEC F104 "Data model") is a single call
            // carrying both together, never a pack-row-then-faces sequence a partial failure could
            // split. The REAL no-partial-installs guarantee (a mid-write Postgres failure rolling back
            // everything already written) is proven against real Postgres in
            // GenWave.MediaLibrary.Tests/Specs/Story282_FontPackRepository.cs — a fake in-memory store
            // cannot honestly repeat that proof, only this call-shape half of it.
            Assert.Equal(1, store.UpsertCallCount);
        }
    }

    public sealed class ScenarioProvenanceRecordsTheDoor
    {
        [Fact]
        public async Task ImportedFromCarriesTheCatalogSlugAndImportedAtTheTime()
        {
            // Given a completed install,
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(store);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);
            var before = DateTime.UtcNow;

            var response = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // When the row is read,
            var pack = Assert.Single(await store.GetAllAsync(CancellationToken.None));

            // Then imported_from carries the catalog slug and imported_at the time (AC2) — packs have
            // no other path.
            Assert.Equal(
                (ImportedFrom: FontPackInstallFixtures.FontSlug, ImportedAtIsRecent: true),
                (pack.ImportedFrom, ImportedAtIsRecent: pack.ImportedAt >= before && pack.ImportedAt <= DateTime.UtcNow));
        }
    }

    public sealed class ScenarioReinstallUpserts
    {
        [Fact]
        public async Task InstallingTheSameSlugAgainReplacesRatherThanDuplicates()
        {
            // Given the same slug installed once already,
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(store);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);
            var first = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When it is installed again,
            var second = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);

            // Then the install completes and the pack's rows are replaced, not duplicated (AC3).
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            Assert.Single(await store.GetAllAsync(CancellationToken.None));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioRejectingBadInstalls
    {
        [Fact]
        public async Task AnUnknownSlugRefusesWithNothingStored()
        {
            // Given a fake origin whose index never declares this slug at all,
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(store);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync("/api/fonts/no-such-pack/install", null);

            // Then it responds 404 and nothing is stored (AC4).
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ADisabledCatalogRefusesWithTheKillSwitchPosture()
        {
            // Given the catalog kill switch (an empty Community:CatalogIndexUrl, SPEC F90.1),
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(store, catalogIndexUrl: "");
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);

            // Then it responds a bare 404 (the same "surface does not exist" posture
            // CatalogController's own routes carry — never a ProblemDetails body) and nothing is
            // stored (AC4).
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AHashMismatchRefusesFailClosedWithNothingStored()
        {
            // Given the SAME index — still declaring the golden woff2's REAL sha256 — but an origin
            // whose actual asset response is corrupted (a tampered/broken upstream),
            var corruptedBytes = "not the real font bytes"u8.ToArray();
            var store = new FakeFontPackStore();
            var handler = FontPackInstallFixtures.BuildRoutedHandler(assetBytesOverride: corruptedBytes);
            await using var factory = new FontPackInstallWebFactory(store, handler);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);

            // Then it is withheld with the integrity posture (502) and nothing is stored (AC4).
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    // ── T198 REVIEW-OBLIGATION RIDER #1: the 23505 mapping ─────────────────

    public sealed class ScenarioCrossPackFileCollision
    {
        [Fact]
        public async Task AFilenameAlreadyInstalledByAnotherPackRefusesWith409NamingTheFileAndOwningPack()
        {
            // Given a face filename already installed under a DIFFERENT pack's slug
            // (station.font_pack_face.file is globally unique, not scoped per-pack — db/32) — the
            // real store would raise Postgres 23505 here; FakeFontPackStore.NextThrow scripts that
            // EXACT exception (mirrors FakeScheduleStore's own precedent for ScheduleController's
            // PostgresException handling — this project has no Postgres fixture to raise a real one
            // against),
            var existingFace = new FontPackFace(FontPackInstallFixtures.AssetFile, "normal", 100, "existing-face-sha");
            var otherPack = new FontPack(
                "other-pack", "Other Family", "{}", "other-pack", DateTime.UtcNow, DateTime.UtcNow, [existingFace]);
            var store = new FakeFontPackStore(otherPack)
            {
                NextThrow = new PostgresException(
                    messageText: "duplicate key value violates unique constraint \"font_pack_face_file_key\"",
                    severity: "ERROR",
                    invariantSeverity: "ERROR",
                    sqlState: "23505",
                    detail: $"Key (file)=({FontPackInstallFixtures.AssetFile}) already exists.",
                    hint: null,
                    position: 0,
                    internalPosition: 0,
                    internalQuery: null,
                    where: null,
                    schemaName: "station",
                    tableName: "font_pack_face",
                    columnName: null,
                    dataTypeName: null,
                    constraintName: "font_pack_face_file_key",
                    file: null,
                    line: null,
                    routine: null),
            };
            await using var factory = new FontPackInstallWebFactory(store);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When installing a DIFFERENT pack whose own face happens to share that same filename,
            var response = await client.PostAsync($"/api/fonts/{FontPackInstallFixtures.FontSlug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it responds 409, naming the file and its owning pack — never the raw Postgres
            // constraint/detail text — and no second pack row lands (only the pre-seeded one remains).
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(FontPackInstallFixtures.AssetFile, body, StringComparison.Ordinal);
            Assert.Contains("other-pack", body, StringComparison.Ordinal);
            Assert.DoesNotContain("font_pack_face_file_key", body, StringComparison.Ordinal);
            Assert.Single(await store.GetAllAsync(CancellationToken.None));
        }
    }

    // ── T199 REVIEW FINDING N2: a manifest listing the same file twice ─────────

    public sealed class ScenarioDuplicateManifestFileEntry
    {
        [Fact]
        public async Task AManifestListingTheSameFileTwiceRefusesWith400NamingTheDuplicate()
        {
            // Given a manifest whose files[] lists the SAME underlying asset twice (e.g. mistakenly
            // declared for both an upright and an italic role) — station.font_pack_face.file is
            // globally UNIQUE (db/32, the same constraint ScenarioCrossPackFileCollision above pins
            // for a CROSS-pack clash), so reaching the store with this shape would die there too, but
            // as a real Postgres 23505 with no OTHER pack actually owning the file — a misleading
            // generic 409 rather than a precise refusal naming the actual problem (review finding N2),
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(
                store, DuplicateManifestFixtures.BuildRoutedHandler(), DuplicateManifestFixtures.IndexUrl);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted (the real production route),
            var response = await client.PostAsync($"/api/fonts/{DuplicateManifestFixtures.Slug}/install", null);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused with a precise 400 naming the duplicated file, before anything
            // reaches the store.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(FontPackInstallFixtures.AssetFile, body, StringComparison.Ordinal);
            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        }
    }

    // ── T198 REVIEW-OBLIGATION RIDER #2: the db/32-vs-db/06 DDL drift detector ─

    public sealed class ScenarioTheDdlStaysInSync
    {
        [Fact]
        public void Db32AndDb06DeclareTheIdenticalFontPackTables()
        {
            // T198 review carry-forward: db/32-font-pack-migration.sh is the STANDALONE in-place
            // upgrade script for an existing install; db/06-station-settings-migration.sh is the
            // fresh-install bootstrap that ALSO creates station.font_pack(+_face) inline (a fresh
            // install never runs db/32 at all — see db/06's own remarks). The two are not
            // byte-identical by design (different inline comments, and db/32 uses lowercase SQL
            // keywords while db/06 uses uppercase, verified by inspection) — this pins that they stay
            // column-for-column, constraint-for-constraint identical once comments/keyword case are
            // normalised away. Cheap at this altitude: plain regex extraction over two committed shell
            // scripts, no database needed.
            var db32 = ReadDbScript("32-font-pack-migration.sh");
            var db06 = ReadDbScript("06-station-settings-migration.sh");

            Assert.Equal(
                (Pack: NormalizeCreateTable(db32, "font_pack"), Face: NormalizeCreateTable(db32, "font_pack_face")),
                (Pack: NormalizeCreateTable(db06, "font_pack"), Face: NormalizeCreateTable(db06, "font_pack_face")));
        }

        static string ReadDbScript(string fileName) => File.ReadAllText(Path.Combine(RepoRoot(), "db", fileName));

        static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
                dir = dir.Parent;
            if (dir is null)
                throw new InvalidOperationException("repo root (GenWave.sln) not found");

            return dir.FullName;
        }

        /// <summary>
        /// Extracts ONE <c>CREATE TABLE IF NOT EXISTS station.&lt;tableName&gt; ( … );</c> block and
        /// normalises away everything the two scripts are allowed to differ on (line comments, keyword
        /// case, incidental whitespace) — leaving only the column/constraint shape a real schema drift
        /// would actually change.
        /// </summary>
        static string NormalizeCreateTable(string script, string tableName)
        {
            var match = Regex.Match(
                script,
                $@"create\s+table\s+if\s+not\s+exists\s+station\.{Regex.Escape(tableName)}\s*\((?<body>.*?)\n\t?\);",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
                throw new InvalidOperationException($"no CREATE TABLE IF NOT EXISTS station.{tableName} found");

            var withoutComments = Regex.Replace(match.Groups["body"].Value, "--[^\n]*", "");
            return Regex.Replace(withoutComments, @"\s+", " ").Trim().ToLowerInvariant();
        }
    }

    // ── T199 REVIEW FINDING N1: the pack-bytes ceiling cuts off EARLY ──────────

    public sealed class ScenarioPackByteCeilingCutsOffEarly
    {
        [Fact]
        public async Task ThePackCeilingRefusesTheInstantTheRunningTotalCrossesItWithoutFetchingWhatFollows()
        {
            // Given an index declaring three assets under one pack: the first two together push the
            // running total past MaxPackBytes (200 KiB) the moment the SECOND is fetched, and a THIRD
            // — its "successor" in fetch order — that only a "sum the total after every asset is
            // already fetched" implementation would go on to request too (review finding N1: the
            // pre-fix behaviour buffered every declared asset, however many, before ever checking the
            // ceiling),
            var store = new FakeFontPackStore();
            var handler = PackByteCeilingFixtures.BuildRoutedHandler();
            await using var factory = new FontPackInstallWebFactory(store, handler, PackByteCeilingFixtures.IndexUrl);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            await client.PostAsync($"/api/fonts/{PackByteCeilingFixtures.Slug}/install", null);

            // Then the successor's own URL was NEVER requested — the early-cutoff proof itself,
            // read off the fake handler's own recorded request log rather than inferred from the
            // response alone (the response's own status/storage effect is the sibling Fact below —
            // one assertion per Fact).
            Assert.DoesNotContain(
                handler.Requests, request => request.RequestUri!.AbsoluteUri == PackByteCeilingFixtures.SuccessorAssetUrl);
        }

        [Fact]
        public async Task ThePackCeilingRefusalIs400WithNothingStored()
        {
            // Given the same over-ceiling pack,
            var store = new FakeFontPackStore();
            await using var factory = new FontPackInstallWebFactory(
                store, PackByteCeilingFixtures.BuildRoutedHandler(), PackByteCeilingFixtures.IndexUrl);
            var client = await FontPackInstallWebFactory.LoggedInClientAsync(factory);

            // When install is attempted,
            var response = await client.PostAsync($"/api/fonts/{PackByteCeilingFixtures.Slug}/install", null);

            // Then it is refused as over the ceiling (400, PackTooLargeProblem) and nothing is stored
            // (the SAME "fail closed, nothing partial" posture AC4's own sad-path Facts pin for every
            // other refusal shape).
            Assert.Equal(
                (HttpStatusCode.BadRequest, 0),
                (response.StatusCode, (await store.GetAllAsync(CancellationToken.None)).Count));
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — boots the real
/// Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at
/// <see cref="FontPackInstallFixtures.IndexUrl"/> (served by a fake origin, mirrors
/// Story279_FontKindAssets.cs's own <c>FontShelfWebFactory</c>) and <see cref="IFontPackStore"/>
/// replaced by a <see cref="FakeFontPackStore"/> (mirrors Story272_ThemeImport.cs's own
/// <c>ThemeImportWebFactory</c>).
/// </summary>
file sealed class FontPackInstallWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story282-fontinstall";

    readonly FakeHttpMessageHandler handler;
    readonly FakeFontPackStore store;
    readonly string catalogIndexUrl;

    public FontPackInstallWebFactory(
        FakeFontPackStore? store = null, FakeHttpMessageHandler? handler = null, string catalogIndexUrl = FontPackInstallFixtures.IndexUrl)
    {
        this.store = store ?? new FakeFontPackStore();
        this.handler = handler ?? FontPackInstallFixtures.BuildRoutedHandler();
        this.catalogIndexUrl = catalogIndexUrl;
    }

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
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

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
/// Locates and reads this story's committed <c>Fixtures/</c> files from their SOURCE location (not a
/// build output copy) — mirrors <c>Story279_FontKindAssets.cs</c>'s own <c>FontFixtureFiles</c> idiom
/// (itself <c>file</c>-scoped, so this file needs its own copy).
/// </summary>
file static class FontFixtureFiles
{
    public static string ReadManifestText() => File.ReadAllText(LocatePath("golden.font.json"));

    public static byte[] ReadWoff2Bytes() => File.ReadAllBytes(LocatePath("golden-font.woff2"));

    static string LocatePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("repo root (GenWave.sln) not found");

        return Path.Combine(dir.FullName, "tests", "GenWave.Host.Tests", "Fixtures", fileName);
    }
}

/// <summary>
/// Fixture documents + a routed fake HTTP double for this file's own Facts — a single valid
/// kind:"font" entry (the golden Space Grotesk pack, T193/T194's own fixture), every sha256 computed
/// from the served content itself so the happy path fetches and hash-verifies cleanly with no extra
/// setup. <c>file</c>-scoped (this file's own established idiom, see <see cref="FontFixtureFiles"/>
/// above).
/// </summary>
file static class FontPackInstallFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string FontSlug = "space-grotesk";

    // Must equal golden.font.json's own upright files[].file name — the golden fixture's one
    // committed face.
    public const string AssetFile = "space-grotesk-variable-latin.woff2";

    // The golden woff2's real sha256 (PLAN T193) — the SAME recorded value
    // Story279_FontKindAssets.cs's own ScenarioGoldenParityFixtures pins independently.
    public const string AssetSha256 = "4f8000489733987cfe711fb469bd932a3024290bea8bc44151f6807f588932ee";

    public static string FontManifestJson => FontFixtureFiles.ReadManifestText();

    public static string FontMetaJson => """
        {
          "author": "Test Fixture",
          "description": "A curated font pack for the install endpoint specs.",
          "audience": "everyone",
          "added": "2026-08-05"
        }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string IndexJson() => $$"""
        { "generatedAt": "2026-08-05", "entries": [
          { "slug": "{{FontSlug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{FontSlug}}/{{FontSlug}}.font.json", "sha256": "{{Sha256Hex(FontManifestJson)}}" },
            "meta": { "path": "entries/{{FontSlug}}/{{FontSlug}}.meta.json", "sha256": "{{Sha256Hex(FontMetaJson)}}" },
            "assets": [
              { "path": "entries/{{FontSlug}}/{{AssetFile}}", "sha256": "{{AssetSha256}}", "bytes": 7844 }
            ] } ] }
        """;

    /// <summary>
    /// Serves every fixture document at its own resolved URL, 404 for anything else. The one binary
    /// asset URL serves <paramref name="assetBytesOverride"/> when given —
    /// <c>AHashMismatchRefusesFailClosedWithNothingStored</c> uses this to make the ORIGIN's real
    /// response diverge from <see cref="IndexJson"/>'s own declared <see cref="AssetSha256"/> (which
    /// always stays the golden woff2's REAL hash) — otherwise the golden woff2's real bytes, so the
    /// happy-path Facts get a hash-verified, correctly-sized asset with no extra setup.
    /// </summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(byte[]? assetBytesOverride = null)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + FontSlug + "/" + FontSlug + ".font.json"] = FontManifestJson,
            [Directory + "entries/" + FontSlug + "/" + FontSlug + ".meta.json"] = FontMetaJson,
        };
        var assetUrl = Directory + "entries/" + FontSlug + "/" + AssetFile;
        var assetBytes = assetBytesOverride ?? FontFixtureFiles.ReadWoff2Bytes();

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>
/// Fixture for <c>ScenarioDuplicateManifestFileEntry</c> (T199 review finding N2) — the SAME golden
/// woff2 asset (real bytes/hash, reusing <see cref="FontPackInstallFixtures.AssetFile"/>/<see cref="FontPackInstallFixtures.AssetSha256"/>)
/// declared exactly ONCE on the index's own <c>assets[]</c> (a single, honestly-fetchable asset), but
/// TWICE inside the manifest's own <c>files[]</c> (an upright and an "italic" role both wrongly naming
/// it) — the malformed shape lives in the manifest text alone, never in what the index or origin
/// actually serves.
/// </summary>
file static class DuplicateManifestFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/duplicate-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "duplicate-pack";

    static string ManifestJson => $$"""
        {"family":"Duplicate Family","files":[
          {"role":"upright","file":"{{FontPackInstallFixtures.AssetFile}}","weight":"400","style":"normal","bytes":7844},
          {"role":"italic","file":"{{FontPackInstallFixtures.AssetFile}}","weight":"400","style":"italic","bytes":7844}
        ],"license":"OFL-1.1","sourceUrl":"https://example.test/duplicate","version":"1.0","subset":"text"}
        """;

    const string MetaJson = """
        {
          "author": "Test Fixture",
          "description": "A malformed pack whose manifest lists the same file twice.",
          "audience": "everyone",
          "added": "2026-08-05"
        }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-05", "entries": [
          { "slug": "{{Slug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{FontPackInstallFixtures.AssetFile}}", "sha256": "{{FontPackInstallFixtures.AssetSha256}}", "bytes": 7844 }
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
        var assetUrl = Directory + "entries/" + Slug + "/" + FontPackInstallFixtures.AssetFile;
        var assetBytes = FontFixtureFiles.ReadWoff2Bytes();

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>
/// Fixture for <c>ScenarioPackByteCeilingCutsOffEarly</c> (T199 review finding N1) — an entry
/// declaring THREE binary assets: the first two (150,000 bytes apiece — each individually well under
/// both <c>CatalogProxyService.MaxAssetBytes</c>, 256 KiB, and <c>FontPackController.MaxPackBytes</c>,
/// 200 KiB) together push the running total to 300,000 bytes the instant the SECOND is fetched, and a
/// third — its "successor" in <c>content.Assets</c>' own declared order — whose own URL this fixture's
/// Facts assert was never requested at all. None of these need to be real font/manifest content (the
/// manifest/meta text is never even parsed — <see cref="FontPackController.FetchAllAssetsAsync"/>'s own
/// ceiling check runs, and refuses, strictly before <c>CatalogFontManifestSerializer.Deserialize</c>
/// ever sees the manifest body), so this fixture is deliberately independent of
/// <see cref="FontPackInstallFixtures"/>'s own golden Space Grotesk content.
/// </summary>
file static class PackByteCeilingFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/ceiling-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "big-pack";

    const string ManifestJson = "{}";
    const string MetaJson = "{}";

    const string AssetAFile = "face-a.woff2";
    const string AssetBFile = "face-b.woff2";
    const string AssetCFile = "face-c.woff2"; // the successor — must never be requested

    // Each individually under MaxAssetBytes (256 KiB) and under MaxPackBytes (200 KiB) alone; only
    // their SUM crosses the 200 KiB pack ceiling, and only once the second one is fetched.
    static readonly byte[] AssetABytes = Filler(0xAA, 150_000);
    static readonly byte[] AssetBBytes = Filler(0xBB, 150_000);
    // Small and never legitimately reachable — its distinct fill byte is irrelevant, since the early-
    // cutoff proof is "was this URL ever requested at all", not a content comparison.
    static readonly byte[] AssetCBytes = Filler(0xCC, 1_000);

    public static string SuccessorAssetUrl => Directory + "entries/" + Slug + "/" + AssetCFile;

    static byte[] Filler(byte value, int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-05", "entries": [
          { "slug": "{{Slug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{AssetAFile}}", "sha256": "{{Sha256Hex(AssetABytes)}}", "bytes": {{AssetABytes.Length}} },
              { "path": "entries/{{Slug}}/{{AssetBFile}}", "sha256": "{{Sha256Hex(AssetBBytes)}}", "bytes": {{AssetBBytes.Length}} },
              { "path": "entries/{{Slug}}/{{AssetCFile}}", "sha256": "{{Sha256Hex(AssetCBytes)}}", "bytes": {{AssetCBytes.Length}} }
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
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Directory + "entries/" + Slug + "/" + AssetAFile] = AssetABytes,
            [Directory + "entries/" + Slug + "/" + AssetBFile] = AssetBBytes,
            [Directory + "entries/" + Slug + "/" + AssetCFile] = AssetCBytes,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
