// STORY-392 — I manage the Ads library (the Briefs half · F162.1 · F162.2 · PLAN T403b)
// The Briefs tab's page half (AC5 in a browser) lives outside this repo's server-side specs.
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres — the Story374/Story382/Story392(T403) arc idiom): every fact drives
// GET/POST/PATCH /api/ad-briefs* over HTTP with an authed admin session, never AdBriefRepository/
// AdBriefsController directly. One arc (AdBriefsApiArc) arranges everything every HAPPY-PATH/sad-path
// Scenario below reads (the SAME "arrange once, many read-only Scenarios" idiom Story392's own
// AdsApiArc already establishes one controller over); the admin-surface posture Scenario needs no
// real database at all (SurfaceGateMiddleware 404s before any store is ever touched), so it gets its
// own, DB-less factory (the Story166/Story374/Story392(T403) precedent).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdBriefsApi
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — through the production surface (WebApplicationFactory)
    // ---------------------------------------------------------------------

    [Collection(AdBriefsApiCollection.Name)]
    public sealed class ScenarioTheListShowsEveryBrief(AdBriefsApiArc arc)
    {
        [Fact]
        public void BothPackAndOwnerRowsAppear()
        {
            // GET /api/ad-briefs (F162.1's Briefs tab) — the SEEDED pack row and the POSTed owner row
            // both come back, bare array, no paging envelope (T403b's own YAGNI call).
            Assert.Equal(HttpStatusCode.OK, arc.ListStatus);
            Assert.Contains(arc.ListBrands, b => b is (AdBriefsApiArc.SeededPackBrand, AdBriefsApiArc.SeededPackSlug));
            Assert.Contains(arc.ListBrands, b => b is (AdBriefsApiArc.CleanOwnerBrand, null));
        }
    }

    [Collection(AdBriefsApiCollection.Name)]
    public sealed class ScenarioPostCreatesAnOwnerBrief(AdBriefsApiArc arc)
    {
        [Fact]
        public void ACleanCreatePostsAndReadsBackEveryFieldVerbatim()
        {
            // POST /api/ad-briefs {brand,premise,tone,structure,enabled} → 201, every field
            // round-trips byte-for-byte, pack_slug is null (owner-only creation).
            Assert.Equal(HttpStatusCode.Created, arc.CleanOwnerPostStatus);
            Assert.Equal(AdBriefsApiArc.CleanOwnerBrand, arc.RoundTripBrand);
            Assert.Equal(AdBriefsApiArc.CleanOwnerPremise, arc.RoundTripPremise);
            Assert.Equal(AdBriefsApiArc.CleanOwnerTone, arc.RoundTripTone);
            Assert.Equal(AdBriefsApiArc.CleanOwnerStructure, arc.RoundTripStructure);
            Assert.True(arc.RoundTripEnabled);
            Assert.Null(arc.RoundTripPackSlug);
        }

        [Fact]
        public void ADuplicateOwnerBrandIs409()
        {
            // A second POST for the SAME brand — the ratified one-owner-per-brand cap (SPEC F159.1
            // rider) surfaces as 409, never a silent update.
            Assert.Equal(HttpStatusCode.Conflict, arc.DuplicateOwnerPostStatus);
            Assert.Equal("brand", arc.DuplicateOwnerPostField);
        }

        [Fact]
        public void ABrandThatOnlyHasAPackBriefCoexists()
        {
            // A brand already carrying a PACK brief — an owner brief for the SAME brand name is a
            // SEPARATE row (the cap is scoped to (pack_slug, brand), not brand alone: NULLS NOT
            // DISTINCT makes (NULL, brand) distinct from (slug, brand) — verified against the REAL
            // constraint, not asserted from memory).
            Assert.Equal(HttpStatusCode.Created, arc.CoexistingOwnerPostStatus);
        }

        [Fact]
        public void AnOmittedEnabledDefaultsToTrue()
        {
            // review F1: the SAME coexisting-brand POST omits `enabled` entirely — the add form's own
            // "new briefs are live by default" posture (AdBriefCreateRequest's own remarks) pinned
            // against the real response body, not merely asserted from the controller's own doc
            // comment.
            Assert.True(arc.CoexistingOwnerEnabled);
        }

        [Fact]
        public void AWhitespaceOnlyPremiseFoldsToNull()
        {
            // review F3: the SAME coexisting-brand POST also sends a whitespace-only premise — reads
            // back null, never the literal spaces.
            Assert.Null(arc.CoexistingOwnerPremise);
        }

        [Fact]
        public void ABlankBrandIs400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.BlankBrandPostStatus);
        }
    }

    [Collection(AdBriefsApiCollection.Name)]
    public sealed class ScenarioPatchTogglesAnyBrief(AdBriefsApiArc arc)
    {
        [Fact]
        public void PatchDisablesAnOwnerBrief()
        {
            Assert.Equal(HttpStatusCode.OK, arc.PatchOwnerStatus);
            Assert.False(arc.PatchOwnerResultEnabled);
        }

        [Fact]
        public void PatchEnablesAPackBrief()
        {
            // The toggle is the operator's own lever over pack content too — only CREATE is
            // owner-only (PLAN T403b's own reading of F162.1).
            Assert.Equal(HttpStatusCode.OK, arc.PatchPackStatus);
            Assert.True(arc.PatchPackResultEnabled);
            Assert.Equal(AdBriefsApiArc.SeededPackSlug, arc.PatchPackResultPackSlug);
        }

        [Fact]
        public void PatchWithNoEnabledFieldIs400()
        {
            Assert.Equal(HttpStatusCode.BadRequest, arc.PatchMissingEnabledStatus);
        }

        [Fact]
        public void PatchOnAnUnknownIdIs404()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.PatchUnknownIdStatus);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the admin surface gates the Briefs tab too
    // ---------------------------------------------------------------------

    public sealed class ScenarioAdminSurfacePosture
    {
        [Fact]
        public async Task EveryAdBriefsRouteIs404WhileAdminIsDisabled()
        {
            // Admin:Enabled=false: /api/ad-briefs* 404s like every admin route (F162.1). No real
            // database needed — SurfaceGateMiddleware refuses before any store is ever touched (the
            // Story166/Story374/Story392(T403) DB-less-factory precedent).
            await using var factory = new AdBriefsAdminOffWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var routes = new (HttpMethod Method, string Path, HttpContent? Body)[]
            {
                (HttpMethod.Get, "/api/ad-briefs", null),
                (HttpMethod.Post, "/api/ad-briefs", JsonContent.Create(new { })),
                (HttpMethod.Patch, "/api/ad-briefs/1", JsonContent.Create(new { })),
            };

            foreach (var (method, path, body) in routes)
            {
                var request = new HttpRequestMessage(method, path) { Content = body };
                var response = await client.SendAsync(request);
                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{method} {path} returned {(int)response.StatusCode} with Admin:Enabled=false.");
            }
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory shared by every happy-path/sad-path
// Scenario above (the Story374/Story382/Story392(T403) "arrange once, many read-only Scenarios"
// idiom). ──

[CollectionDefinition(Name)]
public sealed class AdBriefsApiCollection : ICollectionFixture<AdBriefsApiArc>
{
    public const string Name = "Story392AdBriefsApi";
}

/// <summary>
/// Arranges every fact STORY-392's Briefs-API Scenarios read, entirely over the REAL production HTTP
/// pipeline with a real admin session — no <c>AdBriefRepository</c> call, no
/// <c>AdBriefsController</c> call, anywhere in this class. The pack brief is seeded directly via raw
/// SQL (the <c>GardenerRotFixtures</c>/<c>AdsWireFixtures</c> precedent): no pack-install endpoint
/// exists yet, so a pack row can only ever be arranged independently of the API under test.
/// </summary>
public sealed class AdBriefsApiArc : IAsyncLifetime
{
    public const string SeededPackSlug = "genwave-catalog";
    public const string SeededPackBrand = "Widget Bros";

    public const string CleanOwnerBrand = "Cravin's Diner";
    public const string CleanOwnerPremise = "A cozy neighborhood diner";
    public const string CleanOwnerTone = "warm";
    public const string CleanOwnerStructure = "hook-offer-cta";

    const string CoexistingBrand = "Shared Brand Co";

    public HttpStatusCode ListStatus { get; private set; }
    public IReadOnlyList<(string Brand, string? PackSlug)> ListBrands { get; private set; } = [];

    public HttpStatusCode CleanOwnerPostStatus { get; private set; }
    public string RoundTripBrand { get; private set; } = "";
    public string? RoundTripPremise { get; private set; }
    public string? RoundTripTone { get; private set; }
    public string? RoundTripStructure { get; private set; }
    public bool RoundTripEnabled { get; private set; }
    public string? RoundTripPackSlug { get; private set; }

    public HttpStatusCode DuplicateOwnerPostStatus { get; private set; }
    public string? DuplicateOwnerPostField { get; private set; }

    public HttpStatusCode CoexistingOwnerPostStatus { get; private set; }
    public bool CoexistingOwnerEnabled { get; private set; }
    public string? CoexistingOwnerPremise { get; private set; }

    public HttpStatusCode BlankBrandPostStatus { get; private set; }

    public HttpStatusCode PatchOwnerStatus { get; private set; }
    public bool PatchOwnerResultEnabled { get; private set; }

    public HttpStatusCode PatchPackStatus { get; private set; }
    public bool PatchPackResultEnabled { get; private set; }
    public string? PatchPackResultPackSlug { get; private set; }

    public HttpStatusCode PatchMissingEnabledStatus { get; private set; }
    public HttpStatusCode PatchUnknownIdStatus { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story392AdBriefsDatabase is file-local (CS9051), the identical
        // reason Story374's/Story382's/Story392(T403)'s own arcs give for the same shape.
        await using var database = await Story392AdBriefsDatabase.StartAsync();

        var packId = await AdBriefWireFixtures.InsertPackBriefAsync(
            database.StationConnectionString, SeededPackSlug, SeededPackBrand);

        await using var factory = new Story392AdBriefsWebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = Story392AdBriefsWebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // ── The clean owner create: the editor round-trip fact. ──
        var createPayload = new
        {
            brand = CleanOwnerBrand,
            premise = CleanOwnerPremise,
            tone = CleanOwnerTone,
            structure = CleanOwnerStructure,
            enabled = true,
        };
        var createResponse = await client.PostAsJsonAsync("/api/ad-briefs", createPayload);
        CleanOwnerPostStatus = createResponse.StatusCode;
        var created = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var cleanOwnerId = created.RootElement.GetProperty("id").GetInt64();
        RoundTripBrand = created.RootElement.GetProperty("brand").GetString() ?? "";
        RoundTripPremise = created.RootElement.GetProperty("premise").GetString();
        RoundTripTone = created.RootElement.GetProperty("tone").GetString();
        RoundTripStructure = created.RootElement.GetProperty("structure").GetString();
        RoundTripEnabled = created.RootElement.GetProperty("enabled").GetBoolean();
        RoundTripPackSlug = created.RootElement.GetProperty("packSlug").ValueKind == JsonValueKind.Null
            ? null : created.RootElement.GetProperty("packSlug").GetString();

        // ── The list: both the seeded pack row and the just-created owner row appear. ──
        var listResponse = await client.GetAsync("/api/ad-briefs");
        ListStatus = listResponse.StatusCode;
        var listBody = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        ListBrands = listBody.RootElement.EnumerateArray()
            .Select(item => (
                item.GetProperty("brand").GetString() ?? "",
                item.GetProperty("packSlug").ValueKind == JsonValueKind.Null
                    ? (string?)null : item.GetProperty("packSlug").GetString()))
            .ToList();

        // ── A duplicate owner create for the SAME brand — refused, 409, never a silent update. ──
        var duplicateResponse = await client.PostAsJsonAsync("/api/ad-briefs", new
        {
            brand = CleanOwnerBrand,
            premise = "A different premise that must never land",
        });
        DuplicateOwnerPostStatus = duplicateResponse.StatusCode;
        var duplicateBody = await JsonDocument.ParseAsync(await duplicateResponse.Content.ReadAsStreamAsync());
        DuplicateOwnerPostField = duplicateBody.RootElement.TryGetProperty("field", out var fieldProperty)
            ? fieldProperty.GetString() : null;

        // ── An owner create for a brand that ALREADY has a pack brief — coexists, 201, two separate
        // rows (the (pack_slug, brand) key, not brand alone). The pack row is seeded first, ahead of
        // the owner POST, so the "brand already has a pack row" precondition is independent of the
        // API under test. This SAME POST also carries review findings F1/F3 (folded in, not a
        // separate call): `enabled` is OMITTED entirely — pins the "omitted defaults to true" ruling
        // against the real response body, not merely the controller's own doc comment — and
        // `premise` is whitespace-only — pins that it folds to null on the wire, never the literal
        // spaces. ──
        await AdBriefWireFixtures.InsertPackBriefAsync(
            database.StationConnectionString, "another-pack-slug", CoexistingBrand);
        var coexistingResponse = await client.PostAsJsonAsync(
            "/api/ad-briefs", new { brand = CoexistingBrand, premise = "   " });
        CoexistingOwnerPostStatus = coexistingResponse.StatusCode;
        var coexistingBody = await JsonDocument.ParseAsync(await coexistingResponse.Content.ReadAsStreamAsync());
        CoexistingOwnerEnabled = coexistingBody.RootElement.GetProperty("enabled").GetBoolean();
        CoexistingOwnerPremise = coexistingBody.RootElement.GetProperty("premise").ValueKind == JsonValueKind.Null
            ? null : coexistingBody.RootElement.GetProperty("premise").GetString();

        // ── A blank brand is refused. ──
        var blankBrandResponse = await client.PostAsJsonAsync("/api/ad-briefs", new { brand = "  " });
        BlankBrandPostStatus = blankBrandResponse.StatusCode;

        // ── PATCH disables the owner brief just created. ──
        var patchOwnerResponse = await client.PatchAsync(
            $"/api/ad-briefs/{cleanOwnerId}", JsonContent.Create(new { enabled = false }));
        PatchOwnerStatus = patchOwnerResponse.StatusCode;
        var patchOwnerBody = await JsonDocument.ParseAsync(await patchOwnerResponse.Content.ReadAsStreamAsync());
        PatchOwnerResultEnabled = patchOwnerBody.RootElement.GetProperty("enabled").GetBoolean();

        // ── PATCH enables the SEEDED pack brief. ──
        var patchPackResponse = await client.PatchAsync(
            $"/api/ad-briefs/{packId}", JsonContent.Create(new { enabled = true }));
        PatchPackStatus = patchPackResponse.StatusCode;
        var patchPackBody = await JsonDocument.ParseAsync(await patchPackResponse.Content.ReadAsStreamAsync());
        PatchPackResultEnabled = patchPackBody.RootElement.GetProperty("enabled").GetBoolean();
        PatchPackResultPackSlug = patchPackBody.RootElement.GetProperty("packSlug").GetString();

        // ── PATCH with no `enabled` field is a 400. ──
        var patchMissingResponse = await client.PatchAsync(
            $"/api/ad-briefs/{cleanOwnerId}", JsonContent.Create(new { }));
        PatchMissingEnabledStatus = patchMissingResponse.StatusCode;

        // ── PATCH against an unknown id is a 404. ──
        var patchUnknownResponse = await client.PatchAsync(
            "/api/ad-briefs/999999", JsonContent.Create(new { enabled = true }));
        PatchUnknownIdStatus = patchUnknownResponse.StatusCode;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story374/Story382/
// Story392(T403) "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres with every hosted
/// service removed — no background reach into <c>station.ad_brief</c>, so this arc's own seeded/posted
/// rows are never raced by a background tick. Every <c>AdBriefsController</c> endpoint is still
/// reachable — only the BACKGROUND loops are removed, the same <c>Story392AdsWebFactory</c> idiom one
/// controller over.
/// </summary>
file sealed class Story392AdBriefsWebFactory(Story392AdBriefsDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t403b-briefs-api";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// STORY-392's own DB-less factory — a bogus <c>ConnectionStrings:*</c> (never actually reached:
/// <c>Admin:Enabled=false</c> 404s in <c>SurfaceGateMiddleware</c>, BEFORE routing ever reaches
/// <c>AdBriefsController</c>'s constructor) — no real ephemeral Postgres needed just to prove a 404
/// (the <c>Story374.GardenerSurfaceWebFactory</c>/<c>Story166.KillSwitchWebFactory</c>/
/// <c>Story392(T403).AdsAdminOffWebFactory</c> precedent).
/// </summary>
file sealed class AdBriefsAdminOffWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-t403b-briefs-admin-off");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness — see
/// that type's own remarks for the full "which compose file, why a unique project name + OS-assigned
/// port" rationale. Supplies only the <c>"genwave-t403b"</c> compose project-name prefix this file's
/// own arc needs.
/// </summary>
file sealed class Story392AdBriefsDatabase : EphemeralStationDatabase
{
    Story392AdBriefsDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story392AdBriefsDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t403b");
        var db = new Story392AdBriefsDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange helpers this file's own arc uses — raw SQL against the ephemeral database's own
/// connection string, never through <c>AdBriefRepository</c> (the <c>GardenerRotFixtures</c>/
/// <c>AdsWireFixtures</c> precedent): no pack-install endpoint exists yet, so a pack row is seeded
/// independently of the API under test.</summary>
public static class AdBriefWireFixtures
{
    public static async Task<long> InsertPackBriefAsync(string stationConnectionString, string packSlug, string brand)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into station.ad_brief (pack_slug, brand, premise, tone, structure, enabled)
            values (@packSlug, @brand, 'Seeded pack premise', 'dry', null, false)
            returning id
            """;
        cmd.Parameters.AddWithValue("packSlug", packSlug);
        cmd.Parameters.AddWithValue("brand", brand);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }
}
