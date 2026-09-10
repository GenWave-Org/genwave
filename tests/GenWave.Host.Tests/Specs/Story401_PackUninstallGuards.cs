// STORY-401 — Uninstalling a pack refuses when something references it (SPEC F164.6, F165.6 · PLAN T413/T414)
//
// The voice-pack facts (PLAN T413) drive the REAL production DELETE route through
// WebApplicationFactory<Program> against a REAL ephemeral Postgres (the Story393_AdPackKind.cs
// AdPackKindArc idiom, one pack-kind over: "arrange once, many read-only Scenarios") — station.ad_spot
// and station.persona rows are inserted with raw SQL to simulate a reference existing at delete time,
// exactly the situation VoicePackRepository.DeleteAsync's own single-statement guard has to catch. The
// jingle-pack facts (PLAN T414) drive the same real DELETE route through their own arc
// (JinglePackUninstallArc, below) — a station.ad_spot.bed_media_id row is inserted with raw SQL to
// simulate a live reference the same way, exactly the situation JinglePackRepository.DeleteAsync's own
// guard-read-then-delete (a two-connection sequence, not one in-statement guard — see that type's own
// remarks) has to catch.

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

public static class FeaturePackUninstallRefusesOnActiveReferences
{
    // ---------------------------------------------------------------------
    // SAD PATH — refusals first (the whole guard is a refusal contract)
    // ---------------------------------------------------------------------

    [Collection(VoicePackUninstallCollection.Name)]
    public sealed class ScenarioVoicePackRefusesOnActiveVoicePlan(VoicePackUninstallArc arc)
    {
        [Fact]
        public void UninstallReturns409WhenAVoiceIdSitsInAnApprovedSpotsVoicePlan()
            => Assert.Equal(HttpStatusCode.Conflict, arc.SpotGuardDeleteStatus);

        [Fact]
        public void The409ProblemDetailsListsTheReferencingSpotIds()
            => Assert.Contains(arc.SpotGuardAdSpotId.ToString(), arc.SpotGuardDeleteBody, StringComparison.Ordinal);

        [Fact]
        public void ThePackRowIsUntouchedOnRefusal()
            => Assert.True(arc.SpotGuardPackRowSurvivesRefusal);
    }

    [Collection(VoicePackUninstallCollection.Name)]
    public sealed class ScenarioVoicePackRefusesOnPersonaReference(VoicePackUninstallArc arc)
    {
        [Fact]
        public void UninstallReturns409WhenAPersonasVoiceReferencesAPackVoice()
            => Assert.Equal(HttpStatusCode.Conflict, arc.PersonaGuardDeleteStatus);

        [Fact]
        public void The409ProblemDetailsListsTheReferencingPersona()
            => Assert.Contains(VoicePackUninstallArc.PersonaGuardPersonaName, arc.PersonaGuardDeleteBody, StringComparison.Ordinal);
    }

    [Collection(JinglePackUninstallCollection.Name)]
    public sealed class ScenarioJinglePackRefusesOnActiveBedMediaId(JinglePackUninstallArc arc)
    {
        [Fact]
        public void UninstallReturns409WhenAPackMediaIdSitsInAReadyAdSpotsBedMediaId()
            => Assert.Equal(HttpStatusCode.Conflict, arc.BedGuardDeleteStatus);

        [Fact]
        public void The409ProblemDetailsListsTheReferencingAdSpotIds()
            => Assert.Contains(arc.BedGuardAdSpotId.ToString(), arc.BedGuardDeleteBody, StringComparison.Ordinal);
    }

    [Collection(VoicePackUninstallCollection.Name)]
    public sealed class ScenarioUnknownSlugRefuses(VoicePackUninstallArc arc)
    {
        [Fact]
        public void UninstallReturns404ForASlugThatWasNeverInstalled()
        {
            // T413 review round 2 finding L2 — VoicePackRepository.DeleteAsync's own NotFound arm
            // (the DELETE's affected-row-count alone cannot distinguish "no such pack" from "exists
            // but the guard refused it", so both are re-queried — see that member's own remarks) has
            // no fact against the REAL repository/SQL; a mutation collapsing NotFound into
            // Deleted([]) would stay green everywhere else in this file, since every OTHER Scenario
            // here targets a slug that genuinely exists.
            Assert.Equal(HttpStatusCode.NotFound, arc.UnknownSlugDeleteStatus);
        }

