// STORY-399/401 — A reinstall that drops a title (SPEC F165.5/F165.6 · PLAN T414 review round 2
// findings F3, F4, F6.4)
//
// F3+F4 (HIGH): JinglePackController.Install's own orphan cleanup (the `previousPaths` vs `written`
// diff run after a successful upsert — see that method's own remarks) and
// JinglePackRepository.UpsertAsync's own cross-schema station.ad_spot.bed_media_id guard firing on a
// DROPPED title specifically (as opposed to F165.6's already-covered UNINSTALL guard,
// Story401_PackUninstallGuards.cs) had no fact. F6.4 (part of finding F6) rides the same clean-drop
// arc: a KEPT title's reinstall keeps its OWN media id (AssetUpsertSql's own
// ON CONFLICT (pack_slug, title) DO UPDATE ... RETURNING id) even though its bytes genuinely changed.
//
// Two real-Postgres, two-WebApplicationFactory-instance-per-slug arcs, one ephemeral Postgres shared
// between them (mirrors Story401_PackUninstallGuards.cs's own JinglePackUninstallArc "one Postgres,
// several guarded scenarios" idiom) — but EACH reinstall still needs its own FRESH factory instance:
// CatalogProxyService's own 15-minute index/entry cache would otherwise replay the FIRST install's
// cached manifest/asset bytes against the SAME URLs (Story395_VoicePackInstall.cs's own cancellation
// fact carries the same remark).

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureJinglePackReinstallDropsATitle
{
    [Collection(JinglePackReinstallOrphansCollection.Name)]
    public sealed class ScenarioReinstallDroppingATitleCleansUpItsFile(JinglePackReinstallOrphansArc arc)
    {
        [Fact]
        public void TheSecondInstallSucceeds() => Assert.Equal(HttpStatusCode.OK, arc.CleanDropSecondStatus);

        [Fact]
        public void TheDroppedTitlesFileIsGone() => Assert.True(arc.CleanDropDroppedFileGone);

        [Fact]
        public void TheKeptTitlesFileHoldsTheNewBytesNotTheOld() => Assert.True(arc.CleanDropKeptFileHoldsTheNewBytes);

        [Fact]
        public void TheKeptTitleKeepsItsOriginalMediaId() =>
            Assert.Equal(arc.CleanDropFirstKeptMediaId, arc.CleanDropSecondKeptMediaId);

        [Fact]
        public void ExactlyOneRowSurvivesForTheSlug() => Assert.Equal(1, arc.CleanDropSurvivingRowCount);

        [Fact]
        public void NoPrevSiblingSurvives() => Assert.True(arc.CleanDropNoPrevSiblingSurvives);
    }

    [Collection(JinglePackReinstallOrphansCollection.Name)]
    public sealed class ScenarioReinstallDroppingAReferencedTitleRefuses(JinglePackReinstallOrphansArc arc)
    {
        [Fact]
        public void TheSecondInstallReturns409() => Assert.Equal(HttpStatusCode.Conflict, arc.RefusedDropSecondStatus);

        [Fact]
        public void The409BodyCarriesTheInUseTypeAndTheReferencingAdSpotId()
        {
            Assert.Contains("\"jingle_pack_in_use\"", arc.RefusedDropSecondBody, StringComparison.Ordinal);
            Assert.Contains(arc.RefusedDropAdSpotId.ToString(), arc.RefusedDropSecondBody, StringComparison.Ordinal);
        }

        [Fact]
        public void BothFilesSurviveWithTheirOriginalBytes()
        {
            Assert.True(arc.RefusedDropKeptFileUnchanged);
            Assert.True(arc.RefusedDropDroppedFileUnchanged);
        }

        [Fact]
        public void NoPrevSiblingSurvives() => Assert.True(arc.RefusedDropNoPrevSiblingSurvives);

        [Fact]
        public void BothRowsSurviveUnchanged() => Assert.Equal(2, arc.RefusedDropSurvivingRowCount);
    }
}

