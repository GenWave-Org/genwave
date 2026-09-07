// STORY-399 — Jingle-pack install puts beds in the library ready to duck under an ad (SPEC F165.1–.3/.5 · PLAN T414)
//
// Drives the REAL production install route through WebApplicationFactory<Program> against a REAL
// ephemeral Postgres (the VoicePackUninstallArc idiom — Story401_PackUninstallGuards.cs — one pack
// kind over: "arrange once, many read-only Scenarios") and a fake catalog origin serving one
// three-asset jingle pack (bed/sting/station_id), each asset a real, small ffmpeg-generated WAV
// (JingleTestAudio) so the REAL ILoudnessAnalyzer/ICueAnalyzer/TagLib read genuinely measurable
// audio — never faked bytes standing in for what enrichment is actually supposed to prove.
// ScenarioBedPoolQueryFindsTheBeds stays its own pending stub — its own bed-pool query is PLAN
// T416's own surface, not this task's.

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
using GenWave.Host.Catalog;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureJinglePackInstallPutsBedsInTheLibrary
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(JinglePackInstallCollection.Name)]
    public sealed class ScenarioInstallWritesFilesToAuthored(JinglePackInstallArc arc)
    {
        [Fact]
        public void EveryManifestAssetLandsAtTheExpectedAuthoredPath() => Assert.True(arc.InstalledAssetPathsExist);

        [Fact]
        public void EveryFilesByteLengthMatchesTheManifestsPin() => Assert.True(arc.InstalledAssetByteLengthsMatch);

        // T414 review round 3 finding 4 (F6.8) — a clean install's own `.staging-{guid}` scratch
        // directory is removed on success; today this was covered only incidentally (as a side effect
        // of ScenarioACancelledUpsertDuringAReinstallLeavesThePreviousInstallUntouched's own FIRST
        // install, in Story399_JinglePackEnrichmentAndHashFailures.cs), never by the success arc itself.
        [Fact]
        public void NoStagingSiblingSurvives() => Assert.True(arc.NoStagingSiblingSurvives);
    }

    [Collection(JinglePackInstallCollection.Name)]
    public sealed class ScenarioInstallInsertsOneMediaRowPerAsset(JinglePackInstallArc arc)
    {
        [Fact]
        public void ThreeAssetsBecomeThreeMediaRows() => Assert.Equal(3, arc.InstalledRows.Count);

        [Fact]
        public void EveryRowCarriesImagingKindJingle() =>
            Assert.All(arc.InstalledRows, row => Assert.Equal("jingle", row.ImagingKind));

        [Fact]
        public void EveryRowCarriesTheSourcePackSlug() =>
            Assert.All(arc.InstalledRows, row => Assert.Equal(JinglePackInstallFixtures.Slug, row.PackSlug));

        [Fact]
        public void EveryRowsJingleRoleMatchesTheManifestForThatAsset()
        {
            foreach (var row in arc.InstalledRows)
                Assert.Equal(JinglePackInstallFixtures.RoleForTitle(row.Title), row.JingleRole);
        }

        // F6.2 — SPEC F165.2: "artist = the pack packName" on every row.
        [Fact]
        public void EveryRowsArtistIsThePackName() =>
            Assert.All(arc.InstalledRows, row => Assert.Equal(JinglePackInstallFixtures.PackName, row.Artist));

        // F6.3 — SPEC F165.2: "library_id = the ads library" on every row.
        [Fact]
        public void EveryRowsLibraryIdIsTheAdsLibrarysId() =>
            Assert.All(arc.InstalledRows, row => Assert.Equal(arc.AdsLibraryId, row.LibraryId));
    }

    [Collection(JinglePackInstallCollection.Name)]
    public sealed class ScenarioEnrichmentRan(JinglePackInstallArc arc)
    {
        [Fact]
        public void EveryInstalledRowHasANonNullLoudnessMeasurement() =>
            Assert.All(arc.InstalledRows, row => Assert.True(row.Measurable));

        [Fact]
        public void EveryInstalledRowHasNonNullCuePoints() =>
            Assert.All(arc.InstalledRows, row => Assert.True(row.CueInSec is not null && row.CueOutSec is not null));
    }

    public sealed class ScenarioBedPoolQueryFindsTheBeds
    {
        [Fact]
        public void TheBedPoolQueryReturnsExactlyTheBedRoleRows()
            => Assert.Fail("pending: T416 ad-worker bed-pool query — AC4 (this task, T414, only lands the rows a pool query will read)");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Collection(JinglePackInstallCollection.Name)]
    public sealed class ScenarioTheRotationFenceExcludesJinglesFromMusic(JinglePackInstallArc arc)
    {
        [Fact]
        public void ThePlayablePredicateReturnsNoJingleRowsForMusicSelection() =>
            Assert.Equal(HttpStatusCode.NotFound, arc.RandomAfterInstallStatus);
    }

    public sealed class ScenarioAnInvalidRoleRefuses
    {
        [Fact]
        public void TheAppsOwnClosedSetGateRejectsAManifestWithAnUnknownRole()
        {
            // The genwave-catalog repo's own CI schema check (schemas/jingle-pack-manifest.schema.json,
            // T411) is a DIFFERENT repo's own gate — not exercisable from this xunit fact. This proves
            // this app's own SECOND, independent gate instead
            // (CatalogJinglePackManifestSerializer's own remarks: "defense in depth against a
            // compromised or stale catalog origin") refuses the same shape just as absolutely (AC6): a
            // role outside bed/sting/station_id degrades the WHOLE manifest to null, never a
            // partially-admitted pack.
            const string json = """
                { "packName": "Bad Role Pack",
                  "assets": [ { "file": "x.wav",
                                 "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                                 "role": "not-a-real-role", "title": "X", "license": "CC0" } ] }
                """;
            Assert.Null(CatalogJinglePackManifestSerializer.Deserialize(json));
        }
    }
}