        [Fact]
        public void The404BodyNamesTheUnknownSlug()
            => Assert.Contains("never-installed-pack", arc.UnknownSlugDeleteBody, StringComparison.Ordinal);
    }

    // F7 (T414 review round 2) — the jingle-pack arm of the same unknown-slug 404, unpinned in round 1.
    [Collection(JinglePackUninstallCollection.Name)]
    public sealed class ScenarioJinglePackUnknownSlugRefuses(JinglePackUninstallArc arc)
    {
        [Fact]
        public void UninstallReturns404ForASlugThatWasNeverInstalled()
            => Assert.Equal(HttpStatusCode.NotFound, arc.UnknownSlugDeleteStatus);

        [Fact]
        public void The404BodyNamesTheUnknownSlugAndCarriesTheNotFoundType()
        {
            Assert.Contains("never-installed", arc.UnknownSlugDeleteBody, StringComparison.Ordinal);
            Assert.Contains("\"jingle_pack_not_found\"", arc.UnknownSlugDeleteBody, StringComparison.Ordinal);
        }
    }

    // F6.7 (T414 review round 2) — neither install nor uninstall is reachable without a session.
    [Collection(JinglePackUninstallCollection.Name)]
    public sealed class ScenarioJinglePackRoutesRefuseAnUnauthenticatedCaller(JinglePackUninstallArc arc)
    {
        [Fact]
        public void InstallReturns401WithoutASession()
            => Assert.Equal(HttpStatusCode.Unauthorized, arc.UnauthenticatedInstallStatus);

        [Fact]
        public void UninstallReturns401WithoutASession()
            => Assert.Equal(HttpStatusCode.Unauthorized, arc.UnauthenticatedDeleteStatus);
    }

    [Collection(VoicePackUninstallCollection.Name)]
    public sealed class ScenarioTheGuardRunsInsideTheDelete(VoicePackUninstallArc arc)
    {
        [Fact]
        public void AReferencePresentBeforeTheDeleteRefuses()
        {
            // Given a caller's own hypothetical "is this referenced?" advisory read found nothing —
            // taken BEFORE the reference below was ever inserted,
            Assert.Equal(0, arc.RaceAdvisoryCheckCountBeforeInsert);

            // Then a real DELETE run AFTER a reference lands still refuses (AC6). This fact alone
            // cannot tell a true in-statement guard apart from an advisory pre-check plus a separate
            // unguarded DELETE — this arc always inserts the reference before calling DELETE either
            // way, so both implementations pass it. The structural claim ("the guard is evaluated as
            // part of the SAME statement that removes the row") is pinned independently, on the SQL
            // text itself, by
            // tests/GenWave.MediaLibrary.Tests/Specs/Story401_VoicePackDeleteGuardSql.cs (T413 review
            // round 1 finding F3).
            Assert.Equal(HttpStatusCode.Conflict, arc.RaceDeleteStatus);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — uninstall succeeds when references are absent/inactive
    // ---------------------------------------------------------------------

    [Collection(VoicePackUninstallCollection.Name)]
    public sealed class ScenarioVoicePackUninstallSucceedsWhenReferencesAreRetiredOrFailed(VoicePackUninstallArc arc)
    {
        [Fact]
        public void UninstallReturns204WhenReferencesAreOnlyInRetiredOrFailedSpots()
            => Assert.Equal(HttpStatusCode.NoContent, arc.CleanDeleteStatus);

        [Fact]
        public void ThePtFilesAreRemovedFromTheVoicesRoot()
            => Assert.False(arc.CleanPtFilesSurviveDelete);

        [Fact]
        public void ThePackAndVoiceRowsAreGone()
            => Assert.True(arc.CleanPackAndVoiceRowsGone);
    }

    [Collection(JinglePackUninstallCollection.Name)]
    public sealed class ScenarioJinglePackUninstallSucceedsOtherwise(JinglePackUninstallArc arc)
    {
        [Fact]
        public void UninstallReturns204WithNoActiveReferences()
            => Assert.Equal(HttpStatusCode.NoContent, arc.CleanDeleteStatus);

        [Fact]
        public void TheAuthoredJinglePacksSlugFolderIsGone()
            => Assert.True(arc.CleanSlugFolderGone);

        [Fact]
        public void EveryAssociatedLibraryMediaRowIsDeleted()
            => Assert.True(arc.CleanMediaRowsGone);
    }
}

// ── The DB-backed arc — one real Postgres, one running app, four installed packs, raw-SQL references ──

[CollectionDefinition(Name)]
public sealed class VoicePackUninstallCollection : ICollectionFixture<VoicePackUninstallArc>
{
    public const string Name = "Story401VoicePackUninstall";
}

/// <summary>
/// Arranges every DB-backed fact STORY-401's voice-pack Scenarios read (the Story393_AdPackKind.cs
/// AdPackKindArc "arrange once, many read-only Scenarios" idiom, one pack-kind over): boots ONE real
/// ephemeral Postgres and ONE real, hosted-service-free app instance, installs four fixture packs
/// through the REAL <c>POST /api/voice-packs/{slug}/install</c> route, then — for each guard fact —
/// inserts the referencing <c>station.ad_spot</c>/<c>station.persona</c> row with raw SQL (simulating
/// "something already points at this voice" without a second HTTP surface this task does not own) and
/// calls the REAL <c>DELETE /api/voice-packs/{slug}</c> route. One running app for the whole arrangement
/// is safe here (unlike <c>AdPackKindArc</c>'s own two-instance dance for a reinstall of the SAME slug):
/// every fixture pack here has its own distinct slug and voice ids, so there is no
/// <c>CatalogProxyService</c> cache staleness between installs to route around.
/// </summary>
public sealed class VoicePackUninstallArc : IAsyncLifetime
{
    public const string PersonaGuardPersonaName = "DJ Guard Test";