// ── The DB-backed arc — one real Postgres, four factory instances (two per slug), two slugs ────────

[CollectionDefinition(Name)]
public sealed class JinglePackReinstallOrphansCollection : ICollectionFixture<JinglePackReinstallOrphansArc>
{
    public const string Name = "Story399JinglePackReinstallOrphans";
}

/// <summary>
/// Arranges every DB-backed fact this file's own Scenarios read (the <see cref="JinglePackUninstallArc"/>
/// idiom — Story401_PackUninstallGuards.cs — one real Postgres, several distinctly-slugged
/// arrangements): boots ONE real ephemeral Postgres, seeds the <c>ads</c> library, then runs TWO
/// independent two-install arcs against two distinct slugs — <c>reinstall-drop-clean</c> (a dropped
/// title with no active reference: the file must be unlinked and the kept title's row must keep its
/// own id) and <c>reinstall-drop-refused</c> (the dropped title is referenced by an active
/// <c>station.ad_spot.bed_media_id</c>: the whole reinstall must be refused, leaving both files and
/// both rows exactly as the first install left them).
/// </summary>
public sealed class JinglePackReinstallOrphansArc : IAsyncLifetime
{
    public HttpStatusCode CleanDropSecondStatus { get; private set; }
    public bool CleanDropDroppedFileGone { get; private set; }
    public bool CleanDropKeptFileHoldsTheNewBytes { get; private set; }
    public bool CleanDropNoPrevSiblingSurvives { get; private set; }
    public long CleanDropFirstKeptMediaId { get; private set; }
    public long CleanDropSecondKeptMediaId { get; private set; }
    public int CleanDropSurvivingRowCount { get; private set; }

    public HttpStatusCode RefusedDropSecondStatus { get; private set; }
    public string RefusedDropSecondBody { get; private set; } = "";
    public long RefusedDropAdSpotId { get; private set; }
    public bool RefusedDropKeptFileUnchanged { get; private set; }
    public bool RefusedDropDroppedFileUnchanged { get; private set; }
    public bool RefusedDropNoPrevSiblingSurvives { get; private set; }
    public int RefusedDropSurvivingRowCount { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await JinglePackReinstallOrphansDatabase.StartAsync();
        using var jingleRootDir = new TempDir();
        var jingleRoot = jingleRootDir.Path;
        await SeedAdsLibraryAsync(database.LibraryConnectionString);

        await RunCleanDropAsync(database, jingleRoot);
        await RunRefusedDropAsync(database, jingleRoot);
    }