// ── The DB-backed arc — one real Postgres, one running app, one installed pack, three real assets ──

[CollectionDefinition(Name)]
public sealed class JinglePackInstallCollection : ICollectionFixture<JinglePackInstallArc>
{
    public const string Name = "Story399JinglePackInstall";
}

/// <summary>
/// Arranges every DB-backed fact STORY-399's Scenarios read (the <c>VoicePackUninstallArc</c> idiom —
/// Story401_PackUninstallGuards.cs — one pack kind over: "arrange once, many read-only Scenarios"):
/// boots ONE real ephemeral Postgres and ONE real, hosted-service-free app instance, seeds the
/// <c>ads</c> library <see cref="AdsOptions.LibraryName"/> resolves at install time, installs one
/// fixture pack (three real, ffmpeg-generated assets — a <c>bed</c>, a <c>sting</c>, a
/// <c>station_id</c>) through the REAL <c>POST /api/jingle-packs/{slug}/install</c> route, then reads
/// back every fact the Scenarios above assert on: file existence/size, the real
/// <c>library.media</c> rows the install wrote, and a real <c>GET /media/random</c> call proving the
/// rotation fence (SPEC F158.4) already excludes them. File-existence/byte-length checks are computed
/// HERE, before <see cref="PacksOptions.JingleRoot"/>'s own temp directory is cleaned up in this
/// method's own <c>finally</c> (mirrors <c>VoicePackUninstallArc</c>'s own "compute the bool before
/// cleanup, never hand out a path that is about to be deleted" discipline).
/// </summary>
public sealed class JinglePackInstallArc : IAsyncLifetime
{
    public bool InstalledAssetPathsExist { get; private set; }
    public bool InstalledAssetByteLengthsMatch { get; private set; }
    public bool NoStagingSiblingSurvives { get; private set; }
    public IReadOnlyList<InstalledJingleRow> InstalledRows { get; private set; } = [];
    public HttpStatusCode RandomAfterInstallStatus { get; private set; }
    public long AdsLibraryId { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await JinglePackInstallDatabase.StartAsync();
        var jingleRoot = Directory.CreateTempSubdirectory("t414-story399-jingle-").FullName;
        try
        {
            AdsLibraryId = await SeedAdsLibraryAsync(database.LibraryConnectionString);

            await using var factory = new JinglePackInstallWebFactory(database, jingleRoot);
            var client = await JinglePackInstallWebFactory.LoggedInClientAsync(factory);

            var install = await client.PostAsync($"/api/jingle-packs/{JinglePackInstallFixtures.Slug}/install", null);
            if (!install.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"fixture install of '{JinglePackInstallFixtures.Slug}' failed: {await install.Content.ReadAsStringAsync()}");

            InstalledAssetPathsExist = JinglePackInstallFixtures.AssetFiles.All(
                file => File.Exists(Path.Combine(jingleRoot, JinglePackInstallFixtures.Slug, file)));

            InstalledAssetByteLengthsMatch = JinglePackInstallFixtures.AssetFiles.All(file =>
            {
                var onDiskPath = Path.Combine(jingleRoot, JinglePackInstallFixtures.Slug, file);
                return File.Exists(onDiskPath)
                    && File.ReadAllBytes(onDiskPath).Length == JinglePackInstallFixtures.AssetBytes(file).Length;
            });

            NoStagingSiblingSurvives = !Directory.EnumerateDirectories(jingleRoot, "*.staging-*").Any();

            InstalledRows = await ReadInstalledRowsAsync(database.LibraryConnectionString);

            var random = await client.GetAsync("/media/random");
            RandomAfterInstallStatus = random.StatusCode;
        }
        finally
        {
            try { Directory.Delete(jingleRoot, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    static async Task<long> SeedAdsLibraryAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        // db/01's own seed guarantees 'default' is id=1; 'ads' is the only other row this arc ever
        // inserts, into a fresh ephemeral database — deterministically id=2, matching this file's own
        // JinglePackInstallWebFactory's Station:Scope:LibraryIds set below. Read back rather than
        // assumed, so F6.3's own fact pins the real value the install route actually resolved.
        return await conn.ExecuteScalarAsync<long>("insert into library.library (name) values ('ads') returning id");
    }

    static async Task<IReadOnlyList<InstalledJingleRow>> ReadInstalledRowsAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync(
            """
            select id, title, artist, library_id, imaging_kind, pack_slug, jingle_role, measurable, cue_in_sec, cue_out_sec
            from library.media
            where pack_slug = @slug
            order by title
            """,
            new { slug = JinglePackInstallFixtures.Slug });

        return rows.Select(r => new InstalledJingleRow(
            (long)r.id, (string)r.title, (string)r.artist, (long)r.library_id, (string)r.imaging_kind,
            (string?)r.pack_slug, (string?)r.jingle_role,
            (bool)r.measurable, (double?)r.cue_in_sec, (double?)r.cue_out_sec)).ToList();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>One <c>library.media</c> row this arc's own install wrote, read back with raw SQL — the
/// Scenarios above assert on this projection rather than the HTTP response body, so a fact fails on
/// what the database actually holds, not just on what the controller claims it wrote.</summary>
public sealed record InstalledJingleRow(
    long Id, string Title, string Artist, long LibraryId, string ImagingKind, string? PackSlug, string? JingleRole,
    bool Measurable, double? CueInSec, double? CueOutSec);

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"genwave-t414-399"</c> compose
/// project-name prefix this file's own arc needs.</summary>
file sealed class JinglePackInstallDatabase : EphemeralStationDatabase
{
    JinglePackInstallDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<JinglePackInstallDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t414-399");
        var db = new JinglePackInstallDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="JinglePackInstallArc"/> — boots the
/// real Program.cs graph against a REAL ephemeral Postgres (<paramref name="database"/>) with every
/// hosted service removed (no background reach into <c>library.media</c> racing this arc's own
/// install), the REAL <c>IJinglePackStore</c>/<c>ILoudnessAnalyzer</c>/<c>ICueAnalyzer</c>/
/// <c>IEnergyAnalyzer</c> (never swapped — this file's whole point is proving the REAL enrichment
/// pipeline actually runs), and <c>Community:CatalogIndexUrl</c> pointed at a fake catalog origin
/// serving one three-asset fixture pack. <c>Station:Scope:LibraryIds</c> carries BOTH the default
/// library (id 1, always seeded — db/01) and the <c>ads</c> library this arc itself inserts
/// (deterministically id 2 in a fresh ephemeral database) — the rotation-fence Scenario needs the
/// WHOLE scope to hold nothing but jingle rows, and the default library is never given any row here.
/// </summary>
file sealed class JinglePackInstallWebFactory(JinglePackInstallDatabase database, string jingleRoot)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story399-jingle-install";

    readonly FakeHttpMessageHandler handler = JinglePackInstallFixtures.BuildRoutedHandler();

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
        builder.UseSetting("Community:CatalogIndexUrl", JinglePackInstallFixtures.IndexUrl);
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

/// <summary>
/// A one-entry fake catalog origin serving one jingle pack with three real, ffmpeg-generated WAV
/// assets — one of each closed-set role (<c>bed</c>, <c>sting</c>, <c>station_id</c>). Every asset's
/// bytes are generated ONCE, into a scratch directory this class alone owns and deletes again
/// immediately (never <see cref="JinglePackInstallArc"/>'s own <c>jingleRoot</c> — that is the
/// CONTROLLER's own install target, a distinct path) — both this fixture's own manifest/index hashes
/// AND the fake HTTP handler's asset bodies read the SAME in-memory bytes, so there is only one place
/// a fixture asset's content is ever defined.
/// </summary>
file static class JinglePackInstallFixtures
{
    public const string Slug = "story399-jingle-pack";
    public const string IndexUrl = "https://catalog.test/repo/jingle-install-index.json";
    const string Origin = "https://catalog.test/repo/";
    public const string PackName = "Story399 Test Pack";

    static readonly (string File, string Title, string Role)[] AssetDefs =
    [
        ("morning-drive.wav", "Morning Drive", "bed"),
        ("quick-stab.wav", "Quick Stab", "sting"),
        ("station-id.wav", "Station ID", "station_id"),
    ];

    public static readonly IReadOnlyList<string> AssetFiles = AssetDefs.Select(a => a.File).ToList();

    public static string RoleForTitle(string title) =>
        AssetDefs.Single(a => string.Equals(a.Title, title, StringComparison.Ordinal)).Role;

    static readonly IReadOnlyDictionary<string, byte[]> assetBytesByFile = GenerateAssetBytes();

    static IReadOnlyDictionary<string, byte[]> GenerateAssetBytes()
    {
        var dir = JingleTestAudio.NewTempDir();
        try
        {
            return AssetDefs.ToDictionary(
                a => a.File,
                a => File.ReadAllBytes(JingleTestAudio.CreateBedShapedTone(dir, a.File)),
                StringComparer.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    public static byte[] AssetBytes(string file) => assetBytesByFile[file];

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string ManifestJson() => $$"""
        { "packName": "{{PackName}}",
          "assets": [
            {{string.Join(", ", AssetDefs.Select(a => $$"""
                { "file": "{{a.File}}", "sha256": "{{Sha256Hex(AssetBytes(a.File))}}", "role": "{{a.Role}}", "title": "{{a.Title}}", "license": "CC0" }
                """))}}
          ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A jingle pack for the install specs.","audience":"everyone"}
        """;

    static string EntryJson() => $$"""
        { "slug": "{{Slug}}", "kind": "jingle-pack", "audience": "everyone",
          "manifest": { "path": "entries/{{Slug}}/{{Slug}}.jingle-pack.json", "sha256": "{{Sha256Hex(ManifestJson())}}" },
          "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
          "assets": [
            {{string.Join(", ", AssetDefs.Select(a =>
                $$"""{ "path": "entries/{{Slug}}/{{a.File}}", "sha256": "{{Sha256Hex(AssetBytes(a.File))}}", "bytes": {{AssetBytes(a.File).Length}} }"""))}}
          ] }
        """;

    static string IndexJson() => $$"""
        { "generatedAt": "2026-09-06", "entries": [ {{EntryJson()}} ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = IndexJson(),
            [Origin + $"entries/{Slug}/{Slug}.jingle-pack.json"] = ManifestJson(),
            [Origin + $"entries/{Slug}/{Slug}.meta.json"] = MetaJson,
        };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in AssetFiles)
            assetBytesByUrl[Origin + $"entries/{Slug}/{file}"] = AssetBytes(file);

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

// ── T414 review round 2 finding F2 — text pin for UnwindWritten's own restore-before-delete order ──

/// <summary>
/// Pins <c>JinglePackController.UnwindWritten</c>'s own internal ordering directly from its source
/// text — the <c>Story401_JinglePackDeleteGuardSql.cs</c>'s own <c>DeleteAsyncBodyText()</c> idiom
/// (Story129_SafePathLevelMatching.cs's RepoRoot idiom, applied to C# source), mirrored here because
/// the source being pinned is this project's own controller, not a MediaLibrary repository. A
/// displaced file must be restored BEFORE an undisplaced one is deleted (see that method's own
/// remarks: an atomic rename-over needs the target name to still exist, so restoring it first means
/// the target name is never briefly absent on the unwind path either) — flipping the order reds
/// nothing behaviorally observable through Story401_PackUninstallGuards.cs's own real-Postgres arc
/// when nothing was ever displaced to begin with, so this is the only fact that pins the ordering
/// itself.
/// </summary>
public static class FeatureJinglePackControllerUnwindOrdering
{
    public sealed class ScenarioUnwindRestoresBeforeItDeletes
    {
        [Fact]
        public void UnwindWrittenRestoresDisplacedFilesBeforeDeletingUndisplacedOnes()
        {
            var body = UnwindWrittenBodyText();

            // T414 review round 3 finding 2 — the CALL sites, inside the isolated method body, not
            // merely "referenced somewhere in the rest of the file" (a plain sourceText[start..] slice
            // to EOF let the METHOD DECLARATIONS `void TryDelete(string path)`/`void TryRestore(...)`
            // further down the file vacuously satisfy an ">= 0" guard even with the delete CALL
            // deleted from UnwindWritten's own body entirely — mutation c2).
            Assert.Contains("TryDelete(", body, StringComparison.Ordinal);
            Assert.Contains("TryRestore(", body, StringComparison.Ordinal);

            var restoreIndex = body.IndexOf("TryRestore(", StringComparison.Ordinal);
            var deleteIndex = body.IndexOf("TryDelete(", StringComparison.Ordinal);

            Assert.True(restoreIndex >= 0, "TryRestore( not called in UnwindWritten's own body");
            Assert.True(deleteIndex >= 0, "TryDelete( not called in UnwindWritten's own body");
            Assert.True(restoreIndex < deleteIndex, "restore-before-delete: TryRestore must appear before TryDelete in UnwindWritten's own source");
        }
    }

    static string UnwindWrittenBodyText()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sourceText = File.ReadAllText(Path.Combine(
            repoRoot, "src", "GenWave.Host", "Api", "JinglePackController.cs"));

        const string signature = "void UnwindWritten(IReadOnlyList<WrittenJingleFile> written)";
        var signatureStart = sourceText.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureStart >= 0, "UnwindWritten method signature not found in JinglePackController.cs");

        var bodyOpenBrace = sourceText.IndexOf('{', signatureStart + signature.Length);
        Assert.True(bodyOpenBrace >= 0, "UnwindWritten's own opening brace not found in JinglePackController.cs");

        // Brace-match from the signature's own first '{' to its OWN matching '}' (T414 review round 3
        // finding 2) — a plain sourceText[start..] slice ran to EOF, so it swept up every method
        // DECLARED after UnwindWritten too (including TryDelete's/TryRestore's own signatures), which
        // vacuously satisfied the ">= 0" guards below without pinning a single CALL inside
        // UnwindWritten's own body.
        var depth = 0;
        var bodyCloseBrace = -1;
        for (var i = bodyOpenBrace; i < sourceText.Length; i++)
        {
            if (sourceText[i] == '{')
                depth++;
            else if (sourceText[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    bodyCloseBrace = i;
                    break;
                }
            }
        }
        Assert.True(bodyCloseBrace > bodyOpenBrace, "UnwindWritten's own closing brace not found in JinglePackController.cs");

        return sourceText[bodyOpenBrace..(bodyCloseBrace + 1)];
    }
}

// ── T414 smoke round 4 finding — a null ICueAnalyzer result is a CONTRACT ("no silence found"), never
// a refusal (SPEC F165.3, JingleCuePolicy.Apply) ────────────────────────────────────────────────────

/// <summary>
/// A tight-cut sting, or a bed/station-id that starts and ends hot, has NO silence for
/// <c>ICueAnalyzer</c> to find at all — <c>FfmpegCueAnalyzer</c> returns null "full-file playback is
/// intended" by CONTRACT there, not a decode failure. This installs a pack whose three assets are
/// pure-tone WAVs (<see cref="JingleTestAudio.CreateTone"/> — no silence padding, unlike
/// <c>JinglePackInstallFixtures</c>'s own <see cref="JingleTestAudio.CreateBedShapedTone"/> above,
/// which pins the OTHER path: a <c>station_id</c> WITH an analyzed, silence-trimmed cue) and asserts
/// the whole pack lands — every role, including <c>station_id</c>, storing its own full 0..duration
/// span rather than the install refusing the pack as unreadable. Mirrors
/// <see cref="JinglePackInstallArc"/>'s own shape one asset shape over: a second real ephemeral
/// Postgres + hosted app instance, not a second collection sharing the first (an xUnit collection
/// fixture is one instance per collection; two arcs cannot share one).
/// </summary>
public static class FeatureJinglePackInstallAcceptsPureToneAssetsWithNoSilenceToTrim
{
    [Collection(JinglePackToneInstallCollection.Name)]
    public sealed class ScenarioAllThreeRolesInstallCleanlyWithNoSilenceDetected(JinglePackToneInstallArc arc)
    {
        [Fact]
        public void TheInstallReturns200() => Assert.True(
            arc.InstallStatusCode == HttpStatusCode.OK, $"expected 200, got {arc.InstallStatusCode}: {arc.InstallBody}");

        [Fact]
        public void ThreeAssetsBecomeThreeMediaRows() => Assert.Equal(3, arc.InstalledRows.Count);

        [Fact]
        public void EveryRolesCueInIsExactlyZero() =>
            Assert.All(arc.InstalledRows, row => Assert.Equal(0, row.CueInSec));

        [Fact]
        public void EveryRolesCueOutSpansItsOwnFullDurationIncludingStationId()
        {
            foreach (var row in arc.InstalledRows)
            {
                Assert.NotNull(row.CueOutSec);
                Assert.Equal(row.DurationMs / 1000.0, row.CueOutSec.Value, precision: 2);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class JinglePackToneInstallCollection : ICollectionFixture<JinglePackToneInstallArc>
{
    public const string Name = "Story399JinglePackToneInstall";
}

/// <summary>One <c>library.media</c> row <see cref="JinglePackToneInstallArc"/>'s own install wrote,
/// including the read-back <c>duration_ms</c> a pure-tone asset's own cue_out_sec is compared
/// against — <see cref="InstalledJingleRow"/> above has no duration field, and adding one there would
/// widen every OTHER Scenario's own projection for a fact only this file's own tone-only arc needs.</summary>
public sealed record ToneInstalledJingleRow(string JingleRole, double? CueInSec, double? CueOutSec, int DurationMs);

/// <summary>
/// The <see cref="JinglePackInstallArc"/> idiom, one asset shape over: boots its OWN real ephemeral
/// Postgres and hosted-service-free app instance (never shares <see cref="JinglePackInstallArc"/>'s
/// own — an xUnit collection fixture is one instance per collection), installs
/// <see cref="JinglePackToneInstallFixtures"/>'s own pure-tone pack, and reads back every fact
/// <see cref="FeatureJinglePackInstallAcceptsPureToneAssetsWithNoSilenceToTrim"/>'s Scenario asserts
/// on. Unlike <see cref="JinglePackInstallArc"/>, a failed install is captured (status + body), never
/// thrown — this arc's whole point is proving the happy path a REGRESSION of the T414 smoke-round-4
/// fix would turn into a 422, so the Scenario needs a clean status-code assertion to red on, not an
/// arrange-time exception.
/// </summary>
public sealed class JinglePackToneInstallArc : IAsyncLifetime
{
    public HttpStatusCode InstallStatusCode { get; private set; }
    public string InstallBody { get; private set; } = string.Empty;
    public IReadOnlyList<ToneInstalledJingleRow> InstalledRows { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var database = await JinglePackToneInstallDatabase.StartAsync();
        var jingleRoot = Directory.CreateTempSubdirectory("t414-story399-jingle-tone-").FullName;
        try
        {
            await SeedAdsLibraryAsync(database.LibraryConnectionString);

            await using var factory = new JinglePackToneInstallWebFactory(database, jingleRoot);
            var client = await JinglePackToneInstallWebFactory.LoggedInClientAsync(factory);

            var install = await client.PostAsync($"/api/jingle-packs/{JinglePackToneInstallFixtures.Slug}/install", null);
            InstallStatusCode = install.StatusCode;
            InstallBody = await install.Content.ReadAsStringAsync();

            if (InstallStatusCode == HttpStatusCode.OK)
                InstalledRows = await ReadInstalledRowsAsync(database.LibraryConnectionString);
        }
        finally
        {
            try { Directory.Delete(jingleRoot, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    static async Task SeedAdsLibraryAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("insert into library.library (name) values ('ads')");
    }

    static async Task<IReadOnlyList<ToneInstalledJingleRow>> ReadInstalledRowsAsync(string libraryConnectionString)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync(
            """
            select jingle_role, cue_in_sec, cue_out_sec, duration_ms
            from library.media
            where pack_slug = @slug
            order by jingle_role
            """,
            new { slug = JinglePackToneInstallFixtures.Slug });

        return rows.Select(r => new ToneInstalledJingleRow(
            (string)r.jingle_role, (double?)r.cue_in_sec, (double?)r.cue_out_sec, (int)r.duration_ms)).ToList();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"genwave-t414-tone"</c> compose
/// project-name prefix this file's own tone-only arc needs, distinct from
/// <see cref="JinglePackInstallDatabase"/>'s own prefix so the two arcs' ephemeral Postgres instances
/// never collide.</summary>
file sealed class JinglePackToneInstallDatabase : EphemeralStationDatabase
{
    JinglePackToneInstallDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<JinglePackToneInstallDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t414-tone");
        var db = new JinglePackToneInstallDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary><see cref="JinglePackInstallWebFactory"/>'s own shape, one fixture set over — boots the
/// real Program.cs graph against <paramref name="database"/> with every hosted service removed, the
/// REAL enrichment pipeline (never swapped), and <c>Community:CatalogIndexUrl</c> pointed at
/// <see cref="JinglePackToneInstallFixtures"/>'s own fake catalog origin.</summary>
file sealed class JinglePackToneInstallWebFactory(JinglePackToneInstallDatabase database, string jingleRoot)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story399-jingle-tone-install";

    readonly FakeHttpMessageHandler handler = JinglePackToneInstallFixtures.BuildRoutedHandler();

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
        builder.UseSetting("Community:CatalogIndexUrl", JinglePackToneInstallFixtures.IndexUrl);
        builder.UseSetting("Packs:JingleRoot", jingleRoot);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(JinglePackToneInstallWebFactory factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary><see cref="JinglePackInstallFixtures"/>'s own shape, one asset shape over — three real,
/// ffmpeg-generated WAV assets (one of each closed-set role) with NO silence padding at all
/// (<see cref="JingleTestAudio.CreateTone"/>, not <see cref="JingleTestAudio.CreateBedShapedTone"/>),
/// each its own distinct duration so a wrong row-to-file mapping would show up as a wrong
/// cue_out_sec, not just a coincidentally-matching one.</summary>
file static class JinglePackToneInstallFixtures
{
    public const string Slug = "story399-jingle-pack-tone";
    public const string IndexUrl = "https://catalog.test/repo/jingle-tone-install-index.json";
    const string Origin = "https://catalog.test/repo/";
    public const string PackName = "Story399 Tone Test Pack";

    static readonly (string File, string Title, string Role, double Seconds)[] AssetDefs =
    [
        ("morning-drive-tone.wav", "Morning Drive Tone", "bed", 4.0),
        ("quick-stab-tone.wav", "Quick Stab Tone", "sting", 1.2),
        ("station-id-tone.wav", "Station ID Tone", "station_id", 2.5),
    ];

    public static readonly IReadOnlyList<string> AssetFiles = AssetDefs.Select(a => a.File).ToList();

    static readonly IReadOnlyDictionary<string, byte[]> assetBytesByFile = GenerateAssetBytes();

    static IReadOnlyDictionary<string, byte[]> GenerateAssetBytes()
    {
        var dir = JingleTestAudio.NewTempDir();
        try
        {
            return AssetDefs.ToDictionary(
                a => a.File,
                a => File.ReadAllBytes(JingleTestAudio.CreateTone(dir, a.File, a.Seconds)),
                StringComparer.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    static byte[] AssetBytes(string file) => assetBytesByFile[file];

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string ManifestJson() => $$"""
        { "packName": "{{PackName}}",
          "assets": [
            {{string.Join(", ", AssetDefs.Select(a => $$"""
                { "file": "{{a.File}}", "sha256": "{{Sha256Hex(AssetBytes(a.File))}}", "role": "{{a.Role}}", "title": "{{a.Title}}", "license": "CC0" }
                """))}}
          ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A silence-free jingle pack for the install specs.","audience":"everyone"}
        """;

    static string EntryJson() => $$"""
        { "slug": "{{Slug}}", "kind": "jingle-pack", "audience": "everyone",
          "manifest": { "path": "entries/{{Slug}}/{{Slug}}.jingle-pack.json", "sha256": "{{Sha256Hex(ManifestJson())}}" },
          "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
          "assets": [
            {{string.Join(", ", AssetDefs.Select(a =>
                $$"""{ "path": "entries/{{Slug}}/{{a.File}}", "sha256": "{{Sha256Hex(AssetBytes(a.File))}}", "bytes": {{AssetBytes(a.File).Length}} }"""))}}
          ] }
        """;

    static string IndexJson() => $$"""
        { "generatedAt": "2026-09-06", "entries": [ {{EntryJson()}} ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IndexUrl] = IndexJson(),
            [Origin + $"entries/{Slug}/{Slug}.jingle-pack.json"] = ManifestJson(),
            [Origin + $"entries/{Slug}/{Slug}.meta.json"] = MetaJson,
        };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in AssetFiles)
            assetBytesByUrl[Origin + $"entries/{Slug}/{file}"] = AssetBytes(file);

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