    public HttpStatusCode SpotGuardDeleteStatus { get; private set; }
    public string SpotGuardDeleteBody { get; private set; } = "";
    public long SpotGuardAdSpotId { get; private set; }
    public bool SpotGuardPackRowSurvivesRefusal { get; private set; }

    public HttpStatusCode PersonaGuardDeleteStatus { get; private set; }
    public string PersonaGuardDeleteBody { get; private set; } = "";

    public int RaceAdvisoryCheckCountBeforeInsert { get; private set; }
    public HttpStatusCode RaceDeleteStatus { get; private set; }

    public HttpStatusCode CleanDeleteStatus { get; private set; }
    public bool CleanPtFilesSurviveDelete { get; private set; }
    public bool CleanPackAndVoiceRowsGone { get; private set; }

    public HttpStatusCode UnknownSlugDeleteStatus { get; private set; }
    public string UnknownSlugDeleteBody { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await using var database = await VoicePackUninstallDatabase.StartAsync();
        var voicesRoot = Directory.CreateTempSubdirectory("t413-story401-voices-").FullName;
        try
        {
            await using var factory = new VoicePackUninstallWebFactory(database, voicesRoot);
            var client = await VoicePackUninstallWebFactory.LoggedInClientAsync(factory);

            foreach (var slug in VoicePackUninstallFixtures.AllSlugs)
            {
                var install = await client.PostAsync($"/api/voice-packs/{slug}/install", null);
                if (!install.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"fixture install of '{slug}' failed: {await install.Content.ReadAsStringAsync()}");
            }

            // ── AC1 — an approved spot's own voice_plan blocks the guard pack ──
            SpotGuardAdSpotId = await InsertAdSpotAsync(
                database.StationConnectionString, state: "approved", voiceId: "af_spotguard", failReason: null);

            var spotGuardResponse = await client.DeleteAsync("/api/voice-packs/spot-guard-pack");
            SpotGuardDeleteStatus = spotGuardResponse.StatusCode;
            SpotGuardDeleteBody = await spotGuardResponse.Content.ReadAsStringAsync();
            SpotGuardPackRowSurvivesRefusal = await PackRowExistsAsync(database.StationConnectionString, "spot-guard-pack");

            // ── AC2 — a persona's own voice blocks the guard pack ──
            await InsertPersonaAsync(database.StationConnectionString, PersonaGuardPersonaName, "af_personaguard");

            var personaGuardResponse = await client.DeleteAsync("/api/voice-packs/persona-guard-pack");
            PersonaGuardDeleteStatus = personaGuardResponse.StatusCode;
            PersonaGuardDeleteBody = await personaGuardResponse.Content.ReadAsStringAsync();

            // ── AC6 — the guard runs INSIDE the delete, never against an earlier, now-stale check ──
            RaceAdvisoryCheckCountBeforeInsert =
                await CountReferencingAdSpotsAsync(database.StationConnectionString, "af_raceguard");
            await InsertAdSpotAsync(database.StationConnectionString, state: "approved", voiceId: "af_raceguard", failReason: null);

            var raceResponse = await client.DeleteAsync("/api/voice-packs/race-pack");
            RaceDeleteStatus = raceResponse.StatusCode;

            // ── AC3 — retired/failed references never block ──
            await InsertAdSpotAsync(database.StationConnectionString, state: "retired", voiceId: "af_cleanone", failReason: null);
            await InsertAdSpotAsync(database.StationConnectionString, state: "failed", voiceId: "af_cleantwo", failReason: "render error");

            var cleanResponse = await client.DeleteAsync("/api/voice-packs/clean-pack");
            CleanDeleteStatus = cleanResponse.StatusCode;
            CleanPtFilesSurviveDelete =
                File.Exists(Path.Combine(voicesRoot, "af_cleanone.pt")) || File.Exists(Path.Combine(voicesRoot, "af_cleantwo.pt"));
            CleanPackAndVoiceRowsGone = !await PackRowExistsAsync(database.StationConnectionString, "clean-pack");

            // ── L2 — a slug that was never installed refuses 404, never a 204 "deleted" ──
            var unknownSlugResponse = await client.DeleteAsync("/api/voice-packs/never-installed-pack");
            UnknownSlugDeleteStatus = unknownSlugResponse.StatusCode;
            UnknownSlugDeleteBody = await unknownSlugResponse.Content.ReadAsStringAsync();
        }
        finally
        {
            try { Directory.Delete(voicesRoot, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    static async Task<long> InsertAdSpotAsync(string stationConnectionString, string state, string voiceId, string? failReason)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        var voicePlan = $$"""[{"voiceId": "{{voiceId}}"}]""";
        return await conn.ExecuteScalarAsync<long>(
            """
            with sponsor as (
              insert into station.sponsor (name) values ('Test Brand')
              on conflict on constraint sponsor_pack_slug_name_key do update set name = excluded.name
              returning id
            )
            insert into station.ad_spot (sponsor_id, sponsor_name, title, source, state, voice_plan, fail_reason)
            select sponsor.id, 'Test Brand', 'Test Spot', 'owner'::station.ad_source, @State::station.ad_state, @VoicePlan::jsonb, @FailReason
            from sponsor
            returning id
            """,
            new { State = state, VoicePlan = voicePlan, FailReason = failReason });
    }

    static async Task InsertPersonaAsync(string stationConnectionString, string name, string voice)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "insert into station.persona (name, voice) values (@Name, @Voice)",
            new { Name = name, Voice = voice });
    }

    static async Task<int> CountReferencingAdSpotsAsync(string stationConnectionString, string voiceId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            """
            select count(*)::int
            from station.ad_spot s
            join lateral jsonb_array_elements(coalesce(s.voice_plan, '[]'::jsonb)) e on e->>'voiceId' = @VoiceId
            where s.state in ('draft', 'approved', 'rendering', 'ready')
            """,
            new { VoiceId = voiceId });
    }