    async Task RunCleanDropAsync(JinglePackReinstallOrphansDatabase database, string jingleRoot)
    {
        const string slug = "reinstall-drop-clean";
        var assetDir = JingleTestAudio.NewTempDir();
        try
        {
            var keptPath = JingleTestAudio.CreateBedShapedTone(assetDir, "kept.wav");
            var droppedPath = JingleTestAudio.CreateBedShapedTone(
                assetDir, "dropped.wav", leadingSilenceSec: 1.5, toneSec: 2.5, trailingSilenceSec: 1.5);
            var firstAssets = new[]
            {
                ("Kept Bed", "bed", "kept.wav", File.ReadAllBytes(keptPath)),
                ("Dropped Sting", "sting", "dropped.wav", File.ReadAllBytes(droppedPath)),
            };

            await using (var firstFactory = new JinglePackReinstallOrphansWebFactory(database, jingleRoot, slug, firstAssets))
            {
                var firstClient = await JinglePackReinstallOrphansWebFactory.LoggedInClientAsync(firstFactory);
                var first = await firstClient.PostAsync($"/api/jingle-packs/{slug}/install", null);
                if (!first.IsSuccessStatusCode)
                    throw new InvalidOperationException($"fixture first install of '{slug}' failed: {await first.Content.ReadAsStringAsync()}");
            }

            var firstRows = await ReadRowsAsync(database.LibraryConnectionString, slug);
            CleanDropFirstKeptMediaId = firstRows.Single(r => r.Title == "Kept Bed").Id;

            var keptFilePath = Path.Combine(jingleRoot, slug, "kept.wav");
            var droppedFilePath = Path.Combine(jingleRoot, slug, "dropped.wav");
            var firstKeptBytesHash = Sha256File(keptFilePath);

            var newKeptPath = JingleTestAudio.CreateBedShapedTone(
                assetDir, "kept-v2.wav", leadingSilenceSec: 2.0, toneSec: 4.0, trailingSilenceSec: 2.0);
            var newKeptBytes = File.ReadAllBytes(newKeptPath);
            var secondAssets = new[] { ("Kept Bed", "bed", "kept.wav", newKeptBytes) };
            var secondKeptBytesHash = Convert.ToHexStringLower(SHA256.HashData(newKeptBytes));

            await using (var secondFactory = new JinglePackReinstallOrphansWebFactory(database, jingleRoot, slug, secondAssets))
            {
                var secondClient = await JinglePackReinstallOrphansWebFactory.LoggedInClientAsync(secondFactory);
                var second = await secondClient.PostAsync($"/api/jingle-packs/{slug}/install", null);
                CleanDropSecondStatus = second.StatusCode;
                if (!second.IsSuccessStatusCode)
                    throw new InvalidOperationException($"second install of '{slug}' failed: {await second.Content.ReadAsStringAsync()}");
            }

            CleanDropDroppedFileGone = !File.Exists(droppedFilePath);
            CleanDropKeptFileHoldsTheNewBytes =
                Sha256File(keptFilePath) == secondKeptBytesHash && secondKeptBytesHash != firstKeptBytesHash;
            CleanDropNoPrevSiblingSurvives =
                !Directory.EnumerateFiles(Path.GetDirectoryName(keptFilePath)!, "*.prev-*").Any();

            var secondRows = await ReadRowsAsync(database.LibraryConnectionString, slug);
            CleanDropSurvivingRowCount = secondRows.Count;
            CleanDropSecondKeptMediaId = secondRows.Single(r => r.Title == "Kept Bed").Id;
        }
        finally
        {
            try { Directory.Delete(assetDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    async Task RunRefusedDropAsync(JinglePackReinstallOrphansDatabase database, string jingleRoot)
    {
        const string slug = "reinstall-drop-refused";
        var assetDir = JingleTestAudio.NewTempDir();
        try
        {
            var keptPath = JingleTestAudio.CreateBedShapedTone(assetDir, "kept.wav");
            var droppedPath = JingleTestAudio.CreateBedShapedTone(
                assetDir, "dropped.wav", leadingSilenceSec: 1.5, toneSec: 2.5, trailingSilenceSec: 1.5);
            var firstAssets = new[]
            {
                ("Kept Bed", "bed", "kept.wav", File.ReadAllBytes(keptPath)),
                ("Dropped Sting", "sting", "dropped.wav", File.ReadAllBytes(droppedPath)),
            };

            await using (var firstFactory = new JinglePackReinstallOrphansWebFactory(database, jingleRoot, slug, firstAssets))
            {
                var firstClient = await JinglePackReinstallOrphansWebFactory.LoggedInClientAsync(firstFactory);
                var first = await firstClient.PostAsync($"/api/jingle-packs/{slug}/install", null);
                if (!first.IsSuccessStatusCode)
                    throw new InvalidOperationException($"fixture first install of '{slug}' failed: {await first.Content.ReadAsStringAsync()}");
            }

            var firstRows = await ReadRowsAsync(database.LibraryConnectionString, slug);
            var droppedMediaId = firstRows.Single(r => r.Title == "Dropped Sting").Id;

            var keptFilePath = Path.Combine(jingleRoot, slug, "kept.wav");
            var droppedFilePath = Path.Combine(jingleRoot, slug, "dropped.wav");
            var firstKeptBytesHash = Sha256File(keptFilePath);
            var firstDroppedBytesHash = Sha256File(droppedFilePath);

            RefusedDropAdSpotId = await InsertAdSpotAsync(database.StationConnectionString, droppedMediaId);

            var newKeptPath = JingleTestAudio.CreateBedShapedTone(
                assetDir, "kept-v2.wav", leadingSilenceSec: 2.0, toneSec: 4.0, trailingSilenceSec: 2.0);
            var secondAssets = new[] { ("Kept Bed", "bed", "kept.wav", File.ReadAllBytes(newKeptPath)) };

            await using (var secondFactory = new JinglePackReinstallOrphansWebFactory(database, jingleRoot, slug, secondAssets))
            {
                var secondClient = await JinglePackReinstallOrphansWebFactory.LoggedInClientAsync(secondFactory);
                var second = await secondClient.PostAsync($"/api/jingle-packs/{slug}/install", null);
                RefusedDropSecondStatus = second.StatusCode;
                RefusedDropSecondBody = await second.Content.ReadAsStringAsync();
            }

            RefusedDropKeptFileUnchanged = Sha256File(keptFilePath) == firstKeptBytesHash;
            RefusedDropDroppedFileUnchanged = Sha256File(droppedFilePath) == firstDroppedBytesHash;
            RefusedDropNoPrevSiblingSurvives =
                !Directory.EnumerateFiles(Path.GetDirectoryName(keptFilePath)!, "*.prev-*").Any();

            var secondRows = await ReadRowsAsync(database.LibraryConnectionString, slug);
            RefusedDropSurvivingRowCount = secondRows.Count;
        }
        finally
        {
            try { Directory.Delete(assetDir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    static string Sha256File(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    static async Task SeedAdsLibraryAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        // db/01's own seed guarantees 'default' is id=1; 'ads' — the only other row this arc ever
        // inserts, into a fresh ephemeral database — is deterministically id=2 (mirrors
        // Story399_JinglePackInstall.cs's own SeedAdsLibraryAsync remarks).
        await conn.ExecuteAsync("insert into library.library (name) values ('ads')");
    }

    static async Task<IReadOnlyList<(long Id, string Title)>> ReadRowsAsync(string libraryConnectionString, string slug)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<(long Id, string Title)>(
            "select id, title from library.media where pack_slug = @slug order by title", new { slug });
        return rows.ToList();
    }

    static async Task<long> InsertAdSpotAsync(string stationConnectionString, long bedMediaId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            with sponsor as (
              insert into station.sponsor (name) values ('Test Brand') returning id
            )
            insert into station.ad_spot (sponsor_id, sponsor_name, title, source, state, bed_media_id)
            select sponsor.id, 'Test Brand', 'Test Spot', 'owner'::station.ad_source, 'approved'::station.ad_state, @BedMediaId
            from sponsor
            returning id
            """,
            new { BedMediaId = bedMediaId });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"genwave-t414-r2-reinstall"</c>
/// compose project-name prefix this file's own arc needs (distinct from every other jingle/voice-pack
/// arc's own prefix in this test project — every ephemeral Postgres instance here is fully isolated by
/// construction).</summary>
sealed class JinglePackReinstallOrphansDatabase : EphemeralStationDatabase
{
    JinglePackReinstallOrphansDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<JinglePackReinstallOrphansDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t414-r2-reinstall");
        var db = new JinglePackReinstallOrphansDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="JinglePackReinstallOrphansArc"/> —
/// boots the real Program.cs graph against a REAL ephemeral Postgres (<paramref name="database"/>)
/// with every hosted service removed, the REAL <c>IJinglePackStore</c>/analyzer pipeline (never
/// swapped — this file's whole point is proving the real orphan-diff and cross-schema guard), and a
/// fake catalog origin serving exactly the <paramref name="assets"/> this instance was built with.
/// Mirrors <c>Story399_JinglePackInstall.cs</c>'s own <c>JinglePackInstallWebFactory</c>, parameterized
/// over an arbitrary asset list per instance rather than one fixed pack, since this file's own two arcs
/// each need TWO distinctly-shaped installs of the SAME slug through TWO distinct, cold-cache
/// instances.
/// </summary>
file sealed class JinglePackReinstallOrphansWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-t414-r2-reinstall-orphans";

    readonly JinglePackReinstallOrphansDatabase database;
    readonly string jingleRoot;
    readonly FakeHttpMessageHandler handler;

    public JinglePackReinstallOrphansWebFactory(
        JinglePackReinstallOrphansDatabase database, string jingleRoot, string slug,
        (string Title, string Role, string File, byte[] Bytes)[] assets)
    {
        this.database = database;
        this.jingleRoot = jingleRoot;
        handler = JinglePackReinstallOrphansFixtures.BuildRoutedHandler(slug, assets);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", database.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", database.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        builder.UseSetting("Station:Scope:LibraryIds:1", "2");
        builder.UseSetting("Community:CatalogIndexUrl", JinglePackReinstallOrphansFixtures.IndexUrl);
        builder.UseSetting("Packs:JingleRoot", jingleRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
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

/// <summary>Builds a one-slug fake catalog origin's index/manifest/meta/asset bodies for an arbitrary
/// asset list — mirrors <c>Story399_JinglePackInstall.cs</c>'s own <c>JinglePackInstallFixtures</c>
/// idiom, parameterized per call (rather than one fixed pack) since this file's own two arcs each
/// install the SAME slug twice with genuinely different manifests.</summary>
file static class JinglePackReinstallOrphansFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/jingle-reinstall-orphans-index.json";
    const string Origin = "https://catalog.test/repo/";
    const string PackName = "Reinstall Orphans Test Pack";

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string ManifestJson(string slug, (string Title, string Role, string File, byte[] Bytes)[] assets) => $$"""
        { "packName": "{{PackName}}",
          "assets": [
            {{string.Join(", ", assets.Select(a =>
                $$"""{ "file": "{{a.File}}", "sha256": "{{Sha256Hex(a.Bytes)}}", "role": "{{a.Role}}", "title": "{{a.Title}}", "license": "CC0" }"""))}}
          ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A jingle pack for the reinstall-orphans specs.","audience":"everyone"}
        """;

    static string EntryJson(string slug, string manifestJson, (string Title, string Role, string File, byte[] Bytes)[] assets) => $$"""
        { "slug": "{{slug}}", "kind": "jingle-pack", "audience": "everyone",
          "manifest": { "path": "entries/{{slug}}/{{slug}}.jingle-pack.json", "sha256": "{{Sha256Hex(manifestJson)}}" },
          "meta": { "path": "entries/{{slug}}/{{slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
          "assets": [
            {{string.Join(", ", assets.Select(a =>
                $$"""{ "path": "entries/{{slug}}/{{a.File}}", "sha256": "{{Sha256Hex(a.Bytes)}}", "bytes": {{a.Bytes.Length}} }"""))}}
          ] }
        """;

    static string IndexJson(string entryJson) => $$"""
        { "generatedAt": "2026-09-07", "entries": [ {{entryJson}} ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler(string slug, (string Title, string Role, string File, byte[] Bytes)[] assets)
    {
        var manifestJson = ManifestJson(slug, assets);
        var entryJson = EntryJson(slug, manifestJson, assets);
        var indexJson = IndexJson(entryJson);

        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = indexJson,
            [Origin + $"entries/{slug}/{slug}.jingle-pack.json"] = manifestJson,
            [Origin + $"entries/{slug}/{slug}.meta.json"] = MetaJson,
        };
        var assetBytesByUrl = assets.ToDictionary(a => Origin + $"entries/{slug}/{a.File}", a => a.Bytes, StringComparer.Ordinal);

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