    static async Task<bool> PackRowExistsAsync(string stationConnectionString, string slug)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from station.voice_pack where slug = @Slug", new { Slug = slug }) > 0;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harnesses ───────────────────────────────────────────────────────────────────────────────

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"genwave-t413-401"</c> compose
/// project-name prefix this file's own arc needs.</summary>
file sealed class VoicePackUninstallDatabase : EphemeralStationDatabase
{
    VoicePackUninstallDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<VoicePackUninstallDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t413-401");
        var db = new VoicePackUninstallDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="VoicePackUninstallArc"/> — boots the
/// real Program.cs graph against a REAL ephemeral Postgres (<paramref name="database"/>) with every
/// hosted service removed (no background reach into <c>station.voice_pack</c>/<c>station.ad_spot</c>
/// racing this arc's own installs/deletes — the <c>AdPackInstallWebFactory</c> idiom one controller
/// over), the REAL <c>IVoicePackStore</c> (never swapped — this file's whole point is proving the
/// REAL <c>VoicePackRepository.DeleteAsync</c> guard SQL, unlike Story396/398's <c>FakeVoicePackStore</c>
/// use), and <c>Community:CatalogIndexUrl</c> pointed at a fake catalog origin serving four
/// non-colliding fixture packs.
/// </summary>
file sealed class VoicePackUninstallWebFactory(VoicePackUninstallDatabase database, string voicesRoot)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story401-voicepack-uninstall";

    readonly FakeHttpMessageHandler handler = VoicePackUninstallFixtures.BuildRoutedHandler(voicesRoot);

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
        builder.UseSetting("Community:CatalogIndexUrl", VoicePackUninstallFixtures.IndexUrl);
        builder.UseSetting("Packs:VoicesRoot", voicesRoot);

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
/// A four-entry fake catalog origin (mirrors <c>VoicePackCollisionFixtures</c>'s own idiom one file
/// over) — every pack's own voice ids are globally distinct across the whole fixture set (this arc
/// installs all four into the SAME database, and <c>voice_pack_voice_voice_id_uk</c> is a real,
/// station-wide unique index): <c>spot-guard-pack</c>/<c>af_spotguard</c> and
/// <c>persona-guard-pack</c>/<c>af_personaguard</c> for the two reference-guard Scenarios,
/// <c>race-pack</c>/<c>af_raceguard</c> for the AC6 guard-runs-inside-the-delete Scenario, and
/// <c>clean-pack</c>/(<c>af_cleanone</c>, <c>af_cleantwo</c>) — two voices — for the AC3 happy path.
/// </summary>
file static class VoicePackUninstallFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/voice-uninstall-index.json";
    const string Origin = "https://catalog.test/repo/";
    // Tts:Endpoint under the "Development" ASP.NET environment this factory uses (see
    // appsettings.Development.json) is "http://localhost:8880", not the production default.
    const string KokoroVoicesUrl = "http://localhost:8880/v1/audio/voices";

    static readonly (string Slug, string[] VoiceIds)[] Packs =
    [
        ("spot-guard-pack", ["af_spotguard"]),
        ("persona-guard-pack", ["af_personaguard"]),
        ("race-pack", ["af_raceguard"]),
        ("clean-pack", ["af_cleanone", "af_cleantwo"]),
    ];

    public static readonly IReadOnlyList<string> AllSlugs = Packs.Select(p => p.Slug).ToList();

    static readonly byte[] PreviewBytes = Encoding.UTF8.GetBytes("fake-preview-bytes-for-story401");

    static byte[] PtBytesFor(string slug, string voiceId) => Encoding.UTF8.GetBytes($"{slug}-{voiceId}-bytes");

    static string ManifestJson(string slug, IReadOnlyList<string> voiceIds) => $$"""
        { "packName": "Test Pack", "engine": "kokoro", "synthetic": true, "sourceRef": null,
          "preview": "{{slug}}.preview.mp3",
          "voices": [ {{string.Join(", ", voiceIds.Select(id => $$"""{ "voiceId": "{{id}}" }"""))}} ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A voice pack for the uninstall-guard specs.","audience":"everyone"}
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string EntryJson(string slug, string[] voiceIds) => $$"""
        { "slug": "{{slug}}", "kind": "voice-pack", "audience": "everyone",
          "manifest": { "path": "entries/{{slug}}/{{slug}}.voice-pack.json", "sha256": "{{Sha256Hex(ManifestJson(slug, voiceIds))}}" },
          "meta": { "path": "entries/{{slug}}/{{slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
          "assets": [
            {{string.Join(", ", voiceIds.Select(id =>
                $$"""{ "path": "entries/{{slug}}/{{id}}.pt", "sha256": "{{Sha256Hex(PtBytesFor(slug, id))}}", "bytes": {{PtBytesFor(slug, id).Length}} }"""))}},
            { "path": "entries/{{slug}}/{{slug}}.preview.mp3", "sha256": "{{Sha256Hex(PreviewBytes)}}", "bytes": {{PreviewBytes.Length}} }
          ] }
        """;

    static string IndexJson() => $$"""
        { "generatedAt": "2026-09-06", "entries": [ {{string.Join(", ", Packs.Select(p => EntryJson(p.Slug, p.VoiceIds)))}} ] }
        """;

    /// <summary>Serves every pack entry's own documents/assets, plus a Kokoro <c>GET /v1/audio/voices</c>
    /// that dynamically lists whatever <c>.pt</c> files <paramref name="voicesRoot"/> actually holds at
    /// request time (VoicePackController's collision check always calls it, even on installs that never
    /// collide — see Story398_VoiceIdCollisionRefused.cs's own header remarks for why).</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string voicesRoot)
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal) { [IndexUrl] = IndexJson() };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (slug, voiceIds) in Packs)
        {
            routes[Origin + $"entries/{slug}/{slug}.voice-pack.json"] = ManifestJson(slug, voiceIds);
            routes[Origin + $"entries/{slug}/{slug}.meta.json"] = MetaJson;
            assetBytesByUrl[Origin + $"entries/{slug}/{slug}.preview.mp3"] = PreviewBytes;
            foreach (var voiceId in voiceIds)
                assetBytesByUrl[Origin + $"entries/{slug}/{voiceId}.pt"] = PtBytesFor(slug, voiceId);
        }

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;

            if (string.Equals(absoluteUri, KokoroVoicesUrl, StringComparison.Ordinal))
            {
                var installedVoiceIds = System.IO.Directory.Exists(voicesRoot)
                    ? System.IO.Directory.EnumerateFiles(voicesRoot, "*.pt").Select(f => Path.GetFileNameWithoutExtension(f))
                    : [];
                var voices = installedVoiceIds.ToArray();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { voices }),
                });
            }

            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

// ── The jingle-pack DB-backed arc (PLAN T414) — one real Postgres, one running app, two installed
//    packs, a raw-SQL station.ad_spot.bed_media_id reference ──────────────────────────────────────

[CollectionDefinition(Name)]
public sealed class JinglePackUninstallCollection : ICollectionFixture<JinglePackUninstallArc>
{
    public const string Name = "Story401JinglePackUninstall";
}

/// <summary>
/// Arranges every DB-backed fact this file's own jingle-pack Scenarios read (the
/// <see cref="VoicePackUninstallArc"/> idiom, above, one pack kind over): boots ONE real ephemeral
/// Postgres and ONE real, hosted-service-free app instance, seeds the <c>ads</c> library
/// <c>AdsOptions.LibraryName</c> resolves at install time, installs two fixture packs through the REAL
/// <c>POST /api/jingle-packs/{slug}/install</c> route, inserts a <c>station.ad_spot</c> row whose own
/// <c>bed_media_id</c> names <c>bed-guard-pack</c>'s one asset (simulating "something already points at
/// this background music" without a second HTTP surface this task does not own), then calls the REAL
/// <c>DELETE /api/jingle-packs/{slug}</c> route against both packs — the guarded one first, the clean
/// one second.
/// </summary>
public sealed class JinglePackUninstallArc : IAsyncLifetime
{
    public HttpStatusCode BedGuardDeleteStatus { get; private set; }
    public string BedGuardDeleteBody { get; private set; } = "";
    public long BedGuardAdSpotId { get; private set; }

    public HttpStatusCode CleanDeleteStatus { get; private set; }
    public bool CleanSlugFolderGone { get; private set; }
    public bool CleanMediaRowsGone { get; private set; }

    public HttpStatusCode UnknownSlugDeleteStatus { get; private set; }
    public string UnknownSlugDeleteBody { get; private set; } = "";

    public HttpStatusCode UnauthenticatedInstallStatus { get; private set; }
    public HttpStatusCode UnauthenticatedDeleteStatus { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await JinglePackUninstallDatabase.StartAsync();
        var jingleRoot = Directory.CreateTempSubdirectory("t414-story401-jingle-").FullName;
        try
        {
            await SeedAdsLibraryAsync(database.LibraryConnectionString);

            await using var factory = new JinglePackUninstallWebFactory(database, jingleRoot);

            // F6.7 (T414 review round 2) — install/uninstall both sit behind
            // [Authorize(Policy = AuthorizationPolicies.Settings)]; a client that never logged in must
            // be turned away before either route runs. A slug that was never installed is fine here —
            // auth denial happens in ASP.NET Core's own middleware, before the action (and so before
            // any 404 the controller itself would answer) ever runs.
            var unauthenticatedClient = factory.CreateClient();
            var unauthenticatedInstall = await unauthenticatedClient.PostAsync("/api/jingle-packs/never-installed-jingle-pack/install", null);
            UnauthenticatedInstallStatus = unauthenticatedInstall.StatusCode;
            var unauthenticatedDelete = await unauthenticatedClient.DeleteAsync("/api/jingle-packs/never-installed-jingle-pack");
            UnauthenticatedDeleteStatus = unauthenticatedDelete.StatusCode;

            var client = await JinglePackUninstallWebFactory.LoggedInClientAsync(factory);

            foreach (var slug in JinglePackUninstallFixtures.AllSlugs)
            {
                var install = await client.PostAsync($"/api/jingle-packs/{slug}/install", null);
                if (!install.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"fixture install of '{slug}' failed: {await install.Content.ReadAsStringAsync()}");
            }

            // ── AC4 — an active ad_spot's own bed_media_id blocks the guard pack ──
            var bedMediaId = await ReadMediaIdAsync(
                database.LibraryConnectionString, JinglePackUninstallFixtures.BedGuardSlug, JinglePackUninstallFixtures.BedGuardTitle);
            BedGuardAdSpotId = await InsertAdSpotAsync(database.StationConnectionString, state: "approved", bedMediaId: bedMediaId);

            var bedGuardResponse = await client.DeleteAsync($"/api/jingle-packs/{JinglePackUninstallFixtures.BedGuardSlug}");
            BedGuardDeleteStatus = bedGuardResponse.StatusCode;
            BedGuardDeleteBody = await bedGuardResponse.Content.ReadAsStringAsync();

            // ── AC5 — no active reference uninstalls cleanly ──
            var cleanResponse = await client.DeleteAsync($"/api/jingle-packs/{JinglePackUninstallFixtures.CleanSlug}");
            CleanDeleteStatus = cleanResponse.StatusCode;
            CleanSlugFolderGone = !Directory.Exists(Path.Combine(jingleRoot, JinglePackUninstallFixtures.CleanSlug));
            CleanMediaRowsGone = await CountMediaRowsAsync(database.LibraryConnectionString, JinglePackUninstallFixtures.CleanSlug) == 0;

            // F7 (T414 review round 2) — a slug that was never installed refuses 404, never a 204.
            var unknownSlugResponse = await client.DeleteAsync("/api/jingle-packs/never-installed-jingle-pack");
            UnknownSlugDeleteStatus = unknownSlugResponse.StatusCode;
            UnknownSlugDeleteBody = await unknownSlugResponse.Content.ReadAsStringAsync();
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

    static async Task<long> ReadMediaIdAsync(string libraryConnectionString, string slug, string title)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            "select id from library.media where pack_slug = @slug and title = @title", new { slug, title });
    }

    static async Task<long> InsertAdSpotAsync(string stationConnectionString, string state, long bedMediaId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            with sponsor as (
              insert into station.sponsor (name) values ('Test Brand')
              on conflict on constraint sponsor_pack_slug_name_key do update set name = excluded.name
              returning id
            )
            insert into station.ad_spot (sponsor_id, sponsor_name, title, source, state, bed_media_id)
            select sponsor.id, 'Test Brand', 'Test Spot', 'owner'::station.ad_source, @State::station.ad_state, @BedMediaId
            from sponsor
            returning id
            """,
            new { State = state, BedMediaId = bedMediaId });
    }

    static async Task<int> CountMediaRowsAsync(string libraryConnectionString, string slug)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from library.media where pack_slug = @slug", new { slug });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/>
/// harness — see that type's own remarks. Supplies only the <c>"genwave-t414-401"</c> compose
/// project-name prefix this file's own jingle-pack arc needs (distinct from
/// <see cref="VoicePackUninstallDatabase"/>'s own <c>"genwave-t413-401"</c> prefix, above, and from
/// Story399_JinglePackInstall.cs's own <c>"genwave-t414-399"</c> prefix — every ephemeral Postgres
/// instance in this test project is fully isolated by construction).</summary>
file sealed class JinglePackUninstallDatabase : EphemeralStationDatabase
{
    JinglePackUninstallDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<JinglePackUninstallDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t414-401");
        var db = new JinglePackUninstallDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for <see cref="JinglePackUninstallArc"/> — boots
/// the real Program.cs graph against a REAL ephemeral Postgres (<paramref name="database"/>) with every
/// hosted service removed (no background reach into <c>library.media</c>/<c>station.ad_spot</c> racing
/// this arc's own installs/deletes), the REAL <c>IJinglePackStore</c> (never swapped — this file's
/// whole point is proving the REAL <c>JinglePackRepository.DeleteAsync</c> guard), and
/// <c>Community:CatalogIndexUrl</c> pointed at a fake catalog origin serving two non-colliding fixture
/// packs.
/// </summary>
file sealed class JinglePackUninstallWebFactory(JinglePackUninstallDatabase database, string jingleRoot)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story401-jinglepack-uninstall";

    readonly FakeHttpMessageHandler handler = JinglePackUninstallFixtures.BuildRoutedHandler();

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
        builder.UseSetting("Community:CatalogIndexUrl", JinglePackUninstallFixtures.IndexUrl);
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
/// A two-entry fake catalog origin (mirrors <see cref="VoicePackUninstallFixtures"/>'s own idiom, one
/// pack kind over): <c>bed-guard-pack</c>/<c>Bed Guard Asset</c> for the reference-guard Scenario,
/// <c>clean-jingle-pack</c>/<c>Clean Asset</c> for the happy-path Scenario. Each pack ships exactly one
/// real, ffmpeg-generated WAV asset — mirrors <c>JingleTestAudio</c>'s bytes-are-defined-once idiom in
/// Story399_JinglePackInstall.cs's own <c>JinglePackInstallFixtures</c>.
/// </summary>
file static class JinglePackUninstallFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/jingle-uninstall-index.json";
    const string Origin = "https://catalog.test/repo/";
    const string PackName = "Test Pack";

    public const string BedGuardSlug = "bed-guard-pack";
    public const string BedGuardTitle = "Bed Guard Asset";
    public const string CleanSlug = "clean-jingle-pack";

    static readonly (string Slug, string File, string Title, string Role)[] Packs =
    [
        (BedGuardSlug, "bed-guard-asset.wav", BedGuardTitle, "bed"),
        (CleanSlug, "clean-asset.wav", "Clean Asset", "sting"),
    ];

    public static readonly IReadOnlyList<string> AllSlugs = Packs.Select(p => p.Slug).ToList();

    static readonly IReadOnlyDictionary<string, byte[]> assetBytesByFile = GenerateAssetBytes();

    static IReadOnlyDictionary<string, byte[]> GenerateAssetBytes()
    {
        var dir = JingleTestAudio.NewTempDir();
        try
        {
            return Packs.ToDictionary(
                p => p.File, p => File.ReadAllBytes(JingleTestAudio.CreateBedShapedTone(dir, p.File)), StringComparer.Ordinal);
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

    static string ManifestJson(string file, string title, string role) => $$"""
        { "packName": "{{PackName}}",
          "assets": [ { "file": "{{file}}", "sha256": "{{Sha256Hex(AssetBytes(file))}}", "role": "{{role}}", "title": "{{title}}", "license": "CC0" } ] }
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A jingle pack for the uninstall-guard specs.","audience":"everyone"}
        """;

    static string EntryJson(string slug, string file, string title, string role) => $$"""
        { "slug": "{{slug}}", "kind": "jingle-pack", "audience": "everyone",
          "manifest": { "path": "entries/{{slug}}/{{slug}}.jingle-pack.json", "sha256": "{{Sha256Hex(ManifestJson(file, title, role))}}" },
          "meta": { "path": "entries/{{slug}}/{{slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
          "assets": [
            { "path": "entries/{{slug}}/{{file}}", "sha256": "{{Sha256Hex(AssetBytes(file))}}", "bytes": {{AssetBytes(file).Length}} }
          ] }
        """;

    static string IndexJson() => $$"""
        { "generatedAt": "2026-09-06", "entries": [
            {{string.Join(", ", Packs.Select(p => EntryJson(p.Slug, p.File, p.Title, p.Role)))}}
          ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal) { [IndexUrl] = IndexJson() };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (slug, file, title, role) in Packs)
        {
            routes[Origin + $"entries/{slug}/{slug}.jingle-pack.json"] = ManifestJson(file, title, role);
            routes[Origin + $"entries/{slug}/{slug}.meta.json"] = MetaJson;
            assetBytesByUrl[Origin + $"entries/{slug}/{file}"] = AssetBytes(file);
        }

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
